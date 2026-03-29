using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class UnlockVaultPage : ContentPage
{
  private readonly UnlockForm _form;

  public UnlockVaultPage(BitwardenCliService service, BitwardenSettingsManager? settings = null, Action<string>? onSubmit = null, Action? onBiometricUnlock = null)
  {
    Name = "Unlock";
    Title = "Unlock Bitwarden Vault";
    Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    _form = new UnlockForm(service, settings, onSubmit, onBiometricUnlock);
  }

  public override IContent[] GetContent() => [_form];
}

internal sealed partial class UnlockForm : FormContent
{
  private readonly BitwardenCliService _service;
  private readonly BitwardenSettingsManager? _settings;
  private readonly Action<string>? _onSubmit;
  private readonly Action? _onBiometricUnlock;

  private string BuildFormTemplate()
  {
    var rememberChecked = _settings?.RememberSession.Value == true;
    var showWindowsHello = _settings?.UseDesktopIntegration.Value == true;
    var windowsHelloAction = showWindowsHello ? """
                    ,
                    {
                        "type": "Action.Submit",
                        "tooltip": "Windows Hello",
                        "title": null,
                        "iconUrl": "data:image/png;base64,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",
                        "data": { "action": "biometric" }
                    }
""" : "";
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
                "placeholder": "Enter your master password"
            },
            {
                "type": "ActionSet",
                "actions": [
                    {
                        "type": "Action.Submit",
                        "title": "Unlock",
                        "data": { "action": "password" }
                    }{{windowsHelloAction}}
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

  public UnlockForm(BitwardenCliService service, BitwardenSettingsManager? settings = null, Action<string>? onSubmit = null, Action? onBiometricUnlock = null)
  {
    _service = service;
    _settings = settings;
    _onSubmit = onSubmit;
    _onBiometricUnlock = onBiometricUnlock;
    TemplateJson = BuildFormTemplate();
  }

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();
    var actionData = JsonNode.Parse(data)?.AsObject();
    var action = actionData?["action"]?.GetValue<string>();

    var remember = formInput?["RememberSession"]?.GetValue<string>() == "true";
    if (_settings != null && _settings.RememberSession.Value != remember)
    {
      _settings.RememberSession.Value = remember;
      _settings.SaveSettings();
    }

    if (action == "biometric")
    {
      _onBiometricUnlock?.Invoke();
      return CommandResult.KeepOpen();
    }

    var password = formInput?["MasterPassword"]?.GetValue<string>();
    if (string.IsNullOrEmpty(password))
      return CommandResult.KeepOpen();

    _onSubmit?.Invoke(password);
    return CommandResult.GoBack();
  }
}
