using System;
using System.Collections.Generic;

namespace HoobiBitwarden.Ipc;

internal static class ServerUrlRules
{
  public const string ExampleBaseUrl = "https://vault.example.com";

  public static readonly IReadOnlyDictionary<string, string> AdvancedPaths = new Dictionary<string, string>
  {
    [SettingKeys.ServerWebVaultUrl] = "/",
    [SettingKeys.ServerApiUrl] = "/api",
    [SettingKeys.ServerIdentityUrl] = "/identity",
    [SettingKeys.ServerIconsUrl] = "/icons",
    [SettingKeys.ServerNotificationsUrl] = "/notifications",
    [SettingKeys.ServerEventsUrl] = "/events",
    [SettingKeys.ServerKeyConnectorUrl] = "/key-connector",
  };

  public static bool IsValidBaseUrl(string? baseUrl)
  {
    var trimmed = baseUrl?.Trim() ?? string.Empty;
    var hasScheme = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    return hasScheme && Uri.TryCreate(trimmed, UriKind.Absolute, out _);
  }

  public static string Derive(string baseUrl, string path) => baseUrl.Trim().TrimEnd('/') + path;

  public static string Placeholder(string? baseUrl, string path) => Derive(IsValidBaseUrl(baseUrl) ? baseUrl! : ExampleBaseUrl, path);
}
