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

internal sealed partial class SettingsWithButtonPage(Action open) : ContentPage
{
  private readonly OpenWindowForm _button = new(open);

  public override IContent[] GetContent() => [_button];
}

internal sealed partial class SettingsWithButton(Action open) : ICommandSettings
{
  private readonly SettingsWithButtonPage _page = new(open);

  public IContentPage SettingsPage => _page;
}
