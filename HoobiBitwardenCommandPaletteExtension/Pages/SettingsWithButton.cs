using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class OpenWindowForm(Action open) : FormContent
{
  public override string TemplateJson { get; set; } =
    """{"type":"AdaptiveCard","version":"1.5","body":[],"actions":[{"type":"Action.Submit","title":"Open settings window"}]}""";

  public override ICommandResult SubmitForm(string inputs)
  {
    open();
    return CommandResult.KeepOpen();
  }
}

internal sealed partial class SettingsWithButtonPage : ContentPage
{
  private readonly Settings _settings;
  private readonly OpenWindowForm _button;

  public SettingsWithButtonPage(Settings settings, Action open)
  {
    _settings = settings;
    _button = new OpenWindowForm(open);
    _settings.SettingsChanged += (_, _) => RaiseItemsChanged();
  }

  public override IContent[] GetContent() => [_button, .. _settings.ToContent()];
}

internal sealed partial class SettingsWithButton(Settings settings, Action open) : ICommandSettings
{
  private readonly SettingsWithButtonPage _page = new(settings, open);

  public IContentPage SettingsPage => _page;
}
