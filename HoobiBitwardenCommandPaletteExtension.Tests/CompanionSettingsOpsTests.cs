using System.IO;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class CompanionSettingsOpsTests : IDisposable
{
  private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bw_settings_{Guid.NewGuid():N}");
  private readonly string _settingsPath;
  private readonly BitwardenSettingsManager _settings;
  private readonly CompanionOps _ops;

  public CompanionSettingsOpsTests()
  {
    Directory.CreateDirectory(_dir);
    _settingsPath = Path.Combine(_dir, "settings.json");
    _settings = new BitwardenSettingsManager(_settingsPath);
    var service = new BitwardenCliService(_settings, new FakeProcessFactory().Create) { DataFileOverride = Path.Combine(_dir, "missing.json") };
    _ops = new CompanionOps(service, desktopReachable: () => _desktopReachable);
  }

  private bool _desktopReachable = true;

  public void Dispose()
  {
    _ops.Dispose();
    Directory.Delete(_dir, recursive: true);
    GC.SuppressFinalize(this);
  }

  private async Task<IReadOnlyList<SettingDto>> GetAsync() =>
    IpcJson.Read(await _ops.HandleAsync(Ops.GetSettings, null, CancellationToken.None), IpcJson.Default.SettingsResponse).Settings;

  private async Task<SetSettingResponse> SetAsync(string key, string value) =>
    IpcJson.Read(await _ops.HandleAsync(Ops.SetSetting, IpcJson.ToElement(new SetSettingRequest(key, value), IpcJson.Default.SetSettingRequest), CancellationToken.None), IpcJson.Default.SetSettingResponse);

  [Fact]
  public async Task GetSettings_ReturnsEverySettingInOrder_WithItsKind()
  {
    var settings = await GetAsync();

    Assert.Equal(_settings.OrderedSettings.Count + 14, settings.Count);
    Assert.Equal(8, settings.Count(s => s.Kind == SettingKinds.Section));
    Assert.Equal(2, settings.Count(s => s.Kind == SettingKinds.Link));
    Assert.Equal(17, settings.Count(s => s.Kind == SettingKinds.Toggle));
    Assert.Equal(8, settings.Count(s => s.Kind == SettingKinds.Choice));
    Assert.Equal(10, settings.Count(s => s.Kind == SettingKinds.Text));
    Assert.Equal(["_section_server", "serverRegion", "serverUrl", "serverAdvanced"], settings.Take(4).Select(s => s.Key));
    Assert.Equal(["_section_shortcuts", "hotkeys"], settings.Skip(11).Take(2).Select(s => s.Key));
    Assert.Equal((SettingKinds.Link, "Keyboard shortcuts", "Change item shortcuts and the Enter action"), (settings[12].Kind, settings[12].Label, settings[12].Description));
    Assert.Equal(["_section_auth", "rememberSession", "useApiKeyAuthentication"], settings.Skip(13).Take(3).Select(s => s.Key));
    Assert.Equal(new SettingDto("_section_auth", SettingKinds.Section, "Authentication", null, null, null), settings[13]);

    var liveSync = settings.Single(s => s.Key == "liveSync");
    Assert.Equal((SettingKinds.Toggle, "Live Sync", "true"), (liveSync.Kind, liveSync.Label, liveSync.Value));
    Assert.Null(liveSync.Choices);
    Assert.False(string.IsNullOrEmpty(liveSync.Description));

    var limit = settings.Single(s => s.Key == "contextItemLimit");
    Assert.Equal((SettingKinds.Choice, "3"), (limit.Kind, limit.Value));
    Assert.Equal(["1", "2", "3", "5", "10", "0"], limit.Choices!.Select(c => c.Value));
    Assert.Equal("1 item", limit.Choices![0].Title);

    var path = settings.Single(s => s.Key == "cliDirectoryOverride");
    Assert.Equal((SettingKinds.Text, string.Empty), (path.Kind, path.Value));
  }

  [Fact]
  public async Task GetSettings_CarriesTheParent_AndEachChildFollowsItsParent()
  {
    var settings = await GetAsync();
    var dependencies = new[]
    {
      ("autoBiometricUnlock", "useDesktopIntegration"),
      ("showContextTag", "contextAwareness"),
      ("contextItemLimit", "contextAwareness"),
      ("clipboardClearDelay", "autoClearClipboard"),
      ("showWatchtowerTags", "showTags"),
      ("showProtectedTag", "showTags"),
      ("showPasskeyTag", "showTags"),
      ("totpTagStyle", "showTags"),
      ("organizationTagStyle", "showTags"),
      ("cliDirectoryOverride", "useCliDirectoryOverride"),
      ("cliDataDirectoryOverride", "useCliDataDirectoryOverride"),
    };

    foreach (var (child, parent) in dependencies)
    {
      var childIndex = settings.ToList().FindIndex(s => s.Key == child);
      Assert.Equal((parent, "true"), (settings[childIndex].Parent, settings[childIndex].ParentValue));
      var parentIndex = settings.ToList().FindIndex(s => s.Key == parent);
      Assert.True(parentIndex < childIndex);
      Assert.All(settings.Skip(parentIndex + 1).Take(childIndex - parentIndex - 1), s => Assert.Equal(parent, s.Parent));
    }

    Assert.Equal(dependencies.Length + 10, settings.Count(s => s.Parent is not null));
  }

  [Fact]
  public void ServerRows_LeadTheWindow_WithTheRegionAndTheSelfHostedUrl()
  {
    var rows = CompanionOps.ServerRows("https://vault.example.com", loggedIn: false);

    Assert.Equal(["_section_server", "serverRegion", "serverUrl", "serverAdvanced", "serverWebVaultUrl", "serverApiUrl", "serverIdentityUrl", "serverIconsUrl", "serverNotificationsUrl", "serverEventsUrl", "serverKeyConnectorUrl"], rows.Select(r => r.Key));
    Assert.Equal(("Server", SettingKinds.Section), (rows[0].Label, rows[0].Kind));
    Assert.Equal(ServerRegions.SelfHosted, rows[1].Value);
    Assert.Equal(["cloud", "eu", "selfHosted"], rows[1].Choices!.Select(c => c.Value));
    Assert.Equal(("https://vault.example.com", "serverRegion", ServerRegions.SelfHosted), (rows[2].Value, rows[2].Parent, rows[2].ParentValue));
    Assert.All(rows, r => Assert.Null(r.Confirm));
  }

  [Fact]
  public void ServerRows_ServerUrl_CarriesThePresetUrlsForTheCloudRegions()
  {
    var url = CompanionOps.ServerRows("bitwarden.eu", loggedIn: false).Single(r => r.Key == "serverUrl");

    Assert.Equal(string.Empty, url.Value);
    Assert.Equal("Address of your Bitwarden server", url.Description);
    Assert.Equal(new Dictionary<string, string> { ["cloud"] = "https://vault.bitwarden.com", ["eu"] = "https://vault.bitwarden.eu" }, url.ReadOnlyValues);
  }

  [Fact]
  public void ServerRows_AdvancedRows_AreCollapsedBehindSelfHosted_WithPlaceholdersAndCurrentValues()
  {
    var rows = CompanionOps.ServerRows("https://vault.example.com", loggedIn: false, new Models.ServerConfig("https://vault.example.com", ApiUrl: "https://api.example.com"));

    var advanced = rows.Single(r => r.Key == "serverAdvanced");
    Assert.Equal((SettingKinds.Expander, "serverRegion", ServerRegions.SelfHosted, "Leave blank to derive from the server URL."), (advanced.Kind, advanced.Parent, advanced.ParentValue, advanced.Description));
    var overrides = rows.SkipWhile(r => r.Key != "serverAdvanced").Skip(1).ToList();
    Assert.All(overrides, r => Assert.Equal((SettingKinds.Text, "serverAdvanced", "true"), (r.Kind, r.Parent, r.ParentValue)));
    Assert.Equal(["https://vault.example.com/", "https://vault.example.com/api", "https://vault.example.com/identity", "https://vault.example.com/icons", "https://vault.example.com/notifications", "https://vault.example.com/events", "https://vault.example.com/key-connector"], overrides.Select(r => r.Placeholder));
    Assert.Equal(["", "https://api.example.com", "", "", "", "", ""], overrides.Select(r => r.Value));
  }

  [Theory]
  [InlineData("https://bw.contoso.com/", "https://bw.contoso.com/api")]
  [InlineData(null, "https://vault.example.com/api")]
  [InlineData("bw.contoso.com", "https://vault.example.com/api")]
  public void ServerRows_AdvancedPlaceholders_DeriveFromTheServerUrl_OrShowTheExample(string? url, string api)
  {
    var rows = CompanionOps.ServerRows(url, loggedIn: false);

    Assert.Equal(api, rows.Single(r => r.Key == "serverApiUrl").Placeholder);
  }

  [Fact]
  public void ServerRows_WhileLoggedIn_AdvancedRowsAskForConfirmation()
  {
    var rows = CompanionOps.ServerRows("https://vault.example.com", loggedIn: true);

    Assert.All(rows.Where(r => r.Parent == "serverAdvanced"), r => Assert.Equal(rows[1].Confirm, r.Confirm));
  }

  [Theory]
  [InlineData(null, ServerRegions.Cloud)]
  [InlineData("https://vault.bitwarden.com", ServerRegions.Cloud)]
  [InlineData("https://vault.bitwarden.eu", ServerRegions.Eu)]
  [InlineData("https://vault.example.com", ServerRegions.SelfHosted)]
  public void ServerRows_PickTheRegionFromTheCurrentServer_AndKeepTheUrlOnlyWhenSelfHosted(string? url, string region)
  {
    var rows = CompanionOps.ServerRows(url, loggedIn: false);

    Assert.Equal(region, rows[1].Value);
    Assert.Equal(region == ServerRegions.SelfHosted ? url : string.Empty, rows[2].Value);
  }

  [Fact]
  public void ServerRows_WhileLoggedIn_WarnAndAskForConfirmation()
  {
    var rows = CompanionOps.ServerRows(null, loggedIn: true);

    Assert.Equal("Changing the server logs you out", rows[1].Description);
    Assert.Equal("Changing the server logs you out of Bitwarden. Continue?", rows[1].Confirm);
    Assert.Equal(rows[1].Confirm, rows[2].Confirm);
  }

  [Fact]
  public async Task SetSetting_RejectsTheServerRows()
  {
    Assert.Equal(new SetSettingResponse(false, "Unknown setting"), await SetAsync(SettingKeys.ServerRegion, ServerRegions.Eu));
  }

  [Fact]
  public async Task GetSettings_ShowsThePortableDataDirectoryOnlyWhileTheCliPathIsSet()
  {
    var settings = await GetAsync();

    var child = settings.Single(s => s.Key == "usePortableDataDirectory");
    Assert.Equal(("cliDirectoryOverride", ParentValues.NonEmpty), (child.Parent, child.ParentValue));
    Assert.Equal("Keeps CLI data next to the CLI", child.Description);
    Assert.Equal(settings.ToList().FindIndex(s => s.Key == "cliDirectoryOverride") + 1, settings.ToList().FindIndex(s => s.Key == "usePortableDataDirectory"));
  }

  [Fact]
  public async Task GetSettings_PutsTheLogsLinkRightAfterDebugLogging_UnderDiagnostics()
  {
    var settings = await GetAsync();
    var index = settings.ToList().FindIndex(s => s.Key == "debugLogging");

    Assert.Equal(new SettingDto(SettingKeys.OpenLogs, SettingKinds.Link, "Logs", "Opens the log folder", null, null), settings[index + 1]);
    Assert.Equal("Diagnostics", settings.Single(s => s.Key == "_section_advanced").Label);
  }

  [Fact]
  public async Task SetSetting_RejectsTheLogsLink()
  {
    var reply = await SetAsync(SettingKeys.OpenLogs, "x");

    Assert.Equal(new SetSettingResponse(false, "Unknown setting"), reply);
  }

  [Fact]
  public async Task GetSettings_CarriesThePathsInUseAsPlaceholders_AndFollowsTheOverrides()
  {
    var before = await GetAsync();
    Assert.Equal("bw", before.Single(s => s.Key == "cliDirectoryOverride").Placeholder);
    Assert.Equal(_dir, before.Single(s => s.Key == "cliDataDirectoryOverride").Placeholder);

    var cli = Path.Combine(_dir, "bw.exe");
    await File.WriteAllTextAsync(cli, string.Empty, TestContext.Current.CancellationToken);
    await SetAsync("useCliDirectoryOverride", "true");
    await SetAsync("cliDirectoryOverride", cli);
    var after = await GetAsync();

    Assert.Equal(cli, after.Single(s => s.Key == "cliDirectoryOverride").Placeholder);
    Assert.All(after.Where(s => s.Kind != SettingKinds.Text), s => Assert.Null(s.Placeholder));
  }

  [Fact]
  public async Task GetSettings_CarriesAPathAndSourceTooltip_OnTheTwoPathSettingsOnly()
  {
    var cli = Path.Combine(_dir, "bw.exe");
    await SetAsync("useCliDirectoryOverride", "true");
    await SetAsync("useCliDataDirectoryOverride", "true");
    await SetAsync("cliDirectoryOverride", cli);
    await SetAsync("cliDataDirectoryOverride", _dir);
    var settings = await GetAsync();

    Assert.Equal($"{cli}\nSet here", settings.Single(s => s.Key == "cliDirectoryOverride").Tooltip);
    Assert.Equal($"{_dir}\nSet here", settings.Single(s => s.Key == "cliDataDirectoryOverride").Tooltip);
    Assert.Equal(["cliDataDirectoryOverride", "cliDirectoryOverride", "useDesktopIntegration"], settings.Where(s => s.Tooltip is not null).Select(s => s.Key).Order(StringComparer.Ordinal));
  }

  [Fact]
  public async Task GetSettings_OverrideSwitchOff_KeepsThePath_ButUsesTheNormalDetection()
  {
    var cli = Path.Combine(_dir, "bw.exe");
    await File.WriteAllTextAsync(cli, string.Empty, TestContext.Current.CancellationToken);
    await SetAsync("useCliDirectoryOverride", "true");
    await SetAsync("cliDirectoryOverride", cli);
    await SetAsync("useCliDirectoryOverride", "false");
    var settings = await GetAsync();

    var path = settings.Single(s => s.Key == "cliDirectoryOverride");
    Assert.Equal(cli, path.Value);
    Assert.Equal("bw", path.Placeholder);
    Assert.DoesNotContain("Set here", path.Tooltip, StringComparison.Ordinal);
    Assert.Equal([true, true], settings.Where(s => s.Key is "useCliDirectoryOverride" or "useCliDataDirectoryOverride").Select(s => s.RevealsChildren));
    Assert.Equal(2, settings.Count(s => s.RevealsChildren));
  }

  [Fact]
  public async Task GetSettings_ScansPathOncePerExecutable_UntilASettingChanges()
  {
    var original = Environment.GetEnvironmentVariable("PATH");
    var onPath = Path.Combine(_dir, "pathdir");
    Directory.CreateDirectory(onPath);
    Environment.SetEnvironmentVariable("PATH", onPath);
    try
    {
      Assert.Equal("bw\nNot found on PATH", (await GetAsync()).Single(s => s.Key == "cliDirectoryOverride").Tooltip);

      await File.WriteAllTextAsync(Path.Combine(onPath, "bw.exe"), string.Empty, TestContext.Current.CancellationToken);
      Assert.Equal("bw\nNot found on PATH", (await GetAsync()).Single(s => s.Key == "cliDirectoryOverride").Tooltip);

      await SetAsync("liveSync", "false");
      Assert.Equal($"{Path.Combine(onPath, "bw.exe")}\nFound on PATH", (await GetAsync()).Single(s => s.Key == "cliDirectoryOverride").Tooltip);
    }
    finally
    {
      Environment.SetEnvironmentVariable("PATH", original);
    }
  }

  [Fact]
  public async Task GetSettings_ScansPathAgain_WhenTheCliPathOverrideChanges()
  {
    var original = Environment.GetEnvironmentVariable("PATH");
    Environment.SetEnvironmentVariable("PATH", Path.Combine(_dir, "empty"));
    try
    {
      Assert.Equal("bw\nNot found on PATH", (await GetAsync()).Single(s => s.Key == "cliDirectoryOverride").Tooltip);
      var cli = Path.Combine(_dir, "bw.exe");
      await File.WriteAllTextAsync(cli, string.Empty, TestContext.Current.CancellationToken);
      await SetAsync("useCliDirectoryOverride", "true");
      await SetAsync("cliDirectoryOverride", cli);
      Assert.Equal($"{cli}\nSet here", (await GetAsync()).Single(s => s.Key == "cliDirectoryOverride").Tooltip);
    }
    finally
    {
      Environment.SetEnvironmentVariable("PATH", original);
    }
  }

  [Fact]
  public async Task GetSettings_ShowsTheDesktopStatus_OnlyWhileIntegrationIsOn()
  {
    var row = (await GetAsync()).Single(s => s.Key == "useDesktopIntegration");
    Assert.Equal((SettingStatuses.Ok, "Connected to Bitwarden Desktop"), (row.Status, row.Tooltip));

    _desktopReachable = false;
    row = (await GetAsync()).Single(s => s.Key == "useDesktopIntegration");
    Assert.Equal((SettingStatuses.Error, "Bitwarden Desktop not reachable"), (row.Status, row.Tooltip));

    await SetAsync("useDesktopIntegration", "false");
    var settings = await GetAsync();
    row = settings.Single(s => s.Key == "useDesktopIntegration");
    Assert.Equal((null, null), (row.Status, row.Tooltip));
    Assert.DoesNotContain(settings, s => s.Status is not null);
  }

  [Theory]
  [InlineData(false, true, null)]
  [InlineData(false, false, null)]
  [InlineData(true, true, SettingStatuses.Ok)]
  [InlineData(true, false, SettingStatuses.Error)]
  public void WithDesktopStatus_ProbesOnlyWhenEnabled(bool enabled, bool reachable, string? status)
  {
    var probed = false;
    var row = CompanionOps.WithDesktopStatus(new SettingDto("k", SettingKinds.Toggle, "L", null, "true", null), enabled, () => { probed = true; return reachable; });

    Assert.Equal(status, row.Status);
    Assert.Equal(enabled, probed);
  }

  [Fact]
  public void CliTooltip_NamesTheBranchTheResolverTook()
  {
    var onPath = Path.Combine(_dir, "bw.exe");
    File.WriteAllText(onPath, string.Empty);

    Assert.Equal(@"C:\tools\bw.exe" + "\nSet here", CompanionOps.CliTooltip(@"C:\tools\bw.exe", @"C:\tools", null));
    Assert.Equal(@"C:\cli\bw.exe" + "\nInstalled by the extension", CompanionOps.CliTooltip(@"C:\cli\bw.exe", " ", null));
    Assert.Equal($"{onPath}\nFound on PATH", CompanionOps.CliTooltip("bw", null, $@"C:\missing{Path.PathSeparator}{_dir}"));
    Assert.Equal("bw\nNot found on PATH", CompanionOps.CliTooltip("bw", null, @"C:\missing"));
    Assert.Equal("bw\nNot found on PATH", CompanionOps.CliTooltip("bw", null, null));
  }

  [Theory]
  [InlineData(@"D:\data", @"D:\data", @"C:\cli", "x", "Set here")]
  [InlineData(@"C:\cli", null, @"C:\cli", "x", "Next to the CLI")]
  [InlineData(@"E:\env", null, null, @"E:\env", "From BITWARDENCLI_APPDATA_DIR")]
  [InlineData(@"C:\Roaming\Bitwarden CLI", " ", null, "", "Default location")]
  public void DataTooltip_NamesTheBranchTheResolverTook(string folder, string? typed, string? resolved, string? environment, string source)
  {
    Assert.Equal($"{folder}\n{source}", CompanionOps.DataTooltip(folder, typed, resolved, environment));
  }

  [Fact]
  public async Task GetSettings_ExplainsTheTagStyleOptionsWithTooltips_AndNothingElse()
  {
    var settings = await GetAsync();

    var totp = settings.Single(s => s.Key == "totpTagStyle").Choices!;
    Assert.Equal([("Off", null), ("Static", "Shows a 2FA badge"), ("Live", "Shows the live code and countdown")], totp.Select(c => (c.Title, c.Description)));
    var organization = settings.Single(s => s.Key == "organizationTagStyle").Choices!;
    Assert.Equal([("Initials", "Shows 1-3 letter initials"), ("Name", "Shows the full organisation name"), ("Off", null)], organization.Select(c => (c.Title, c.Description)));
    Assert.Equal(["off", "static", "live"], totp.Select(c => c.Value));
    Assert.Equal(["initials", "name", "off"], organization.Select(c => c.Value));
    Assert.All(settings.Where(s => s.Kind == SettingKinds.Choice && s.Key is not ("totpTagStyle" or "organizationTagStyle")).SelectMany(s => s.Choices!), c => Assert.Null(c.Description));
  }

  [Theory]
  [InlineData("totpTagStyle", "static", "Shows a 2FA badge")]
  [InlineData("totpTagStyle", "live", "Shows the live code and countdown")]
  [InlineData("organizationTagStyle", "initials", "Shows 1-3 letter initials")]
  [InlineData("organizationTagStyle", "name", "Shows the full organisation name")]
  [InlineData("organizationTagStyle", "off", null)]
  [InlineData("autoLockTimeout", "0", null)]
  public void ChoiceDescription_LooksUpTheTooltipByTheSettingKeyAndValue(string key, string value, string? expected) =>
    Assert.Equal(expected, BitwardenSettingsManager.ChoiceDescription(key, value));

  [Fact]
  public async Task SetSetting_Toggle_ChangesTheValue_RaisesSettingsChanged_AndSaves()
  {
    var raised = 0;
    _settings.Settings.SettingsChanged += (_, _) => raised++;

    var reply = await SetAsync("showWebsiteIcons", "false");

    Assert.Equal(new SetSettingResponse(true, null), reply);
    Assert.False(_settings.ShowWebsiteIcons.Value);
    Assert.Equal(1, raised);
    Assert.False(new BitwardenSettingsManager(_settingsPath).ShowWebsiteIcons.Value);
    Assert.Equal("false", (await GetAsync()).Single(s => s.Key == "showWebsiteIcons").Value);
  }

  [Fact]
  public async Task SetSetting_Choice_ChangesTheValue_AndRaisesSettingsChanged()
  {
    var raised = 0;
    _settings.Settings.SettingsChanged += (_, _) => raised++;

    var reply = await SetAsync("contextItemLimit", "5");

    Assert.True(reply.Success);
    Assert.Equal("5", _settings.ContextItemLimit.Value);
    Assert.Equal(1, raised);
    Assert.Equal("5", new BitwardenSettingsManager(_settingsPath).ContextItemLimit.Value);
  }

  [Fact]
  public async Task SetSetting_Text_ChangesTheValue_AndRaisesSettingsChanged()
  {
    var cli = Path.Combine(_dir, "bw.exe");
    await File.WriteAllTextAsync(cli, string.Empty, TestContext.Current.CancellationToken);
    var raised = 0;
    _settings.Settings.SettingsChanged += (_, _) => raised++;

    var reply = await SetAsync("cliDirectoryOverride", cli);

    Assert.True(reply.Success);
    Assert.Equal(cli, _settings.CliDirectoryOverride.Value);
    Assert.Equal(1, raised);
    Assert.Equal(cli, new BitwardenSettingsManager(_settingsPath).CliDirectoryOverride.Value);
  }

  [Theory]
  [InlineData("noSuchSetting", "true", "Unknown setting")]
  [InlineData("_section_auth", "true", "Unknown setting")]
  [InlineData("showWebsiteIcons", "yes", "Invalid value for this setting")]
  [InlineData("contextItemLimit", "7", "Invalid value for this setting")]
  [InlineData("cliDirectoryOverride", "line one\nline two", "Invalid value for this setting")]
  public async Task SetSetting_RejectsAnUnknownKeyOrAValueThatDoesNotFit_AndChangesNothing(string key, string value, string error)
  {
    var raised = 0;
    _settings.Settings.SettingsChanged += (_, _) => raised++;

    var reply = await SetAsync(key, value);

    Assert.Equal(new SetSettingResponse(false, error), reply);
    Assert.Equal(0, raised);
    Assert.True(_settings.ShowWebsiteIcons.Value);
    Assert.Equal("3", _settings.ContextItemLimit.Value);
    Assert.Equal(string.Empty, _settings.CliDirectoryOverride.Value);
  }

  [Fact]
  public async Task SetSetting_RejectsATextThatIsTooLong()
  {
    var reply = await SetAsync("cliDirectoryOverride", new string('a', 4097));

    Assert.False(reply.Success);
    Assert.Equal(string.Empty, _settings.CliDirectoryOverride.Value);
  }

  [Fact]
  public void SettingsChangedEvent_FiresWhenThePowerToysPageRaisesIt_AndStopsAfterDispose()
  {
    var service = new BitwardenCliService(_settings, new FakeProcessFactory().Create) { DataFileOverride = Path.Combine(_dir, "missing.json") };
    var ops = new CompanionOps(service);
    var raised = 0;
    ops.SettingsChanged += () => raised++;

    _settings.RaiseChanged();
    Assert.Equal(1, raised);

    ops.Dispose();
    _settings.RaiseChanged();
    Assert.Equal(1, raised);
  }

  [Fact]
  public async Task SettingOps_WithoutASettingsManager_FailWithoutThrowingAnythingElse()
  {
    using var ops = new CompanionOps(new BitwardenCliService());

    var read = await Assert.ThrowsAsync<IpcException>(() => ops.HandleAsync(Ops.GetSettings, null, CancellationToken.None));
    var write = await Assert.ThrowsAsync<IpcException>(() => ops.HandleAsync(Ops.SetSetting, IpcJson.ToElement(new SetSettingRequest("a", "b"), IpcJson.Default.SetSettingRequest), CancellationToken.None));

    Assert.Equal((IpcErrors.Failed, IpcErrors.Failed), (read.Code, write.Code));
  }

  [Fact]
  public void OrderedSettings_MatchTheSettingsInTheManager()
  {
    Assert.All(_settings.OrderedSettings, s => Assert.NotNull(s));
    var raised = 0;
    _settings.Settings.SettingsChanged += (_, _) => raised++;

    _settings.RaiseChanged();

    Assert.Equal(1, raised);
  }

  [Fact]
  public void CompanionHost_ForwardsSettingsChanged_WithoutStartingTheCompanion()
  {
    using var host = new CompanionHost(_ops);

    _settings.RaiseChanged();
    host.NotifySettingsChanged();

    Assert.Null(host.Process);
  }
}
