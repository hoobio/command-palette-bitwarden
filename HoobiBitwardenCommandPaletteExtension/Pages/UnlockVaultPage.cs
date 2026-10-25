using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class UnlockVaultPage : ContentPage
{
  private readonly UnlockForm _form;

  public UnlockVaultPage(BitwardenSettingsManager? settings = null, Action<string>? onSubmit = null, Action? onBiometricUnlock = null)
  {
    Name = "Unlock";
    Title = "Unlock Bitwarden Vault";
    Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    _form = new UnlockForm(settings, onSubmit, onBiometricUnlock);
  }

  public override IContent[] GetContent()
  {
    EnterKeySubmitService.Arm(UnlockForm.SubmitButtonTitle);
    return [_form];
  }
}

internal sealed partial class UnlockForm : FormContent
{
  internal const string SubmitButtonTitle = "Unlock";

  private readonly BitwardenSettingsManager? _settings;
  private readonly Action<string>? _onSubmit;
  private readonly Action? _onBiometricUnlock;

  private string BuildFormTemplate()
  {
    var rememberChecked = _settings?.RememberSession.Value == true;
    var showWindowsHello = _settings?.UseDesktopIntegration.Value == true;
    // Left padding of half the icon height offsets the renderer's 8px spacer after an icon-only button's image, so the glyph sits centred.
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
                        "title": "{{SubmitButtonTitle}}",
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
            }
        ]
    }
    """;
  }

  public UnlockForm(BitwardenSettingsManager? settings = null, Action<string>? onSubmit = null, Action? onBiometricUnlock = null)
  {
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
      EnterKeySubmitService.Disarm();
      _onBiometricUnlock?.Invoke();
      return CommandResult.GoBack();
    }

    // Stay armed on an empty submit: disarming here would disable Enter for the rest of the form's life.
    var password = formInput?["MasterPassword"]?.GetValue<string>();
    if (string.IsNullOrEmpty(password))
      return CommandResult.KeepOpen();

    EnterKeySubmitService.Disarm();
    _onSubmit?.Invoke(password);
    return CommandResult.GoBack();
  }
}
