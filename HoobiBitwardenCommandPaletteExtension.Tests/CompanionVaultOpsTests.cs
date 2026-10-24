using System.IO;
using System.Text.Json;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class CompanionVaultOpsTests
{
  private static readonly string LoginId = Guid.NewGuid().ToString();
  private static readonly string NoteId = Guid.NewGuid().ToString();
  private static readonly string CardId = Guid.NewGuid().ToString();
  private static readonly string TrashId = Guid.NewGuid().ToString();
  private static readonly string ArchivedId = Guid.NewGuid().ToString();

  private sealed class Harness
  {
    public BitwardenCliService Service { get; } = new();
    public CompanionOps Ops { get; }
    public int Loads { get; private set; }
    public Func<Task<VaultExtras?>> Load { get; set; }
    public BitwardenItem Trash { get; } = new()
    {
      Id = TrashId,
      Name = "Old login",
      Type = BitwardenItemType.Login,
      Username = "former",
      Password = ProtectedSecret.From("trash-secret"),
      DeletedDate = DateTime.UtcNow,
    };

    public Harness()
    {
      Service.LoadTestData(
      [
        new BitwardenItem
        {
          Id = LoginId,
          Name = "GitHub",
          Type = BitwardenItemType.Login,
          Reprompt = 1,
          Favorite = true,
          FolderId = "f1",
          OrganizationId = "o1",
          CollectionIds = ["c1"],
          Username = "octocat",
          Password = ProtectedSecret.From("hunter2"),
          HasTotp = true,
          TotpSecret = ProtectedSecret.From("JBSWY3DPEHPK3PXP"),
          Uris = [new ItemUri("https://www.github.com/login", UriMatchType.Default), new ItemUri("github.com", UriMatchType.Default), new ItemUri("gist.github.com", UriMatchType.Default)],
        },
        new BitwardenItem { Id = NoteId, Name = "Note", Type = BitwardenItemType.SecureNote, Notes = ProtectedSecret.From("secret note body") },
        new BitwardenItem { Id = CardId, Name = "Visa", Type = BitwardenItemType.Card, CardBrand = "Visa", CardNumber = ProtectedSecret.From("4111111111111111"), CardCode = ProtectedSecret.From("987") },
      ],
        new() { ["f2"] = "Personal", ["f1"] = "Work" },
        new() { ["o1"] = "Acme" });
      Service.SetSession("session-key");
      Load = () =>
      {
        Loads++;
        return Task.FromResult<VaultExtras?>(new VaultExtras(
          [Trash, new BitwardenItem { Id = ArchivedId, Name = "Archived", Type = BitwardenItemType.Identity, IdentityEmail = "a@b.c", ArchivedDate = DateTime.UtcNow }],
          [new VaultCollection("c2", "Shared", "o1"), new VaultCollection("c1", "Engineering", "o1")]));
      };
      Ops = new CompanionOps(Service, loadExtras: () => Load());
    }
  }

  [Fact]
  public async Task ListItems_IsSecretFree_AndMatchesThePaletteSubtitles()
  {
    var h = new Harness();

    var list = await h.Ops.ListItemsAsync();
    var json = JsonSerializer.Serialize(list, IpcJson.Default.ItemListResponse);

    foreach (var secret in new[] { "hunter2", "JBSWY3DPEHPK3PXP", "secret note body", "4111111111111111", "987", "trash-secret" })
      Assert.DoesNotContain(secret, json, StringComparison.Ordinal);

    var login = list.Items.Single(i => i.Id == LoginId);
    Assert.Equal("octocat", login.Subtext);
    Assert.True(login.Reprompt);
    Assert.True(login.Favorite);
    Assert.True(login.HasTotp);
    Assert.Equal(["c1"], login.CollectionIds);
    Assert.Equal(["github.com", "gist.github.com"], login.Hosts);
    Assert.False(login.Deleted);

    var note = list.Items.Single(i => i.Id == NoteId);
    Assert.Null(note.Subtext);
    Assert.True(note.HasNotes);
    Assert.Equal("Visa ····1111", list.Items.Single(i => i.Id == CardId).Subtext);

    var trash = list.Items.Single(i => i.Id == TrashId);
    Assert.True(trash.Deleted);
    Assert.Equal("former", trash.Subtext);
    var archived = list.Items.Single(i => i.Id == ArchivedId);
    Assert.True(archived.Archived);
    Assert.Equal("a@b.c", archived.Subtext);
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
  public async Task Extras_AreLoadedOnce_ServeTheDetailPane_AndAreWipedOnLock()
  {
    var h = new Harness();
    var locked = 0;
    h.Ops.Locked += () => locked++;

    await h.Ops.ListItemsAsync();
    await h.Ops.ListFiltersAsync();
    Assert.Equal(1, h.Loads);

    Assert.Equal("Old login", h.Ops.GetItem(TrashId).Name);
    Assert.Equal("trash-secret", h.Ops.Reveal(TrashId, ItemFields.Password));

    h.Service.ClearSession();
    h.Ops.RaiseIfLocked();

    Assert.Equal(1, locked);
    Assert.Throws<ObjectDisposedException>(() => h.Trash.Password!.Reveal());
    Assert.Equal(IpcErrors.Locked, (await Assert.ThrowsAsync<IpcException>(h.Ops.ListItemsAsync)).Code);

    h.Service.SetSession("session-key");
    Assert.Equal(IpcErrors.NotFound, Assert.Throws<IpcException>(() => h.Ops.GetItem(TrashId)).Code);
  }

  [Fact]
  public async Task Extras_LoadFailure_StillListsTheCache_AndALockDuringLoadIsReported()
  {
    var h = new Harness { Load = () => throw new InvalidOperationException("bw failed") };

    var list = await h.Ops.ListItemsAsync();
    Assert.Equal(3, list.Items.Count);

    h.Load = () => Task.FromResult<VaultExtras?>(null);
    Assert.Equal(IpcErrors.Locked, (await Assert.ThrowsAsync<IpcException>(h.Ops.ListItemsAsync)).Code);
  }

  [Fact]
  public async Task HandleAsync_RoutesTheListOps()
  {
    var h = new Harness();
    var ct = TestContext.Current.CancellationToken;

    var items = IpcJson.Read(await h.Ops.HandleAsync(Ops.ListItems, null, ct), IpcJson.Default.ItemListResponse);
    var filters = IpcJson.Read(await h.Ops.HandleAsync(Ops.ListFilters, null, ct), IpcJson.Default.FiltersResponse);

    Assert.Equal(5, items.Items.Count);
    Assert.Equal(2, filters.Folders.Count);
  }

  [Fact]
  public async Task VaultChanged_IsRaisedWhenTheCacheRefreshes()
  {
    var service = new BitwardenCliService(processFactory: new ScriptedCliFactory().Create);
    service.SetSession("session-key");
    using var ops = new CompanionOps(service, loadExtras: () => Task.FromResult<VaultExtras?>(null));
    var raised = 0;
    ops.VaultChanged += () => raised++;

    await service.RefreshCacheAsync();

    Assert.Equal(1, raised);
  }

  [Fact]
  public void Summarize_UsesACachedFaviconOnlyWhenIconsAreOn()
  {
    var host = $"{Guid.NewGuid():N}.test";
    var item = new BitwardenItem { Id = LoginId, Type = BitwardenItemType.Login, Uris = [new ItemUri($"https://{host}", UriMatchType.Default)] };
    Assert.Null(CompanionOps.Summarize(item, true).IconPath);

    Directory.CreateDirectory(FaviconService.CacheDir);
    var path = Path.Combine(FaviconService.CacheDir, host + ".png");
    File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
    try
    {
      Assert.Equal(path, CompanionOps.Summarize(item, true).IconPath);
      Assert.Null(CompanionOps.Summarize(item, false).IconPath);
    }
    finally
    {
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
  public async Task LoadVaultExtras_KeepsOnlyTrashAndArchivedItems_AndParsesCollections()
  {
    var service = new BitwardenCliService(processFactory: psi => new FakeCliProcess(stdout: psi.Arguments switch
    {
      "list items --trash" => $"[{{\"id\":\"{TrashId}\",\"name\":\"Old\",\"type\":1,\"deletedDate\":\"2025-01-01T00:00:00Z\",\"collectionIds\":[\"c1\"],\"login\":{{\"password\":\"p\"}}}}]\n",
      "list items --archived" => $"[{{\"id\":\"{ArchivedId}\",\"name\":\"Kept\",\"type\":2,\"archivedDate\":\"2025-01-01T00:00:00Z\"}},{{\"id\":\"{LoginId}\",\"name\":\"Active\",\"type\":1}}]\n",
      "list collections" => "[{\"id\":\"c1\",\"name\":\"Engineering\",\"organizationId\":\"o1\"},{\"id\":\"\",\"name\":\"bad\"}]\n",
      _ => "[]\n",
    }));
    service.SetSession("session-key");

    var extras = await service.LoadVaultExtrasAsync();

    Assert.NotNull(extras);
    Assert.Equal([TrashId, ArchivedId], extras.Items.Select(i => i.Id));
    Assert.Equal(["c1"], extras.Items[0].CollectionIds);
    Assert.NotNull(extras.Items[0].DeletedDate);
    Assert.NotNull(extras.Items[1].ArchivedDate);
    Assert.Equal(new VaultCollection("c1", "Engineering", "o1"), Assert.Single(extras.Collections));
    Assert.Empty(BitwardenCliService.ParseCollections("not json"));
    Assert.Empty(BitwardenCliService.ParseCollections("{}"));
  }
}
