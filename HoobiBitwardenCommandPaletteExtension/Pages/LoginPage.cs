using System;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class LoginPage : ContentPage
{
  private readonly LoginForm _form;

  public LoginPage(BitwardenCliService service, BitwardenSettingsManager? settings = null,
    Action<string, string>? onSubmit = null, string? initialEmail = null,
    string? initialClientId = null, string? initialClientSecret = null)
  {
    Name = "Login";
    Title = "Login to Bitwarden";
    Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    _form = new LoginForm(service, settings, onSubmit, initialEmail, initialClientId, initialClientSecret);
  }

  public override IContent[] GetContent() => [_form];
}

internal sealed partial class LoginForm : FormContent
{
  private readonly BitwardenCliService _service;
  private readonly BitwardenSettingsManager? _settings;
  private readonly Action<string, string>? _onSubmit;
  private readonly string? _initialEmail;
  private readonly string? _initialClientId;
  private readonly string? _initialClientSecret;

  private static string JsonEscape(string? value) =>
    string.IsNullOrEmpty(value) ? "" : value.Replace("\\", "\\\\").Replace("\"", "\\\"");

  private string BuildTemplate()
  {
    if (_service.IsApiKeyAuthEnabled)
      return BuildApiKeyTemplate();
    return BuildPasswordTemplate();
  }

  private string BuildPasswordTemplate()
  {
    var rememberChecked = _settings?.RememberSession.Value == true;
    var emailValue = JsonEscape(_initialEmail);
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
                "style": "Email",
                "value": "{{emailValue}}"
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

  private string BuildApiKeyTemplate()
  {
    var clientIdValue = JsonEscape(_initialClientId);
    var clientSecretValue = JsonEscape(_initialClientSecret);
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
                "label": "Client ID",
                "id": "ClientId",
                "isRequired": true,
                "errorMessage": "Client ID is required",
                "placeholder": "user.xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
                "value": "{{clientIdValue}}"
            },
            {
                "type": "Input.Text",
                "label": "Client Secret",
                "style": "Password",
                "id": "ClientSecret",
                "isRequired": true,
                "errorMessage": "Client Secret is required",
                "placeholder": "Enter your client secret",
                "value": "{{clientSecretValue}}"
            },
            {
                "type": "ActionSet",
                "actions": [
                    {
                        "type": "Action.Submit",
                        "title": "Login with API Key"
                    }
                ]
            },
            {
                "type": "TextBlock",
                "text": "Get your API key from the Bitwarden web vault: **Settings \u2192 Security \u2192 Keys**",
                "wrap": true,
                "isSubtle": true,
                "size": "small"
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

  public LoginForm(BitwardenCliService service, BitwardenSettingsManager? settings = null,
    Action<string, string>? onSubmit = null, string? initialEmail = null,
    string? initialClientId = null, string? initialClientSecret = null)
  {
    _service = service;
    _settings = settings;
    _onSubmit = onSubmit;
    _initialEmail = initialEmail;
    _initialClientId = initialClientId;
    _initialClientSecret = initialClientSecret;
    TemplateJson = BuildTemplate();
  }

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();

    if (_service.IsApiKeyAuthEnabled)
    {
      var clientId = formInput?["ClientId"]?.GetValue<string>()?.Trim();
      var clientSecret = formInput?["ClientSecret"]?.GetValue<string>();
      if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        return CommandResult.KeepOpen();
      _onSubmit?.Invoke(clientId, clientSecret);
      return CommandResult.GoBack();
    }

    var email = formInput?["Email"]?.GetValue<string>()?.Trim();
    var password = formInput?["MasterPassword"]?.GetValue<string>();

    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
      return CommandResult.KeepOpen();

    var remember = formInput?["RememberSession"]?.GetValue<string>() == "true";
    if (_settings != null && _settings.RememberSession.Value != remember)
      _settings.RememberSession.Value = remember;

    _onSubmit?.Invoke(email, password);
    return CommandResult.GoBack();
  }
}
