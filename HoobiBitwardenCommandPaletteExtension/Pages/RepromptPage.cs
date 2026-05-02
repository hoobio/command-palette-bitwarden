using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Pages;

internal record BiometricVerificationRequest(string ItemId, BitwardenCliService Service, Action InnerAction, string ActionLabel);

internal sealed partial class RepromptPage : ContentPage
{
  internal static int GracePeriodSeconds { get; set; } = 60;

  // Per-item grace timestamps (Stopwatch ticks). In-memory only: verification
  // never persists across process restarts, and a verified item only grants
  // grace for that specific item, not for the vault as a whole.
  private static readonly ConcurrentDictionary<string, long> _verifiedItems = new();
  private static int _failureCount;
  private static long _cooldownUntilTicks;
  private const int MaxFailuresBeforeCooldown = 5;
  private const int CooldownSeconds = 30;

  internal static event Action? GraceStarted;
  internal static event Action<BiometricVerificationRequest>? BiometricRequested;

  internal static bool IsWithinGracePeriod(string itemId)
  {
    if (GracePeriodSeconds <= 0 || string.IsNullOrEmpty(itemId)) return false;
    if (!_verifiedItems.TryGetValue(itemId, out var ts)) return false;
    if (Stopwatch.GetElapsedTime(ts).TotalSeconds < GracePeriodSeconds)
      return true;

    _verifiedItems.TryRemove(itemId, out _);
    return false;
  }

  internal static void RecordVerification(string itemId)
  {
    if (string.IsNullOrEmpty(itemId)) return;
    _verifiedItems[itemId] = Stopwatch.GetTimestamp();
    Interlocked.Exchange(ref _failureCount, 0);
    GraceStarted?.Invoke();
  }

  internal static void ClearGracePeriod()
  {
    _verifiedItems.Clear();
    Interlocked.Exchange(ref _failureCount, 0);
    Interlocked.Exchange(ref _cooldownUntilTicks, 0);
  }

  internal static int GetCooldownSecondsRemaining()
  {
    var ticks = Interlocked.Read(ref _cooldownUntilTicks);
    if (ticks == 0) return 0;
    var remaining = (new DateTime(ticks, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
    return remaining > 0 ? (int)Math.Ceiling(remaining) : 0;
  }

  internal static void RecordFailure()
  {
    var failures = Interlocked.Increment(ref _failureCount);
    if (failures >= MaxFailuresBeforeCooldown)
      Interlocked.Exchange(ref _cooldownUntilTicks, DateTime.UtcNow.AddSeconds(CooldownSeconds).Ticks);
  }

  internal static void RaiseBiometricRequested(BiometricVerificationRequest request) =>
    BiometricRequested?.Invoke(request);

  private readonly RepromptForm _form;

  public RepromptPage(BitwardenCliService service, string itemId, Action innerAction, string actionLabel)
  {
    Name = "Verify Password";
    Title = "Master Password Required";
    Icon = new IconInfo("");
    _form = new RepromptForm(service, itemId, innerAction, actionLabel);
  }

  public override IContent[] GetContent() => [_form];
}

internal sealed partial class RepromptForm : FormContent
{
  private readonly BitwardenCliService _service;
  private readonly string _itemId;
  private readonly Action _innerAction;
  private readonly string _actionLabel;
  private string? _errorText;

  public RepromptForm(BitwardenCliService service, string itemId, Action innerAction, string actionLabel)
  {
    _service = service;
    _itemId = itemId;
    _innerAction = innerAction;
    _actionLabel = actionLabel;
    TemplateJson = BuildTemplate();
  }

  private static string EscapeJsonString(string value)
  {
    var sb = new System.Text.StringBuilder(value.Length);
    foreach (var c in value)
    {
      switch (c)
      {
        case '"': sb.Append("\\\""); break;
        case '\\': sb.Append("\\\\"); break;
        case '\b': sb.Append("\\b"); break;
        case '\f': sb.Append("\\f"); break;
        case '\n': sb.Append("\\n"); break;
        case '\r': sb.Append("\\r"); break;
        case '\t': sb.Append("\\t"); break;
        default:
          if (c < 0x20)
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
          else
            sb.Append(c);
          break;
      }
    }
    return sb.ToString();
  }

  private string BuildTemplate()
  {
    var biometricEnabled = _service.Settings?.UseDesktopIntegration.Value == true;
    var biometricAction = biometricEnabled ? """
                    ,
                    {
                        "type": "Action.Submit",
                        "title": "Use Windows Hello",
                        "data": { "action": "biometric" }
                    }
""" : "";

    return """
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
                "placeholder": "Enter your master password"
            },
            {
                "type": "ActionSet",
                "actions": [
                    {
                        "type": "Action.Submit",
                        "title": "Verify & Continue",
                        "data": { "action": "password" }
                    }
""" + biometricAction + """
                ]
            }
""" + (_errorText != null ? $$"""
            ,{
                "type": "TextBlock",
                "text": "{{EscapeJsonString(_errorText)}}",
                "color": "Attention",
                "wrap": true,
                "size": "small"
            }
""" : "") + """
        ]
    }
    """;
  }

  public override ICommandResult SubmitForm(string inputs, string data)
  {
    var actionData = JsonNode.Parse(data)?.AsObject();
    var action = actionData?["action"]?.GetValue<string>() ?? "password";

    var cooldown = RepromptPage.GetCooldownSecondsRemaining();
    if (cooldown > 0)
    {
      _errorText = $"Too many failed attempts. Try again in {cooldown}s.";
      TemplateJson = BuildTemplate();
      return CommandResult.KeepOpen();
    }

    return action switch
    {
      "biometric" => HandleBiometric(),
      _ => HandlePassword(inputs),
    };
  }

  // Biometric verify is handled by the parent page so the WinHello prompt
  // can come to the foreground. Doing it inline on the form leaves the
  // adaptive-card UI mounted, and the WinHello prompt z-orders behind it
  // (the prompt only becomes visible after the palette closes, by which
  // time it has already been treated as cancelled).
  private CommandResult HandleBiometric()
  {
    RepromptPage.RaiseBiometricRequested(
      new BiometricVerificationRequest(_itemId, _service, _innerAction, _actionLabel));
    return CommandResult.GoBack();
  }

  private CommandResult HandlePassword(string inputs)
  {
    var formInput = JsonNode.Parse(inputs)?.AsObject();
    var password = formInput?["MasterPassword"]?.GetValue<string>();

    if (string.IsNullOrEmpty(password))
      return CommandResult.KeepOpen();

    var verifyingStatus = new StatusMessage { Message = "Verifying master password...", State = MessageState.Info };
    ExtensionHost.ShowStatus(verifyingStatus, StatusContext.Page);

    bool verified;
    try
    {
#pragma warning disable VSTHRD002
      verified = Task.Run(() => _service.VerifyMasterPasswordAsync(password)).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Reprompt", $"Verify exception: {ex.GetType().Name}: {ex.Message}");
      _errorText = "Verification failed. Please try again.";
      TemplateJson = BuildTemplate();
      return CommandResult.KeepOpen();
    }
    finally
    {
      try { ExtensionHost.HideStatus(verifyingStatus); } catch { }
    }

    if (!verified)
    {
      RepromptPage.RecordFailure();
      var nextCooldown = RepromptPage.GetCooldownSecondsRemaining();
      _errorText = nextCooldown > 0
        ? $"Too many failed attempts. Try again in {nextCooldown}s."
        : "Incorrect master password. Please try again.";
      TemplateJson = BuildTemplate();
      return CommandResult.KeepOpen();
    }

    RepromptPage.RecordVerification(_itemId);
    try { _innerAction(); }
    catch (Exception ex)
    {
      DebugLogService.Log("Reprompt", $"Inner action exception: {ex.GetType().Name}: {ex.Message}");
      _errorText = "Action failed after verification.";
      TemplateJson = BuildTemplate();
      return CommandResult.KeepOpen();
    }

    return CommandResult.ShowToast($"Copied {_actionLabel} to clipboard");
  }
}
