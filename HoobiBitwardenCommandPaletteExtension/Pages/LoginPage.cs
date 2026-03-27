using System;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class LoginPage : ContentPage
{
  private readonly LoginForm _form;

  public LoginPage(BitwardenCliService service, BitwardenSettingsManager? settings = null, Action<string, string>? onSubmit = null)
  {
    Name = "Login";
    Title = "Login to Bitwarden";
    Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    _form = new LoginForm(service, settings, onSubmit);
  }

  public override IContent[] GetContent() => [_form];
}

internal sealed partial class LoginForm : FormContent
{
  private readonly BitwardenCliService _service;
  private readonly BitwardenSettingsManager? _settings;
  private readonly Action<string, string>? _onSubmit;

  private string BuildTemplate()
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
                "text": "Login to Bitwarden",
                "horizontalAlignment": "center",
                "wrap": true,
                "style": "heading"
            },
            {
                "type": "Input.Text",
                "label": "Email",
                "id": "Email",
                "isRequired": true,
                "errorMessage": "Email is required",
                "placeholder": "your@email.com",
                "style": "Email"
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
                        "title": "Login"
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

  public LoginForm(BitwardenCliService service, BitwardenSettingsManager? settings = null, Action<string, string>? onSubmit = null)
  {
    _service = service;
    _settings = settings;
    _onSubmit = onSubmit;
    TemplateJson = BuildTemplate();
  }

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();
    var email = formInput?["Email"]?.GetValue<string>()?.Trim();
    var password = formInput?["MasterPassword"]?.GetValue<string>();

    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
      return CommandResult.KeepOpen();

    var remember = formInput?["RememberSession"]?.GetValue<string>() == "true";
    if (_settings != null && _settings.RememberSession.Value != remember)
    {
      _settings.RememberSession.Value = remember;
      _settings.SaveSettings();
    }

    _onSubmit?.Invoke(email, password);
    return CommandResult.GoBack();
  }
}
