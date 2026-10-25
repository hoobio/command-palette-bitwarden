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

    Assert.Equal(_settings.OrderedSettings.Count, settings.Count);
    Assert.Equal(29, settings.Count);
    Assert.Equal(6, settings.Count(s => s.Kind == SettingKinds.Section));
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
