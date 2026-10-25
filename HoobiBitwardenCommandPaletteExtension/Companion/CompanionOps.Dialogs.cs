using System;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  internal Task PostUnlockRefresh { get; private set; } = Task.CompletedTask;

  internal async Task<UnlockResponse> UnlockAsync(string? password, bool rememberSession, bool biometric)
  {
    if (!biometric && string.IsNullOrEmpty(password))
      return new UnlockResponse(false, "Enter your master password");

    if (_service.Settings is { } settings && settings.RememberSession.Value != rememberSession)
    {
      settings.RememberSession.Value = rememberSession;
      settings.SaveSettings();
    }

    var (success, error) = biometric ? await _service.UnlockWithBiometricsAsync() : await _service.UnlockAsync(password!);
    if (success)
    {
      PostUnlockRefresh = RefreshAfterUnlockAsync();
      return new UnlockResponse(true, null);
    }

    if (_service.LastStatus == VaultStatus.Unauthenticated)
      return new UnlockResponse(false, "You are not logged in", LoggedOut: true);
    if (biometric)
      return new UnlockResponse(false, error ?? "Windows Hello unlock failed");
    return new UnlockResponse(false, error?.Contains("key", StringComparison.OrdinalIgnoreCase) == true ? "Invalid password entered" : error ?? "Unlock failed");
  }

  internal async Task<SetServerResponse> SetServerAsync(SetServerRequest request)
  {
    if (!TryBuildServerConfig(request, out var config, out var invalid))
      return new SetServerResponse(false, invalid);

    if (_service.LastStatus is VaultStatus.Locked or VaultStatus.Unlocked)
      await _service.LogoutAsync();

    var error = await _service.SetServerUrlAsync(config);
    return new SetServerResponse(error is null, error);
  }

  private async Task RefreshAfterUnlockAsync()
  {
    try
    {
      var refresh = _service.RefreshCacheAsync();
      _service.NotifyStatusChanged();
      await refresh;
    }
    catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
    {
      DebugLogService.Log("Companion", $"Refresh after unlock failed: {ex.GetType().Name}");
    }
  }

  private static bool TryBuildServerConfig(SetServerRequest request, out ServerConfig config, out string? error)
  {
    config = new ServerConfig("bitwarden.com");
    error = null;
    switch (request.Region)
    {
      case ServerRegions.Cloud:
        return true;
      case ServerRegions.Eu:
        config = new ServerConfig("https://vault.bitwarden.eu");
        return true;
      case ServerRegions.SelfHosted:
        break;
      default:
        throw new ArgumentException("Unknown region", nameof(request));
    }

    var baseUrl = request.BaseUrl?.Trim() ?? string.Empty;
    var hasScheme = baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    if (!hasScheme || !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
    {
      error = "Enter a valid server URL starting with http:// or https://";
      return false;
    }

    var webVault = OptionalHttpsUrl(nameof(request.WebVaultUrl), request.WebVaultUrl, ref error);
    var api = OptionalHttpsUrl(nameof(request.ApiUrl), request.ApiUrl, ref error);
    var identity = OptionalHttpsUrl(nameof(request.IdentityUrl), request.IdentityUrl, ref error);
    var icons = OptionalHttpsUrl(nameof(request.IconsUrl), request.IconsUrl, ref error);
    var notifications = OptionalHttpsUrl(nameof(request.NotificationsUrl), request.NotificationsUrl, ref error);
    var events = OptionalHttpsUrl(nameof(request.EventsUrl), request.EventsUrl, ref error);
    var keyConnector = OptionalHttpsUrl(nameof(request.KeyConnectorUrl), request.KeyConnectorUrl, ref error);
    if (error is not null)
      return false;

    config = new ServerConfig(baseUrl, webVault, api, identity, icons, notifications, events, keyConnector);
    return true;
  }

  private static string? OptionalHttpsUrl(string key, string? value, ref string? error)
  {
    var trimmed = value?.Trim();
    if (error is not null || string.IsNullOrEmpty(trimmed))
      return null;
    if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme == "https")
      return trimmed;
    error = $"Invalid {key}: must start with https://";
    return null;
  }
}
