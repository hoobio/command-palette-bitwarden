using System;
using System.IO;
using System.Linq;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  private const int MaxSettingTextLength = 4096;

  internal event Action? SettingsChanged;

  private void RaiseSettingsChanged(object sender, Settings e) => SettingsChanged?.Invoke();

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

  internal static string RegionOf(string? serverUrl) => Pages.SetServerPage.DetectPreset(serverUrl) switch
  {
    "bitwarden.com" => ServerRegions.Cloud,
    "bitwarden.eu" => ServerRegions.Eu,
    _ => ServerRegions.SelfHosted,
  };

  private SettingDto[] ServerRows() => ServerRows(BitwardenCliService.ServerUrl, _service.LastStatus is VaultStatus.Locked or VaultStatus.Unlocked);

  internal static SettingDto[] ServerRows(string? url, bool loggedIn)
  {
    var region = RegionOf(url);
    var warning = "Changing the server logs you out";
    var confirm = loggedIn ? $"{warning} of Bitwarden. Continue?" : null;
    return
    [
      new("_section_server", SettingKinds.Section, "Server", null, null, null),
      new(SettingKeys.ServerRegion, SettingKinds.Choice, "Region", loggedIn ? warning : "Where your vault is hosted", region, ServerChoices, Confirm: confirm),
      new(SettingKeys.ServerUrl, SettingKinds.Text, "Server URL", "Address of your self-hosted server", region == ServerRegions.SelfHosted ? url : string.Empty, null, SettingKeys.ServerRegion, ServerRegions.SelfHosted, Placeholder: "https://vault.example.com", Confirm: confirm),
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
    key == manager.CliDirectoryOverride.Key ? CliTooltip(_service.CliExecutable, manager.CliDirectoryOverride.Value, Environment.GetEnvironmentVariable("PATH"))
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
