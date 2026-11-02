using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal static class CompanionUnavailable
{
  internal const string Message = "The companion window could not start. Restart PowerToys or reinstall the extension.";

  internal static AnonymousCommand Command() =>
    new(() => { }) { Name = "Companion unavailable", Result = CommandResult.ShowToast(Message) };

  internal static ListItem Item(string title) => new(Command())
  {
    Title = title,
    Subtitle = Message,
    Icon = new IconInfo(""),
  };
}
