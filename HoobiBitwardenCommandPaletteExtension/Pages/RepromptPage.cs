using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal record VerificationRequest(string Password, BitwardenCliService Service, Action InnerAction, string ActionLabel);

internal sealed partial class RepromptPage : ContentPage
{
  internal static int GracePeriodSeconds { get; set; } = 60;
  private static DateTime _lastVerified = DateTime.MinValue;

  internal static event Action? GraceStarted;
  internal static event Action<VerificationRequest>? VerificationRequested;

  internal static bool IsWithinGracePeriod() =>
    GracePeriodSeconds > 0 && (DateTime.UtcNow - _lastVerified).TotalSeconds < GracePeriodSeconds;

  internal static void RecordVerification()
  {
    _lastVerified = DateTime.UtcNow;
    GraceStarted?.Invoke();
  }

  internal static void ClearGracePeriod() => _lastVerified = DateTime.MinValue;

  internal static void RaiseVerificationRequested(VerificationRequest request) =>
    VerificationRequested?.Invoke(request);

  private readonly RepromptForm _form;

  public RepromptPage(BitwardenCliService service, Action innerAction, string actionLabel, ICommandResult? successResult = null)
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
    TemplateJson = BuildInitialTemplate();
  }

  internal void ShowError()
  {
    _showError = true;
    TemplateJson = BuildErrorTemplate();
  }

  internal void ResetError()
  {
    if (_showError)
    {
      _showError = false;
      TemplateJson = BuildInitialTemplate();
    }
  }

  private static string BuildInitialTemplate() => """
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
            }
        ]
    }
    """;

  private static string BuildErrorTemplate() => """
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
            },
            {
                "type": "TextBlock",
                "text": "Incorrect master password. Please try again.",
                "color": "Attention",
                "wrap": true,
                "size": "small"
            }
        ]
    }
    """;

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();
    var password = formInput?["MasterPassword"]?.GetValue<string>();

    if (string.IsNullOrEmpty(password))
      return CommandResult.KeepOpen();

    RepromptPage.RaiseVerificationRequested(
      new VerificationRequest(password, _service, _innerAction, _actionLabel));

    return CommandResult.GoBack();
  }
}
