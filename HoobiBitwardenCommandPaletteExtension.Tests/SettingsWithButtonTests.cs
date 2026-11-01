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
  public void GetContent_ReturnsButtonFirstThenSettingsForm()
  {
    var settings = new Settings();
    settings.Add(new ToggleSetting("flag", "Flag", "", false));
    var page = new SettingsWithButtonPage(settings, () => { });

    var content = page.GetContent();

    Assert.Equal(2, content.Length);
    Assert.IsType<OpenWindowForm>(content[0]);
    Assert.IsAssignableFrom<IFormContent>(content[1]);
  }

  [Fact]
  public void SettingsPage_ReturnsSameInstanceEveryTime()
  {
    var settings = new SettingsWithButton(new Settings(), () => { });

    Assert.Same(settings.SettingsPage, settings.SettingsPage);
  }

  [Fact]
  public void SettingsChanged_RaisesItemsChanged()
  {
    var settings = new Settings();
    var page = new SettingsWithButtonPage(settings, () => { });
    var raised = 0;
    page.ItemsChanged += (_, _) => raised++;

    typeof(Settings).GetMethod("RaiseSettingsChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.Invoke(settings, null);

    Assert.Equal(1, raised);
  }
}
