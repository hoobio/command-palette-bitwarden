using System;
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

  internal SettingsResponse GetSettings() =>
    new(Manager().OrderedSettings.Select(DescribeSetting).OfType<SettingDto>().ToArray());

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
    ChoiceSetSetting choice => new SettingDto(choice.Key, SettingKinds.Choice, choice.Label ?? string.Empty, choice.Description, choice.Value, choice.Choices.Select(c => new ChoiceDto(c.Title, c.Value)).ToArray()),
    TextSetting text => new SettingDto(text.Key, SettingKinds.Text, text.Label ?? string.Empty, text.Description, text.Value ?? string.Empty, null),
    _ => null,
  };
}
