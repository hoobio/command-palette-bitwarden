using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Pages;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class BitwardenFallbackItemTests
{
  private static List<BitwardenItem> TestItems() =>
  [
    new BitwardenItem { Id = "1", Name = "GitHub", Type = BitwardenItemType.Login, Username = "octocat" },
  ];

  [Fact]
  public void UpdateQuery_CacheLoadedButVaultNotUnlocked_ShowsNothing()
  {
    var factory = new FakeProcessFactory();
    var svc = new BitwardenCliService(processFactory: factory.Create);
    svc.LoadTestData(TestItems(), []);
    factory.Enqueue(new FakeCliProcess(stdout: "", exitCode: 1));
    using var fallback = new BitwardenFallbackItem(svc);

    fallback.UpdateQuery("github");

    Assert.Equal(string.Empty, fallback.Title);
  }

  [Fact]
  public async Task UpdateQuery_UnlockedVault_ShowsSingleMatch()
  {
    var factory = new FakeProcessFactory();
    var svc = new BitwardenCliService(processFactory: factory.Create);
    factory.Enqueue(new FakeCliProcess(stdout: "session_key\n", exitCode: 0));
    await svc.UnlockAsync("pw");
    svc.LoadTestData(TestItems(), []);
    using var fallback = new BitwardenFallbackItem(svc);

    fallback.UpdateQuery("github");

    Assert.Equal("GitHub", fallback.Title);
  }

  [Fact]
  public async Task UpdateQuery_RepromptItem_DefaultCommandRequiresReprompt()
  {
    RepromptPage.ClearGracePeriod();
    var factory = new FakeProcessFactory();
    var svc = new BitwardenCliService(processFactory: factory.Create);
    factory.Enqueue(new FakeCliProcess(stdout: "session_key\n", exitCode: 0));
    await svc.UnlockAsync("pw");
    svc.LoadTestData([new BitwardenItem { Id = "1", Name = "GitHub", Type = BitwardenItemType.Login, Reprompt = 1, Uris = [new ItemUri("https://github.com", UriMatchType.Default)] }], []);
    using var fallback = new BitwardenFallbackItem(svc);

    fallback.UpdateQuery("github");

    Assert.IsType<RepromptPage>(fallback.Command);
  }

  [Fact]
  public async Task UpdateQuery_LockedVault_StartsNoCliProcess()
  {
    var starts = 0;
    var svc = new BitwardenCliService(processFactory: psi =>
    {
      Interlocked.Increment(ref starts);
      throw new InvalidOperationException(psi.Arguments);
    });
    using var fallback = new BitwardenFallbackItem(svc);

    fallback.UpdateQuery("github");
    await Task.Delay(200, TestContext.Current.CancellationToken);

    Assert.Equal(0, Volatile.Read(ref starts));
    Assert.Equal(string.Empty, fallback.Title);
  }

  private static async Task<int> VersionProbesAfterTwoLockedQueries(TimeSpan retryInterval)
  {
    var settingsPath = Path.Combine(Path.GetTempPath(), $"bw_test_{Guid.NewGuid():N}.json");
    var settings = new BitwardenSettingsManager(settingsPath);
    settings.RememberSession.Value = true;
    SessionStore.Save("stored-session");
    var probes = 0;
    try
    {
      var svc = new BitwardenCliService(settings, psi =>
      {
        if (psi.Arguments == "--version")
          Interlocked.Increment(ref probes);
        throw new InvalidOperationException(psi.Arguments);
      });
      using var fallback = new BitwardenFallbackItem(svc) { StatusRetryInterval = retryInterval };

      fallback.UpdateQuery("github");
      for (var i = 0; i < 50 && Volatile.Read(ref probes) == 0; i++)
        await Task.Delay(20, TestContext.Current.CancellationToken);
      await Task.Delay(100, TestContext.Current.CancellationToken);
      fallback.UpdateQuery("githu");
      await Task.Delay(200, TestContext.Current.CancellationToken);

      Assert.Equal(string.Empty, fallback.Title);
      return Volatile.Read(ref probes);
    }
    finally
    {
      SessionStore.Clear();
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task UpdateQuery_LockedWithStoredCredential_RetriesStatusOncePerInterval() =>
    Assert.Equal(1, await VersionProbesAfterTwoLockedQueries(TimeSpan.FromSeconds(30)));

  [Fact]
  public async Task UpdateQuery_LockedWithStoredCredential_RetriesAgainAfterInterval() =>
    Assert.Equal(2, await VersionProbesAfterTwoLockedQueries(TimeSpan.Zero));

  [Fact]
  public async Task UpdateQuery_UnlockedWithoutCache_RefreshesAndShowsMatch()
  {
    var id = 0;
    var svc = new BitwardenCliService(processFactory: psi => new FakeCliProcess(psi.Arguments switch
    {
      "list items" => """[{"id":"1","name":"GitHub","type":1,"login":{"username":"octocat"}}]""",
      "list folders" or "list organizations" => "[]",
      _ => "session_key\n",
    }, id: Interlocked.Increment(ref id))) { DataFileOverride = Path.Combine(Path.GetTempPath(), $"bw_missing_{Guid.NewGuid():N}.json") };
    await svc.UnlockAsync("pw");
    using var fallback = new BitwardenFallbackItem(svc);

    fallback.UpdateQuery("github");
    for (var i = 0; i < 50 && fallback.Title.Length == 0; i++)
      await Task.Delay(20, TestContext.Current.CancellationToken);

    Assert.Equal("GitHub", fallback.Title);
  }

  [Fact]
  public void UpdateQuery_ShortQuery_ClearsResult()
  {
    var svc = new BitwardenCliService(processFactory: new FakeProcessFactory().Create);
    using var fallback = new BitwardenFallbackItem(svc);

    fallback.UpdateQuery("g");

    Assert.Equal(string.Empty, fallback.Title);
  }
}
