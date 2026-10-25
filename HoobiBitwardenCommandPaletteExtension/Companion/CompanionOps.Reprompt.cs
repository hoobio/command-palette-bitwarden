using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  private sealed record PendingReprompt(string ItemId, Action Action, DateTime ExpiresUtc);

  private readonly ConcurrentDictionary<string, PendingReprompt> _pendingReprompts = new();

  internal TimeSpan RepromptLifetime { get; set; } = TimeSpan.FromMinutes(5);

  internal int PendingRepromptCount => _pendingReprompts.Count;

  internal RepromptDialogRequest RegisterReprompt(string itemId, Action action, long returnWindow)
  {
    var now = DateTime.UtcNow;
    foreach (var expired in _pendingReprompts.Where(p => p.Value.ExpiresUtc <= now).Select(p => p.Key))
      _pendingReprompts.TryRemove(expired, out _);

    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    _pendingReprompts[token] = new PendingReprompt(itemId, action, now + RepromptLifetime);
    return new RepromptDialogRequest(returnWindow, token, _service.GetCachedItem(itemId)?.Name ?? string.Empty, _biometricEnabled());
  }

  internal void CancelReprompt(string? token)
  {
    if (token is not null)
      _pendingReprompts.TryRemove(token, out _);
  }

  internal async Task<RepromptConfirmResponse> ConfirmRepromptAsync(string? token, string? password, bool biometric)
  {
    if (token is null || !_pendingReprompts.TryGetValue(token, out var pending) || pending.ExpiresUtc <= DateTime.UtcNow)
    {
      CancelReprompt(token);
      return new RepromptConfirmResponse(false, "This request expired. Close this window and try again.");
    }

    if (!biometric && string.IsNullOrEmpty(password))
      return new RepromptConfirmResponse(false, "Enter your master password");

    var verification = await VerifyRepromptAsync(pending.ItemId, password, biometric: biometric);
    if (!verification.Verified)
    {
      return new RepromptConfirmResponse(false, verification.CooldownSeconds > 0
        ? $"Too many failed attempts. Try again in {verification.CooldownSeconds}s."
        : biometric ? "Windows Hello verification failed" : "Incorrect master password. Please try again.");
    }

    if (!_pendingReprompts.TryRemove(token, out _))
      return new RepromptConfirmResponse(false, "This request expired. Close this window and try again.");

    try
    {
      pending.Action();
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Reprompt", $"Inner action exception: {ex.GetType().Name}: {ex.Message}");
      _pendingReprompts[token] = pending;
      return new RepromptConfirmResponse(false, "Action failed after verification.");
    }

    return new RepromptConfirmResponse(true, null);
  }
}
