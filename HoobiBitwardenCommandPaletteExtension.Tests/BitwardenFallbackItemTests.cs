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
  public void UpdateQuery_ShortQuery_ClearsResult()
  {
    var svc = new BitwardenCliService(processFactory: new FakeProcessFactory().Create);
    using var fallback = new BitwardenFallbackItem(svc);

    fallback.UpdateQuery("g");

    Assert.Equal(string.Empty, fallback.Title);
  }
}
