using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal sealed partial class RepromptPage : ContentPage
{
  private readonly RepromptForm _form;

  public RepromptPage(BitwardenCliService service, Action innerAction, string actionLabel)
  {
    Name = "Verify Password";
    Title = "Master Password Required";
    Icon = new IconInfo("\uE72E");
    _form = new RepromptForm(service, innerAction, actionLabel);
  }

  public override IContent[] GetContent() => [_form];
}

internal sealed partial class RepromptForm : FormContent
{
  private readonly BitwardenCliService _service;
  private readonly Action _innerAction;
  private readonly string _actionLabel;
  private bool _showError;

  public RepromptForm(BitwardenCliService service, Action innerAction, string actionLabel)
  {
    _service = service;
    _innerAction = innerAction;
    _actionLabel = actionLabel;
    TemplateJson = BuildTemplate();
  }

  private string BuildTemplate()
  {
    var errorBlock = _showError
        ? """
          ,{
              "type": "TextBlock",
              "text": "Incorrect master password. Please try again.",
              "color": "Attention",
              "wrap": true,
              "size": "small"
          }
          """
        : "";

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
                "text": "Re-enter your master password",
                "horizontalAlignment": "center",
                "wrap": true,
                "style": "heading"
            },
            {
                "type": "TextBlock",
                "text": "This item requires master password verification before you can access it.",
                "wrap": true,
                "isSubtle": true,
                "size": "small"
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
                        "title": "Verify & Continue"
                    }
                ]
            }{{errorBlock}}
        ]
    }
    """;
  }

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();
    var password = formInput?["MasterPassword"]?.GetValue<string>();

    if (string.IsNullOrEmpty(password))
      return CommandResult.KeepOpen();

    // SubmitForm is synchronous by SDK design (IFormContent.SubmitForm returns ICommandResult).
    // bw unlock is a fast local crypto operation with no SynchronizationContext to deadlock on.
#pragma warning disable VSTHRD002
    var verified = _service.VerifyMasterPasswordAsync(password).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

    if (!verified)
    {
      _showError = true;
      TemplateJson = BuildTemplate();
      return CommandResult.KeepOpen();
    }

    _innerAction();
    return CommandResult.ShowToast($"Copied {_actionLabel} to clipboard");
  }
}
