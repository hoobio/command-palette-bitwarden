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
    _ops = new CompanionOps(service);
  }

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

    Assert.Equal(_settings.OrderedSettings.Count + 1, settings.Count);
    Assert.Equal(6, settings.Count(s => s.Kind == SettingKinds.Section));
    Assert.Equal(1, settings.Count(s => s.Kind == SettingKinds.Link));
    Assert.Equal(14, settings.Count(s => s.Kind == SettingKinds.Toggle));
    Assert.Equal(7, settings.Count(s => s.Kind == SettingKinds.Choice));
    Assert.Equal(2, settings.Count(s => s.Kind == SettingKinds.Text));
    Assert.Equal(["_section_auth", "rememberSession", "useApiKeyAuthentication"], settings.Take(3).Select(s => s.Key));
    Assert.Equal(new SettingDto("_section_auth", SettingKinds.Section, "Authentication", null, null, null), settings[0]);

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
    };

    foreach (var (child, parent) in dependencies)
    {
      var childIndex = settings.ToList().FindIndex(s => s.Key == child);
      Assert.Equal((parent, "true"), (settings[childIndex].Parent, settings[childIndex].ParentValue));
      var parentIndex = settings.ToList().FindIndex(s => s.Key == parent);
      Assert.True(parentIndex < childIndex);
      Assert.All(settings.Skip(parentIndex + 1).Take(childIndex - parentIndex - 1), s => Assert.Equal(parent, s.Parent));
    }

    Assert.Equal(dependencies.Length + 1, settings.Count(s => s.Parent is not null));
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
    await SetAsync("cliDirectoryOverride", cli);
    var after = await GetAsync();

    Assert.Equal(cli, after.Single(s => s.Key == "cliDirectoryOverride").Placeholder);
    Assert.All(after.Where(s => s.Kind != SettingKinds.Text), s => Assert.Null(s.Placeholder));
  }

  [Fact]
  public async Task GetSettings_CarriesAPathAndSourceTooltip_OnTheTwoPathSettingsOnly()
  {
    var cli = Path.Combine(_dir, "bw.exe");
    await SetAsync("cliDirectoryOverride", cli);
    await SetAsync("cliDataDirectoryOverride", _dir);
    var settings = await GetAsync();

    Assert.Equal($"{cli}\nSet here", settings.Single(s => s.Key == "cliDirectoryOverride").Tooltip);
    Assert.Equal($"{_dir}\nSet here", settings.Single(s => s.Key == "cliDataDirectoryOverride").Tooltip);
    Assert.Equal(2, settings.Count(s => s.Tooltip is not null));
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
