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

internal sealed partial class RepromptPage : ContentPage
{
  internal static int GracePeriodSeconds { get; set; } = 60;

  // Per-item grace timestamps (Stopwatch ticks). In-memory only — verification
  // never persists across process restarts, and a verified item only grants
  // grace for that specific item, not for the vault as a whole.
  private static readonly ConcurrentDictionary<string, long> _verifiedItems = new();
  private static int _failureCount;
  private static long _cooldownUntilTicks;
  private const int MaxFailuresBeforeCooldown = 5;
  private const int CooldownSeconds = 30;

  internal static event Action? GraceStarted;

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

  private readonly RepromptForm _form;

  public RepromptPage(BitwardenCliService service, string itemId, Action innerAction, string actionLabel)
  {
    Name = "Verify Password";
    Title = "Master Password Required";
    Icon = new IconInfo("");
    _form = new RepromptForm(service, itemId, innerAction, actionLabel);
  }

  public override IContent[] GetContent()
  {
    _form.OnPageShown();
    return [_form];
  }
}

internal sealed partial class RepromptForm : FormContent
{
  private enum AuthState { Initial, Authenticating, Verified, Failed }

  private readonly BitwardenCliService _service;
  private readonly string _itemId;
  private readonly Action _innerAction;
  private readonly string _actionLabel;
  private AuthState _state = AuthState.Initial;
  private string? _errorText;
  private string? _statusText;
  private int _autoTriggered;

  public RepromptForm(BitwardenCliService service, string itemId, Action innerAction, string actionLabel)
  {
    _service = service;
    _itemId = itemId;
    _innerAction = innerAction;
    _actionLabel = actionLabel;
    TemplateJson = BuildTemplate();
  }

  internal void OnPageShown()
  {
    if (Interlocked.CompareExchange(ref _autoTriggered, 1, 0) != 0) return;
    if (!ShouldAutoTriggerBiometric()) return;

    _state = AuthState.Authenticating;
    _statusText = "Connecting to Bitwarden Desktop...";
    TemplateJson = BuildTemplate();
    _ = Task.Run(RunAutoBiometricAsync);
  }

  private bool ShouldAutoTriggerBiometric()
  {
    var settings = _service.Settings;
    if (settings?.UseDesktopIntegration.Value != true) return false;
    if (settings?.AutoBiometricUnlock.Value != true) return false;
    if (RepromptPage.GetCooldownSecondsRemaining() > 0) return false;
    return true;
  }

  private async Task RunAutoBiometricAsync()
  {
    var (success, error) = await _service.VerifyWithBiometricsAsync(
      onStatus: msg =>
      {
        _statusText = msg;
        TemplateJson = BuildTemplate();
      });

    _statusText = null;
    if (success)
    {
      _state = AuthState.Verified;
      _errorText = null;
    }
    else
    {
      RepromptPage.RecordFailure();
      _state = AuthState.Failed;
      var cooldown = RepromptPage.GetCooldownSecondsRemaining();
      _errorText = cooldown > 0
        ? $"Too many failed attempts. Try again in {cooldown}s."
        : error ?? "Biometric verification failed";
    }
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

  private string BuildTemplate() => _state switch
  {
    AuthState.Authenticating => BuildAuthenticatingTemplate(_statusText),
    AuthState.Verified => BuildVerifiedTemplate(),
    _ => BuildStandardTemplate(_errorText),
  };

  private static string BuildAuthenticatingTemplate(string? statusText) =>
    $$"""
    {
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "type": "AdaptiveCard",
        "version": "1.6",
        "body": [
            {
                "type": "TextBlock",
                "size": "medium",
                "weight": "bolder",
                "text": "Waiting for Windows Hello...",
                "horizontalAlignment": "center",
                "wrap": true,
                "style": "heading"
            },
            {
                "type": "TextBlock",
                "text": "{{EscapeJsonString(statusText ?? "Authenticating with Bitwarden Desktop app")}}",
                "wrap": true,
                "isSubtle": true,
                "size": "small",
                "horizontalAlignment": "center"
            }
        ]
    }
    """;

  private string BuildVerifiedTemplate() =>
    $$"""
    {
        "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
        "type": "AdaptiveCard",
        "version": "1.6",
        "body": [
            {
                "type": "TextBlock",
                "size": "medium",
                "weight": "bolder",
                "text": "Verified with Windows Hello",
                "horizontalAlignment": "center",
                "wrap": true,
                "style": "heading",
                "color": "Good"
            },
            {
                "type": "TextBlock",
                "text": "Press Enter to {{EscapeJsonString(string.Equals(_actionLabel, "open", StringComparison.OrdinalIgnoreCase) ? "open this item" : $"copy {_actionLabel.ToLowerInvariant()}")}}.",
                "wrap": true,
                "isSubtle": true,
                "size": "small",
                "horizontalAlignment": "center"
            },
            {
                "type": "ActionSet",
                "actions": [
                    {
                        "type": "Action.Submit",
                        "title": "Continue",
                        "style": "positive",
                        "data": { "action": "continue" }
                    }
                ]
            }
        ]
    }
    """;

  private string BuildStandardTemplate(string? errorText)
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
""" + (errorText != null ? $$"""
            ,{
                "type": "TextBlock",
                "text": "{{EscapeJsonString(errorText)}}",
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
      _state = AuthState.Failed;
      _errorText = $"Too many failed attempts. Try again in {cooldown}s.";
      TemplateJson = BuildTemplate();
      return CommandResult.KeepOpen();
    }

    return action switch
    {
      "biometric" => HandleBiometric(),
      "continue" => HandleContinueAfterAuth(),
      _ => HandlePassword(inputs),
    };
  }

  private CommandResult HandleBiometric()
  {
    var status = new StatusMessage { Message = "Connecting to Bitwarden Desktop...", State = MessageState.Info };
    ExtensionHost.ShowStatus(status, StatusContext.Page);

    bool success;
    string? error;
    try
    {
#pragma warning disable VSTHRD002
      var result = Task.Run(() => _service.VerifyWithBiometricsAsync(
        onStatus: msg => status.Message = msg)).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
      success = result.Success;
      error = result.Error;
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Reprompt", $"Biometric exception: {ex.GetType().Name}: {ex.Message}");
      success = false;
      error = ex.Message;
    }
    finally
    {
      try { ExtensionHost.HideStatus(status); } catch { }
    }

    if (!success)
    {
      RepromptPage.RecordFailure();
      var nextCooldown = RepromptPage.GetCooldownSecondsRemaining();
      _state = AuthState.Failed;
      _errorText = nextCooldown > 0
        ? $"Too many failed attempts. Try again in {nextCooldown}s."
        : error ?? "Biometric verification failed";
      TemplateJson = BuildTemplate();
      return CommandResult.KeepOpen();
    }

    return RunInnerAction();
  }

  private CommandResult HandleContinueAfterAuth()
  {
    if (_state != AuthState.Verified)
      return CommandResult.KeepOpen();
    return RunInnerAction();
  }

  private CommandResult RunInnerAction()
  {
    RepromptPage.RecordVerification(_itemId);
    try { _innerAction(); }
    catch (Exception ex)
    {
      DebugLogService.Log("Reprompt", $"Inner action exception: {ex.GetType().Name}: {ex.Message}");
      _state = AuthState.Failed;
      _errorText = "Action failed after verification.";
      TemplateJson = BuildTemplate();
      return CommandResult.KeepOpen();
    }

    return CommandResult.ShowToast($"Copied {_actionLabel} to clipboard");
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
      _state = AuthState.Failed;
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
      _state = AuthState.Failed;
      _errorText = nextCooldown > 0
        ? $"Too many failed attempts. Try again in {nextCooldown}s."
        : "Incorrect master password. Please try again.";
      TemplateJson = BuildTemplate();
      return CommandResult.KeepOpen();
    }

    return RunInnerAction();
  }
}
