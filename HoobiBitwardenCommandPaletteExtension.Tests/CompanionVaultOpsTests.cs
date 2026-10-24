using System.IO;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class CompanionVaultOpsTests
{
  private static readonly string LoginId = Guid.NewGuid().ToString();
  private static readonly string CardId = Guid.NewGuid().ToString();

  private sealed class Harness
  {
    public BitwardenCliService Service { get; } = new();
    public CompanionOps Ops { get; }
    public int Loads { get; private set; }
    public Func<Task<List<VaultCollection>?>> Load { get; set; }

    public Harness()
    {
      Service.LoadTestData(
      [
        new BitwardenItem { Id = LoginId, Name = "GitHub", Type = BitwardenItemType.Login, Username = "octocat", Password = ProtectedSecret.From("hunter2") },
        new BitwardenItem { Id = CardId, Name = "Visa", Type = BitwardenItemType.Card, CardBrand = "Visa", CardNumber = ProtectedSecret.From("4111111111111111") },
      ],
        new() { ["f2"] = "Personal", ["f1"] = "Work" },
        new() { ["o1"] = "Acme" });
      Service.SetSession("session-key");
      Load = () =>
      {
        Loads++;
        return Task.FromResult<List<VaultCollection>?>([new VaultCollection("c2", "Shared", "o1"), new VaultCollection("c1", "Engineering", "o1")]);
      };
      Ops = new CompanionOps(Service, loadCollections: () => Load());
    }
  }

  [Fact]
  public async Task ListFilters_ReturnsSortedFoldersCollectionsAndOrganizations()
  {
    var h = new Harness();

    var filters = await h.Ops.ListFiltersAsync();

    Assert.Equal(["Personal", "Work"], filters.Folders.Select(f => f.Name));
    Assert.Equal(["Engineering", "Shared"], filters.Collections.Select(c => c.Name));
    Assert.All(filters.Collections, c => Assert.Equal("o1", c.OrganizationId));
    Assert.Equal("Acme", Assert.Single(filters.Organizations).Name);
  }

  [Fact]
  public async Task Collections_AreLoadedOnce_AndAreDroppedOnLock()
  {
    var h = new Harness();
    var locked = 0;
    h.Ops.Locked += () => locked++;

    await h.Ops.ListFiltersAsync();
    await h.Ops.ListFiltersAsync();
    Assert.Equal(1, h.Loads);

    h.Service.ClearSession();
    h.Ops.RaiseIfLocked();

    Assert.Equal(1, locked);
    Assert.Equal(IpcErrors.Locked, (await Assert.ThrowsAsync<IpcException>(h.Ops.ListFiltersAsync)).Code);

    h.Service.SetSession("session-key");
    await h.Ops.ListFiltersAsync();
    Assert.Equal(2, h.Loads);
  }

  [Fact]
  public async Task Collections_LoadFailure_StillListsFolders_AndALockDuringLoadIsReported()
  {
    var h = new Harness { Load = () => throw new InvalidOperationException("bw failed") };

    var filters = await h.Ops.ListFiltersAsync();
    Assert.Empty(filters.Collections);
    Assert.Equal(2, filters.Folders.Count);

    h.Load = () => Task.FromResult<List<VaultCollection>?>(null);
    Assert.Equal(IpcErrors.Locked, (await Assert.ThrowsAsync<IpcException>(h.Ops.ListFiltersAsync)).Code);
  }

  [Fact]
  public async Task HandleAsync_RoutesListFilters()
  {
    var h = new Harness();

    var filters = IpcJson.Read(await h.Ops.HandleAsync(Ops.ListFilters, null, TestContext.Current.CancellationToken), IpcJson.Default.FiltersResponse);

    Assert.Equal(2, filters.Folders.Count);
  }

  [Fact]
  public async Task VaultChanged_IsRaisedWhenTheCacheRefreshes()
  {
    var service = new BitwardenCliService(processFactory: new ScriptedCliFactory().Create);
    service.SetSession("session-key");
    using var ops = new CompanionOps(service, loadCollections: () => Task.FromResult<List<VaultCollection>?>(null));
    var raised = 0;
    ops.VaultChanged += () => raised++;

    await service.RefreshCacheAsync();

    Assert.Equal(1, raised);
  }

  [Fact]
  public void GetItem_SendsACachedFaviconOnlyWhenOneExists()
  {
    var h = new Harness();
    var host = $"{Guid.NewGuid():N}.test";
    var id = Guid.NewGuid().ToString();
    h.Service.LoadTestData([new BitwardenItem { Id = id, Name = "Site", Type = BitwardenItemType.Login, Uris = [new ItemUri($"https://{host}", UriMatchType.Default)] }], [], []);
    Assert.Null(h.Ops.GetItem(id).IconPath);

    Directory.CreateDirectory(FaviconService.CacheDir);
    var path = Path.Combine(FaviconService.CacheDir, host + ".png");
    File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
    try
    {
      Assert.Equal(path, h.Ops.GetItem(id).IconPath);
    }
    finally
    {
      File.Delete(path);
    }
  }

  [Fact]
  public void CardBrandIcon_IsSentInTheDetail()
  {
    var h = new Harness();
    var card = new BitwardenItem { Id = CardId, Type = BitwardenItemType.Card, CardBrand = "Visa" };
    var key = Helpers.VaultItemHelper.IconCacheKey(card);
    Assert.StartsWith("card-brand:visa:", key, StringComparison.Ordinal);
    Assert.Null(Helpers.VaultItemHelper.IconCacheKey(new BitwardenItem { Type = BitwardenItemType.SecureNote }));

    Directory.CreateDirectory(FaviconService.CacheDir);
    var path = Path.Combine(FaviconService.CacheDir, key!.Replace(':', '_') + ".png");
    var existed = File.Exists(path);
    if (!existed)
      File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
    try
    {
      Assert.Equal(path, h.Ops.GetItem(CardId).IconPath);
    }
    finally
    {
      if (!existed)
        File.Delete(path);
    }
  }

  [Fact]
  public void CompanionHost_NotifyVaultChanged_DoesNotStartTheCompanion()
  {
    using var host = new CompanionHost(new CompanionOps(new BitwardenCliService()));
    host.NotifyVaultChanged();
    Assert.Null(host.Process);
  }

  [Fact]
  public async Task LoadCollections_ParsesCollections_AndSkipsInvalidEntries()
  {
    var service = new BitwardenCliService(processFactory: psi => new FakeCliProcess(stdout: psi.Arguments == "list collections"
      ? "[{\"id\":\"c1\",\"name\":\"Engineering\",\"organizationId\":\"o1\"},{\"id\":\"\",\"name\":\"bad\"}]\n"
      : "[]\n"));
    service.SetSession("session-key");

    var collections = await service.LoadCollectionsAsync();

    Assert.Equal(new VaultCollection("c1", "Engineering", "o1"), Assert.Single(collections!));
    Assert.Empty(BitwardenCliService.ParseCollections("not json"));
    Assert.Empty(BitwardenCliService.ParseCollections("{}"));
  }
}
