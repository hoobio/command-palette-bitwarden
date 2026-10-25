using System;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class LoginPage : ContentPage
{
  private readonly LoginForm _form;

  public LoginPage(BitwardenSettingsManager? settings = null, Action<string, string, int?>? onSubmit = null, Action<string, string>? onApiKeySubmit = null)
  {
    Name = "Login";
    Title = "Login to Bitwarden";
    Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    _form = new LoginForm(settings, onSubmit, onApiKeySubmit);
  }

  public override IContent[] GetContent()
  {
    EnterKeySubmitService.Arm(LoginForm.SubmitButtonTitle);
    return [_form];
  }
}

internal sealed partial class LoginForm : FormContent
{
  internal const string SubmitButtonTitle = "Login";

  private const string NoMethodValue = "none";

  private const string ApiKeyHelpUrl = "https://bitwarden.com/help/personal-api-key/";

  private readonly BitwardenSettingsManager? _settings;
  private readonly Action<string, string, int?>? _onSubmit;
  private readonly Action<string, string>? _onApiKeySubmit;
  private readonly bool _useApiKey;

  private string BuildTemplate()
  {
    var rememberChecked = _settings?.RememberSession.Value == true;
    var customDataDir = BitwardenCliService.ResolveDataDirectory(
        _settings?.CliDirectoryOverride.Value,
        _settings?.UsePortableDataDirectory.Value ?? false,
        _settings?.CliDataDirectoryOverride.Value);
    var dataDirWarningBlock = customDataDir != null && !_useApiKey
        ? BuildCustomDataDirWarningBlock(customDataDir)
        : string.Empty;
    var credentialBlocks = _useApiKey ? ApiKeyCredentialBlocks : PasswordCredentialBlocks;
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
            {{dataDirWarningBlock}}
            {{credentialBlocks}}
            {
                "type": "ActionSet",
                "actions": [
                    {
                        "type": "Action.Submit",
                        "title": "{{SubmitButtonTitle}}"
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
            }
        ]
    }
    """;
  }

  private const string PasswordCredentialBlocks = $$"""
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
                "type": "Input.ChoiceSet",
                "label": "Two-factor method (if prompted)",
                "id": "TwoFactorMethod",
                "value": "0",
                "style": "compact",
                "choices": [
                    { "title": "Authenticator app", "value": "0" },
                    { "title": "Email", "value": "1" },
                    { "title": "YubiKey OTP", "value": "3" },
                    { "title": "Auto-detect", "value": "{{NoMethodValue}}" }
                ]
            },
            {
                "type": "TextBlock",
                "text": "For Duo Push or WebAuthn, turn on 'Use API Key Authentication' in settings, or run `bw login` in a terminal first, then unlock here with your master password.",
                "wrap": true,
                "isSubtle": true,
                "size": "small"
            },
""";

  private const string ApiKeyCredentialBlocks = $$"""
            {
                "type": "Input.Text",
                "label": "Client ID",
                "id": "ClientId",
                "isRequired": true,
                "errorMessage": "Client ID is required",
                "placeholder": "user.xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
            },
            {
                "type": "Input.Text",
                "label": "Client Secret",
                "style": "Password",
                "id": "ClientSecret",
                "isRequired": true,
                "errorMessage": "Client secret is required",
                "placeholder": "Enter your client secret"
            },
            {
                "type": "TextBlock",
                "text": "Find your personal API key in the Bitwarden web vault under Settings > Security > Keys > View API key. It is stored in Windows Credential Manager, so you only enter it once. [Learn more]({{ApiKeyHelpUrl}})",
                "wrap": true,
                "isSubtle": true,
                "size": "small"
            },
""";

  public LoginForm(BitwardenSettingsManager? settings = null, Action<string, string, int?>? onSubmit = null, Action<string, string>? onApiKeySubmit = null)
  {
    _settings = settings;
    _onSubmit = onSubmit;
    _onApiKeySubmit = onApiKeySubmit;
    _useApiKey = settings?.UseApiKeyAuthentication.Value == true;
    TemplateJson = BuildTemplate();
  }

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();
    var (first, second) = _useApiKey
        ? (formInput?["ClientId"]?.GetValue<string>()?.Trim(), formInput?["ClientSecret"]?.GetValue<string>()?.Trim())
        : (formInput?["Email"]?.GetValue<string>()?.Trim(), formInput?["MasterPassword"]?.GetValue<string>());

    // Stay armed on an empty submit: disarming here would disable Enter for the rest of the form's life.
    if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
      return CommandResult.KeepOpen();

    EnterKeySubmitService.Disarm();

    var remember = formInput?["RememberSession"]?.GetValue<string>() == "true";
    if (_settings != null && _settings.RememberSession.Value != remember)
    {
      _settings.RememberSession.Value = remember;
      _settings.SaveSettings();
    }

    if (_useApiKey)
    {
      _onApiKeySubmit?.Invoke(first, second);
      return CommandResult.GoBack();
    }

    int? twoFactorMethod = ParseTwoFactorMethod(formInput?["TwoFactorMethod"]?.GetValue<string>());

    _onSubmit?.Invoke(first, second, twoFactorMethod);
    return CommandResult.GoBack();
  }

  internal static int? ParseTwoFactorMethod(string? raw)
  {
    if (string.IsNullOrEmpty(raw) || string.Equals(raw, NoMethodValue, StringComparison.OrdinalIgnoreCase))
      return null;
    return int.TryParse(raw, out var method) ? method : 0;
  }

  internal static string BuildCustomDataDirWarningBlock(string dataDir)
  {
    var escaped = System.Text.Json.JsonEncodedText.Encode(dataDir).ToString();
    return $$"""
            {
                "type": "TextBlock",
                "text": "⚠ Custom CLI data directory: {{escaped}}\n\nFor an interactive `bw login` (Duo Push, WebAuthn) to share session with this extension, set `BITWARDENCLI_APPDATA_DIR` in your terminal to that path before running `bw login`. Otherwise the CLI will write auth state to a different location and the extension won't see it.",
                "wrap": true,
                "color": "attention",
                "size": "small"
            },
            """;
  }
}
