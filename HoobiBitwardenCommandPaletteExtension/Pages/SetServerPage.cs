using System;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class SetServerPage : ContentPage
{
  private readonly SetServerForm _form;

  public SetServerPage(BitwardenCliService service, Action<string>? onSubmit = null)
  {
    Name = "Set Server";
    Title = "Set Bitwarden Server";
    Icon = new IconInfo("\uE774");
    _form = new SetServerForm(service, onSubmit);
  }

  public override IContent[] GetContent() => [_form];
}

internal sealed partial class SetServerForm : FormContent
{
  private readonly BitwardenCliService _service;
  private readonly Action<string>? _onSubmit;

  private static string BuildTemplate()
  {
    var currentServer = (BitwardenCliService.ServerUrl ?? "https://vault.bitwarden.com")
        .Replace("\\", "\\\\").Replace("\"", "\\\"");
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
                "text": "Set Bitwarden Server",
                "horizontalAlignment": "center",
                "wrap": true,
                "style": "heading"
            },
            {
                "type": "TextBlock",
                "text": "Current: {{currentServer}}",
                "wrap": true,
                "isSubtle": true
            },
            {
                "type": "Input.Text",
                "label": "Server URL",
                "id": "ServerUrl",
                "isRequired": true,
                "errorMessage": "Server URL is required",
                "placeholder": "https://your-server.example.com",
                "value": "{{currentServer}}"
            }
        ],
        "actions": [
            {
                "type": "Action.Submit",
                "title": "Save"
            }
        ]
    }
    """;
  }

  public SetServerForm(BitwardenCliService service, Action<string>? onSubmit = null)
  {
    _service = service;
    _onSubmit = onSubmit;
    TemplateJson = BuildTemplate();
  }

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();
    var url = formInput?["ServerUrl"]?.GetValue<string>()?.Trim();

    if (string.IsNullOrEmpty(url))
      return CommandResult.KeepOpen();

    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
      return CommandResult.ShowToast("Invalid URL: must start with https://");

    _onSubmit?.Invoke(url);
    return CommandResult.GoBack();
  }
}
