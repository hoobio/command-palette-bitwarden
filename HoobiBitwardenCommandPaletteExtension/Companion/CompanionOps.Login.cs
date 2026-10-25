using System;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  internal Task PostLoginSync { get; private set; } = Task.CompletedTask;

  internal LoginDialogRequest BuildLoginDialog(long returnWindow) =>
    new(returnWindow, _service.Settings?.RememberSession.Value == true, _service.DataDirectory);

  internal async Task<LoginResponse> LoginAsync(LoginRequest request)
  {
    var email = request.Email?.Trim();
    if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(request.Password))
      return new LoginResponse(false, "Enter your email and master password");

    var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim();
    ApplyRememberSession(request.RememberSession);

    var (success, error, twoFactorRequired, deviceVerificationRequired) = await _service.LoginAsync(email, request.Password, code, request.Method);
    if (success)
      return Succeeded();

    if (code is null)
    {
      if (twoFactorRequired || deviceVerificationRequired)
        return new LoginResponse(false, null, twoFactorRequired, deviceVerificationRequired);
      return new LoginResponse(false, error ?? "Login failed");
    }

    return new LoginResponse(false, error?.Contains("Code", StringComparison.OrdinalIgnoreCase) == true
      ? "Invalid 2FA code - try again"
      : error ?? "Verification failed");
  }

  internal async Task<LoginResponse> SubmitDeviceVerificationAsync(string? code)
  {
    var trimmed = code?.Trim() ?? string.Empty;
    if (!TwoFactorRules.IsNumericCode(trimmed))
      return new LoginResponse(false, "Enter the 6 to 8 digit code sent to your login email");

    var (success, error) = await _service.SubmitDeviceVerificationAsync(trimmed);
    return success ? Succeeded() : new LoginResponse(false, error ?? "Verification failed - try again");
  }

  internal void CancelLogin() => _service.DisposeDeviceVerificationProcess();

  private void ApplyRememberSession(bool rememberSession)
  {
    if (_service.Settings is { } settings && settings.RememberSession.Value != rememberSession)
    {
      settings.RememberSession.Value = rememberSession;
      settings.SaveSettings();
    }
  }

  private LoginResponse Succeeded()
  {
    PostLoginSync = SyncAfterLoginAsync();
    return new LoginResponse(true, null);
  }

  private async Task SyncAfterLoginAsync()
  {
    try
    {
      var sync = _service.SyncVaultAsync();
      _service.NotifyStatusChanged();
      await sync;
    }
    catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
    {
      DebugLogService.Log("Companion", $"Sync after login failed: {ex.GetType().Name}");
    }
  }
}
