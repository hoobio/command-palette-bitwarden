using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Pages;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class SettingsWithButtonTests
{
  [Fact]
  public void SubmitForm_InvokesOpenAndKeepsPaletteOpen()
  {
    var opened = 0;
    var form = new OpenWindowForm(() => opened++);

    var result = form.SubmitForm("{}");

    Assert.Equal(1, opened);
    Assert.Equal(CommandResultKind.KeepOpen, result.Kind);
  }

  [Fact]
  public void GetContent_ReturnsOnlyTheOpenWindowButton()
  {
    var page = new SettingsWithButtonPage(() => { });

    Assert.IsType<OpenWindowForm>(Assert.Single(page.GetContent()));
  }

  [Fact]
  public void SettingsPage_ReturnsSameInstanceEveryTime()
  {
    var settings = new SettingsWithButton(() => { });

    Assert.Same(settings.SettingsPage, settings.SettingsPage);
  }
}
