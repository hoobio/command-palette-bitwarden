using System.Net;
using System.Net.Http;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class AppUpdateTests
{
  private const string Releases = """
    [
      {"tag_name":"v1.14.0-pre.3","name":"v1.14.0-pre.3","html_url":"https://github.com/hoobio/command-palette-bitwarden/releases/tag/v1.14.0-pre.3","prerelease":true,"draft":false},
      {"tag_name":"v1.13.0","name":"v1.13.0","html_url":"https://github.com/hoobio/command-palette-bitwarden/releases/tag/v1.13.0","prerelease":false,"draft":false},
      {"tag_name":"v1.99.0","name":"v1.99.0","html_url":"https://github.com/hoobio/command-palette-bitwarden/releases/tag/v1.99.0","prerelease":false,"draft":true},
      {"tag_name":"v1.12.0","name":"v1.12.0","html_url":"https://example.com/evil","prerelease":false,"draft":false}
    ]
    """;

  private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
  {
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      Calls++;
      return Task.FromResult(respond(request));
    }
  }

  private sealed class FakeStore(bool available) : IStoreUpdates
  {
    public Exception? CheckFails { get; init; }

    public Exception? InstallFails { get; init; }

    public Func<Task>? Hold { get; init; }

    public int Installs { get; private set; }

    public async Task<bool> CheckAsync()
    {
      if (Hold is not null)
        await Hold();
      return CheckFails is null ? available : throw CheckFails;
    }

    public Task InstallAsync()
    {
      Installs++;
      return InstallFails is null ? Task.CompletedTask : throw InstallFails;
    }
  }

  private static GitHubReleaseClient ClientReturning(string json, HttpStatusCode status = HttpStatusCode.OK, StubHandler? handler = null) =>
    new(new HttpClient(handler ?? new StubHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(json) })));

  private static AppUpdateService Service(AppInfo app, IStoreUpdates? store = null, string json = Releases, HttpStatusCode status = HttpStatusCode.OK) =>
    new(app, store, ClientReturning(json, status));

  [Theory]
  [InlineData("Hoobi.BitwardenCommandPaletteExtension.Dev", AppChannels.Dev)]
  [InlineData("Hoobi.BitwardenCommandPaletteExtension.Prerelease", AppChannels.Prerelease)]
  [InlineData("Hoobi.BitwardenCommandPaletteExtension", AppChannels.Release)]
  public void ChannelOf_ReadsTheSuffixOfThePackageName(string name, string channel) =>
    Assert.Equal(channel, AppInfo.ChannelOf(name));

  [Theory]
  [InlineData("1.13.0+626a78a4f1b2c3d4e5f60718293a4b5c6d7e8f90", "626a78a")]
  [InlineData("1.13.0+abc", "abc")]
  [InlineData("1.13.0", null)]
  [InlineData("1.13.0+", null)]
  [InlineData(null, null)]
  public void ShortCommit_TakesTheFirstSevenCharactersAfterThePlus(string? informational, string? expected) =>
    Assert.Equal(expected, AppInfo.ShortCommit(informational));

  [Fact]
  public void From_CombinesVersionChannelCommitAndStore() =>
    Assert.Equal(new AppInfo("1.13.0", "626a78a", AppChannels.Prerelease, true), AppInfo.From("1.13.0", "Hoobi.BitwardenCommandPaletteExtension.Prerelease", true, "1.13.0+626a78a1"));

  [Fact]
  public void Current_FallsBackWhenThereIsNoPackageIdentity()
  {
    var current = AppInfo.Current;

    Assert.Equal(AppChannels.Dev, current.Channel);
    Assert.False(current.IsStore);
    Assert.False(string.IsNullOrEmpty(current.Version));
  }

  [Theory]
  [InlineData("v1.2.3", 1, 2, 3, int.MaxValue)]
  [InlineData("1.2.3", 1, 2, 3, int.MaxValue)]
  [InlineData("V10.0.1-pre.7", 10, 0, 1, 7)]
  [InlineData("1.2.3-rc", 1, 2, 3, 0)]
  public void SemVer_ParsesTagsWithAndWithoutPrefixAndPreRelease(string tag, int major, int minor, int patch, int pre)
  {
    Assert.True(SemVer.TryParse(tag, out var version));
    Assert.Equal(new SemVer(major, minor, patch, pre), version);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("1.2")]
  [InlineData("a.b.c")]
  [InlineData("1.2.x")]
  public void SemVer_RejectsWhatIsNotAVersion(string? tag) => Assert.False(SemVer.TryParse(tag, out _));

  [Theory]
  [InlineData("1.2.3", "1.2.4", -1)]
  [InlineData("1.3.0", "1.2.9", 1)]
  [InlineData("2.0.0", "1.9.9", 1)]
  [InlineData("1.2.3", "1.2.3", 0)]
  [InlineData("1.2.3-pre.5", "1.2.3", -1)]
  [InlineData("1.2.3-pre.5", "1.2.3-pre.4", 1)]
  public void SemVer_ComparesNumericallyAndRanksPreReleasesBelowTheStable(string left, string right, int sign)
  {
    Assert.True(SemVer.TryParse(left, out var a));
    Assert.True(SemVer.TryParse(right, out var b));

    Assert.Equal(sign, Math.Sign(a.Compare(b)));
  }

  [Fact]
  public void SelectLatest_SkipsDraftsAndUnparsableTags_AndHidesPreReleasesFromOtherChannels()
  {
    var releases = new[]
    {
      new GitHubRelease { TagName = "v1.14.0-pre.3", Prerelease = true },
      new GitHubRelease { TagName = "v1.13.0" },
      new GitHubRelease { TagName = "v1.99.0", Draft = true },
      new GitHubRelease { TagName = "nightly" },
    };

    Assert.Equal("v1.13.0", GitHubReleaseClient.SelectLatest(releases, includePrereleases: false)!.Value.Release.TagName);
    Assert.Equal("v1.14.0-pre.3", GitHubReleaseClient.SelectLatest(releases, includePrereleases: true)!.Value.Release.TagName);
    Assert.Null(GitHubReleaseClient.SelectLatest([], includePrereleases: true));
  }

  [Fact]
  public async Task GitHubCheck_ReportsANewerStableRelease_AndIgnoresAForeignReleaseUrl()
  {
    var client = ClientReturning("""[{"tag_name":"v1.13.0","name":"v1.13.0","html_url":"https://example.com/x","prerelease":false,"draft":false}]""");

    var reply = await client.CheckAsync(new AppInfo("1.12.0", null, AppChannels.Release, false), TestContext.Current.CancellationToken);

    Assert.Equal(new UpdateResponse(UpdateStates.Available, "v1.13.0", "https://github.com/hoobio/command-palette-bitwarden/releases/latest", false), reply);
  }

  [Fact]
  public async Task GitHubCheck_ReportsUpToDate_ForTheLatestStable_AndTheNewestPreReleaseOnlyOnThePrereleaseChannel()
  {
    var client = ClientReturning(Releases);

    var stable = await client.CheckAsync(new AppInfo("1.13.0", null, AppChannels.Release, false), TestContext.Current.CancellationToken);
    var prerelease = await client.CheckAsync(new AppInfo("1.13.0", null, AppChannels.Prerelease, false), TestContext.Current.CancellationToken);

    Assert.Equal(UpdateStates.UpToDate, stable.State);
    Assert.Equal("https://github.com/hoobio/command-palette-bitwarden/releases/tag/v1.13.0", stable.ReleaseUrl);
    Assert.Equal((UpdateStates.Available, "v1.14.0-pre.3"), (prerelease.State, prerelease.LatestVersion));
  }

  [Theory]
  [InlineData("not-a-version", Releases)]
  [InlineData("1.13.0", "[]")]
  [InlineData("1.13.0", "null")]
  public async Task GitHubCheck_Fails_WhenTheCurrentVersionOrTheReleasesCannotBeCompared(string current, string json)
  {
    var reply = await ClientReturning(json).CheckAsync(new AppInfo(current, null, AppChannels.Release, false), TestContext.Current.CancellationToken);

    Assert.Equal(UpdateStates.Failed, reply.State);
  }

  [Fact]
  public void CreateHttpClient_SendsTheHeadersTheGitHubApiNeeds()
  {
    using var client = GitHubReleaseClient.CreateHttpClient("1.13.0");

    Assert.Contains("HoobiBitwardenCommandPalette/1.13.0", client.DefaultRequestHeaders.UserAgent.ToString());
    Assert.Contains("application/vnd.github+json", client.DefaultRequestHeaders.Accept.ToString());
    Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
  }

  [Fact]
  public async Task Check_IsOff_ForDevBuilds_WithoutAnyRequest()
  {
    var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
    var service = new AppUpdateService(new AppInfo("1.13.0", null, AppChannels.Dev, false), null, ClientReturning(string.Empty, handler: handler));

    var reply = await service.CheckAsync(TestContext.Current.CancellationToken);

    Assert.Equal(UpdateStates.Off, reply.State);
    Assert.Equal(0, handler.Calls);
  }

  [Theory]
  [InlineData(true, UpdateStates.Available)]
  [InlineData(false, UpdateStates.UpToDate)]
  public async Task Check_AsksTheStore_ForAStoreInstall(bool available, string state)
  {
    var service = Service(new AppInfo("1.13.0", null, AppChannels.Release, true), new FakeStore(available));

    var reply = await service.CheckAsync(TestContext.Current.CancellationToken);

    Assert.Equal(new UpdateResponse(state, null, null, true), reply);
  }

  [Fact]
  public async Task Check_Fails_WhenTheStoreOrTheNetworkThrows()
  {
    var storeService = Service(new AppInfo("1.13.0", null, AppChannels.Release, true), new FakeStore(true) { CheckFails = new InvalidOperationException("no store") });
    var networkService = Service(new AppInfo("1.13.0", null, AppChannels.Release, false), json: "{}", status: HttpStatusCode.InternalServerError);

    Assert.Equal(new UpdateResponse(UpdateStates.Failed, null, null, true), await storeService.CheckAsync(TestContext.Current.CancellationToken));
    Assert.Equal(new UpdateResponse(UpdateStates.Failed, null, null, false), await networkService.CheckAsync(TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task Check_RejectsASecondCheckWhileOneIsRunning()
  {
    IpcException? second = null;
    AppUpdateService? service = null;
    service = Service(
      new AppInfo("1.13.0", null, AppChannels.Release, true),
      new FakeStore(true) { Hold = async () => second = await Assert.ThrowsAsync<IpcException>(() => service!.CheckAsync(TestContext.Current.CancellationToken)) });

    var first = await service.CheckAsync(TestContext.Current.CancellationToken);

    Assert.Equal(IpcErrors.Conflict, second?.Code);
    Assert.Equal(UpdateStates.Available, first.State);
    Assert.Equal(UpdateStates.Available, (await service.CheckAsync(TestContext.Current.CancellationToken)).State);
  }

  [Fact]
  public async Task Install_GoesToTheStore_AndIsInvalidWithoutOne()
  {
    var store = new FakeStore(true);
    await Service(new AppInfo("1.13.0", null, AppChannels.Release, true), store).InstallAsync();
    var none = await Assert.ThrowsAsync<IpcException>(() => Service(new AppInfo("1.13.0", null, AppChannels.Release, false)).InstallAsync());
    var failing = await Assert.ThrowsAsync<IpcException>(() => Service(new AppInfo("1.13.0", null, AppChannels.Release, true), new FakeStore(true) { InstallFails = new InvalidOperationException("boom") }).InstallAsync());

    Assert.Equal(1, store.Installs);
    Assert.Equal((IpcErrors.Invalid, IpcErrors.Failed), (none.Code, failing.Code));
  }

  [Fact]
  public async Task Ops_ServeAboutCheckAndInstall_OverTheDispatcher()
  {
    var store = new FakeStore(true);
    var service = Service(new AppInfo("1.13.0", "626a78a", AppChannels.Release, true), store);
    using var ops = new CompanionOps(new BitwardenCliService(), updates: service);

    var about = IpcJson.Read(await ops.HandleAsync(Ops.GetAbout, null, CancellationToken.None), IpcJson.Default.AboutResponse);
    var check = IpcJson.Read(await ops.HandleAsync(Ops.CheckUpdates, null, CancellationToken.None), IpcJson.Default.UpdateResponse);
    var install = await ops.HandleAsync(Ops.InstallUpdate, null, CancellationToken.None);

    Assert.Equal(new AboutResponse("1.13.0", "626a78a", AppChannels.Release, true), about);
    Assert.Equal(UpdateStates.Available, check.State);
    Assert.Null(install);
    Assert.Equal(1, store.Installs);
  }
}
