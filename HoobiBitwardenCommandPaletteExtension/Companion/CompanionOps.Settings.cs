using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Helpers;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  private const int MaxSettingTextLength = 4096;

  internal event Action? SettingsChanged;

  private readonly Lock _cliTooltipLock = new();
  private (string Executable, string? Override, string Tooltip)? _cliTooltip;

  private void RaiseSettingsChanged(object sender, Settings e)
  {
    ClearCliTooltip();
    SettingsChanged?.Invoke();
  }

  internal void ClearCliTooltip()
  {
    lock (_cliTooltipLock)
      _cliTooltip = null;
  }

  private string CachedCliTooltip(string executable, string? pathOverride)
  {
    lock (_cliTooltipLock)
    {
      if (_cliTooltip is { } cached && cached.Executable == executable && cached.Override == pathOverride)
        return cached.Tooltip;
      var tooltip = CliTooltip(executable, pathOverride, Environment.GetEnvironmentVariable("PATH"));
      _cliTooltip = (executable, pathOverride, tooltip);
      return tooltip;
    }
  }

  internal SettingsResponse GetSettings()
  {
    var manager = Manager();
    return new((SettingDto[])[.. ServerRows(), .. manager.OrderedSettings.Select(DescribeSetting).OfType<SettingDto>()
      .Select(s => manager.TryGetParent(s.Key, out var parent) ? s with { Parent = parent.Key, ParentValue = parent.Value } : s)
      .Select(s => s with { Placeholder = PlaceholderFor(manager, s.Key), Tooltip = TooltipFor(manager, s.Key) })
      .Select(s => s.Key == manager.UseDesktopIntegration.Key ? WithDesktopStatus(s, manager.UseDesktopIntegration.Value, _desktopReachable) : s)
      .SelectMany(s => s.Key == manager.DebugLogging.Key ? new[] { s, OpenLogsLink } : new[] { s })
      .Prepend(HotkeysLink)
      .Prepend(ShortcutsSection)]);
  }

  private static readonly ChoiceDto[] ServerChoices =
  [
    new("Bitwarden.com", ServerRegions.Cloud),
    new("Bitwarden.eu", ServerRegions.Eu),
    new("Self-hosted", ServerRegions.SelfHosted),
  ];

  private static readonly Dictionary<string, string> PresetUrls = new()
  {
    [ServerRegions.Cloud] = "https://vault.bitwarden.com",
    [ServerRegions.Eu] = "https://vault.bitwarden.eu",
  };

  internal static string RegionOf(string? serverUrl) =>
    string.IsNullOrEmpty(serverUrl) || VaultItemHelper.IsCloudHost(serverUrl, "bitwarden.com") ? ServerRegions.Cloud
    : VaultItemHelper.IsCloudHost(serverUrl, "bitwarden.eu") ? ServerRegions.Eu
    : ServerRegions.SelfHosted;

  internal SetServerRequest CurrentServer()
  {
    var url = BitwardenCliService.ServerUrl;
    var region = RegionOf(url);
    if (region != ServerRegions.SelfHosted)
      return new SetServerRequest(region, null);
    var urls = _service.ReadServerUrls();
    return new SetServerRequest(region, url, urls?.WebVaultUrl, urls?.ApiUrl, urls?.IdentityUrl, urls?.IconsUrl, urls?.NotificationsUrl, urls?.EventsUrl, urls?.KeyConnectorUrl);
  }

  private SettingDto[] ServerRows()
  {
    var url = BitwardenCliService.ServerUrl;
    return ServerRows(url, _service.LastStatus is VaultStatus.Locked or VaultStatus.Unlocked, RegionOf(url) == ServerRegions.SelfHosted ? _service.ReadServerUrls() : null);
  }

  internal static SettingDto[] ServerRows(string? url, bool loggedIn, ServerConfig? urls = null)
  {
    var region = RegionOf(url);
    var warning = "Changing the server logs you out";
    var confirm = loggedIn ? $"{warning} of Bitwarden. Continue?" : null;
    SettingDto Advanced(string key, string label, string? value, string path) =>
      new(key, SettingKinds.Text, label, null, value ?? string.Empty, null, SettingKeys.ServerAdvanced, "true", Placeholder: "{base URL}" + path, Confirm: confirm);
    return
    [
      new("_section_server", SettingKinds.Section, "Server", null, null, null),
      new(SettingKeys.ServerRegion, SettingKinds.Choice, "Region", loggedIn ? warning : "Where your vault is hosted", region, ServerChoices, Confirm: confirm),
      new(SettingKeys.ServerUrl, SettingKinds.Text, "Server URL", "Address of your Bitwarden server", region == ServerRegions.SelfHosted ? url : string.Empty, null, SettingKeys.ServerRegion, ServerRegions.SelfHosted, Placeholder: "https://vault.example.com", Confirm: confirm, ReadOnlyValues: PresetUrls),
      new(SettingKeys.ServerAdvanced, SettingKinds.Expander, "Advanced", "Leave blank to derive from the server URL.", null, null, SettingKeys.ServerRegion, ServerRegions.SelfHosted),
      Advanced(SettingKeys.ServerWebVaultUrl, "Web Vault URL", urls?.WebVaultUrl, "/"),
      Advanced(SettingKeys.ServerApiUrl, "API URL", urls?.ApiUrl, "/api"),
      Advanced(SettingKeys.ServerIdentityUrl, "Identity URL", urls?.IdentityUrl, "/identity"),
      Advanced(SettingKeys.ServerIconsUrl, "Icons URL", urls?.IconsUrl, "/icons"),
      Advanced(SettingKeys.ServerNotificationsUrl, "Notifications URL", urls?.NotificationsUrl, "/notifications"),
      Advanced(SettingKeys.ServerEventsUrl, "Events URL", urls?.EventsUrl, "/events"),
      Advanced(SettingKeys.ServerKeyConnectorUrl, "Key Connector URL", urls?.KeyConnectorUrl, "/key-connector"),
    ];
  }

  private static readonly SettingDto OpenLogsLink = new(SettingKeys.OpenLogs, SettingKinds.Link, "Logs", "Opens the log folder", null, null);

  private static readonly SettingDto ShortcutsSection = new("_section_shortcuts", SettingKinds.Section, "Shortcuts", null, null, null);

  private static readonly SettingDto HotkeysLink = new(SettingKeys.Hotkeys, SettingKinds.Link, "Keyboard shortcuts", "Change item shortcuts and the Enter action", null, null);

  private string? PlaceholderFor(BitwardenSettingsManager manager, string key) =>
    key == manager.CliDirectoryOverride.Key ? _service.CliExecutable
    : key == manager.CliDataDirectoryOverride.Key ? _service.DataFileFolder
    : null;

  private string? TooltipFor(BitwardenSettingsManager manager, string key) =>
    key == manager.CliDirectoryOverride.Key ? CachedCliTooltip(_service.CliExecutable, manager.CliDirectoryOverride.Value)
    : key == manager.CliDataDirectoryOverride.Key ? DataTooltip(_service.DataFileFolder, manager.CliDataDirectoryOverride.Value, _service.DataDirectory, Environment.GetEnvironmentVariable("BITWARDENCLI_APPDATA_DIR"))
    : null;

  internal static SettingDto WithDesktopStatus(SettingDto row, bool enabled, Func<bool> reachable) =>
    !enabled ? row
    : reachable() ? row with { Status = SettingStatuses.Ok, Tooltip = "Connected to Bitwarden Desktop" }
    : row with { Status = SettingStatuses.Error, Tooltip = "Bitwarden Desktop not reachable" };

  internal static string CliTooltip(string executable, string? pathOverride, string? pathVariable)
  {
    if (!string.IsNullOrWhiteSpace(pathOverride))
      return $"{executable}\nSet here";
    if (executable != "bw")
      return $"{executable}\nInstalled by the extension";
    return FindOnPath(pathVariable) is { } found ? $"{found}\nFound on PATH" : "bw\nNot found on PATH";
  }

  internal static string DataTooltip(string? folder, string? dataDirOverride, string? resolvedDirectory, string? environmentDirectory)
  {
    var source = !string.IsNullOrWhiteSpace(dataDirOverride) ? "Set here"
      : resolvedDirectory != null ? "Next to the CLI"
      : !string.IsNullOrEmpty(environmentDirectory) ? "From BITWARDENCLI_APPDATA_DIR"
      : "Default location";
    return $"{folder}\n{source}";
  }

  internal static string? FindOnPath(string? pathVariable) =>
    (pathVariable ?? string.Empty)
      .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .Select(directory => Path.Combine(directory, "bw.exe"))
      .FirstOrDefault(File.Exists);

  internal SetSettingResponse SetSetting(SetSettingRequest request)
  {
    var manager = Manager();
    switch (manager.OrderedSettings.FirstOrDefault(s => KeyOf(s) == request.Key))
    {
      case ToggleSetting toggle when request.Value is "true" or "false":
        toggle.Value = request.Value == "true";
        break;
      case ChoiceSetSetting choice when choice.Choices.Any(c => c.Value == request.Value):
        choice.Value = request.Value;
        break;
      case TextSetting text when request.Value.Length <= MaxSettingTextLength && !request.Value.Any(char.IsControl):
        text.Value = request.Value;
        break;
      case null or SectionHeaderSetting:
        return new SetSettingResponse(false, "Unknown setting");
      default:
        return new SetSettingResponse(false, "Invalid value for this setting");
    }

    manager.RaiseChanged();
    return new SetSettingResponse(true, null);
  }

  private BitwardenSettingsManager Manager() => _service.Settings ?? throw new IpcException(IpcErrors.Failed);

  private static string? KeyOf(object setting) => setting switch
  {
    Setting<bool> flag => flag.Key,
    Setting<string> text => text.Key,
    _ => null,
  };

  private static SettingDto? DescribeSetting(object setting) => setting switch
  {
    SectionHeaderSetting section => new SettingDto(section.Key, SettingKinds.Section, section.Label ?? string.Empty, null, null, null),
    ToggleSetting toggle => new SettingDto(toggle.Key, SettingKinds.Toggle, toggle.Label ?? string.Empty, toggle.Description, toggle.Value ? "true" : "false", null),
    ChoiceSetSetting choice => new SettingDto(choice.Key, SettingKinds.Choice, choice.Label ?? string.Empty, choice.Description, choice.Value, choice.Choices.Select(c => new ChoiceDto(c.Title, c.Value, BitwardenSettingsManager.ChoiceDescription(choice.Key, c.Value))).ToArray()),
    TextSetting text => new SettingDto(text.Key, SettingKinds.Text, text.Label ?? string.Empty, text.Description, text.Value ?? string.Empty, null),
    _ => null,
  };
}
