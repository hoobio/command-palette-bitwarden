using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class UnlockVaultPage : ContentPage
{
  private readonly UnlockForm _form;

  public UnlockVaultPage(BitwardenCliService service, BitwardenSettingsManager? settings = null)
  {
    Name = "Unlock";
    Title = "Unlock Bitwarden Vault";
    Icon = new IconInfo("\uE72E");
    _form = new UnlockForm(service, settings);
  }

  public override IContent[] GetContent() => [_form];
}

internal sealed partial class UnlockForm : FormContent
{
  private readonly BitwardenCliService _service;
  private readonly BitwardenSettingsManager? _settings;

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
                "type": "Input.Toggle",
                "id": "RememberSession",
                "title": "Remember session (stay unlocked between launches)",
                "valueOn": "true",
                "valueOff": "false",
                "value": "{{(rememberChecked ? "true" : "false")}}"
            }
        ],
        "actions": [
            {
                "type": "Action.Submit",
                "title": "\uE785  Unlock"
            }
        ]
    }
    """;
  }

  private static readonly string LoadingTemplate = """
  {
      "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
      "type": "AdaptiveCard",
      "version": "1.6",
      "body": [
          {
              "type": "TextBlock",
              "size": "medium",
              "weight": "bolder",
              "text": "Unlocking vault...",
              "horizontalAlignment": "center",
              "wrap": true,
              "style": "heading"
          },
          {
              "type": "TextBlock",
              "text": "Please wait while your vault is being unlocked.",
              "horizontalAlignment": "center",
              "wrap": true
          }
      ]
  }
  """;

  public UnlockForm(BitwardenCliService service, BitwardenSettingsManager? settings = null)
  {
    _service = service;
    _settings = settings;
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
      _settings.RememberSession.Value = remember;

    TemplateJson = LoadingTemplate;

    var sessionKey = Task.Run(() => _service.UnlockAsync(password)).GetAwaiter().GetResult();

    if (sessionKey == null)
    {
      TemplateJson = BuildFormTemplate();
      return CommandResult.ShowToast("Unlock failed - check your master password");
    }

    return CommandResult.GoBack();
  }
}
