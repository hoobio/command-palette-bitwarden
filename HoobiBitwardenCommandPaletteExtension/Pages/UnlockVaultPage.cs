using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class UnlockVaultPage : ContentPage
{
  private readonly UnlockForm _form;

  public UnlockVaultPage(BitwardenCliService service, BitwardenSettingsManager? settings = null, Action<string>? onSubmit = null)
  {
    Name = "Unlock";
    Title = "Unlock Bitwarden Vault";
    Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    _form = new UnlockForm(service, settings, onSubmit);
  }

  public override IContent[] GetContent() => [_form];
}

internal sealed partial class UnlockForm : FormContent
{
  private readonly BitwardenCliService _service;
  private readonly BitwardenSettingsManager? _settings;
  private readonly Action<string>? _onSubmit;

  private string BuildFormTemplate()
  {
    var rememberChecked = _settings?.RememberSession.Value == true;
    return $$"""
    {
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "type": "AdaptiveCard",
        "version": "1.6",
        "body": [
            {
                "type": "TextBlock",
                "size": "medium",
                "weight": "bolder",
                "text": "Unlock your Bitwarden vault",
                "horizontalAlignment": "center",
                "wrap": true,
                "style": "heading"
            },
            {
                "type": "Input.Text",
                "label": "Master Password",
                "style": "Password",
                "id": "MasterPassword",
                "isRequired": true,
                "errorMessage": "Master password is required",
                "placeholder": "Enter your master password"
            },
            {
                "type": "ActionSet",
                "actions": [
                    {
                        "type": "Action.Submit",
                        "title": "Unlock"
                    }
                ]
            },
            {
                "type": "Input.Toggle",
                "id": "RememberSession",
                "title": "Remember session (stay unlocked between launches)",
                "valueOn": "true",
                "valueOff": "false",
                "value": "{{(rememberChecked ? "true" : "false")}}"
            },
            {
                "type": "TextBlock",
                "text": "[Upvote this issue](https://github.com/microsoft/PowerToys/issues/46003) to help bring Enter key support.",
                "wrap": true,
                "isSubtle": true,
                "size": "small"
            }
        ]
    }
    """;
  }

  public UnlockForm(BitwardenCliService service, BitwardenSettingsManager? settings = null, Action<string>? onSubmit = null)
  {
    _service = service;
    _settings = settings;
    _onSubmit = onSubmit;
    TemplateJson = BuildFormTemplate();
  }

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();
    var password = formInput?["MasterPassword"]?.GetValue<string>();

    if (string.IsNullOrEmpty(password))
      return CommandResult.KeepOpen();

    var remember = formInput?["RememberSession"]?.GetValue<string>() == "true";
    if (_settings != null && _settings.RememberSession.Value != remember)
    {
      _settings.RememberSession.Value = remember;
      _settings.SaveSettings();
    }

    _onSubmit?.Invoke(password);
    return CommandResult.GoBack();
  }
}
