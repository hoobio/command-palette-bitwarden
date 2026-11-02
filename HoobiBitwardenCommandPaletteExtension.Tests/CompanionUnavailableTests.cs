using HoobiBitwardenCommandPaletteExtension.Companion;
using Microsoft.CommandPalette.Extensions;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class CompanionUnavailableTests
{
  [Fact]
  public void Command_ShowsTheRestartMessage_AndDoesNothingWhenInvoked()
  {
    var command = CompanionUnavailable.Command();

    Assert.Equal("Companion unavailable", command.Name);
    Assert.NotNull(((IInvokableCommand)command).Invoke(null!));
    Assert.Contains("Restart PowerToys", CompanionUnavailable.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Item_CarriesTheTitleAndTheMessage()
  {
    var item = CompanionUnavailable.Item("Login to Bitwarden");

    Assert.Equal("Login to Bitwarden", item.Title);
    Assert.Equal(CompanionUnavailable.Message, item.Subtitle);
    Assert.Equal(CompanionUnavailable.Command().Name, item.Command!.Name);
  }
}
