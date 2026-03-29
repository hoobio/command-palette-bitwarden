using System;
using System.Security.Cryptography;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using OtpNet;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Commands;

internal sealed partial class CopyOtpCommand : InvokableCommand
{
  private readonly string _totpSecret;

  public CopyOtpCommand(string totpSecret)
  {
    _totpSecret = totpSecret;
    Name = "Copy OTP";
    Icon = new IconInfo("\uEC92");
  }

  public override ICommandResult Invoke()
  {
    try
    {
      CopyToClipboard(_totpSecret);
      return CommandResult.ShowToast("Copied TOTP to clipboard");
    }
    catch
    {
      return CommandResult.ShowToast("Failed to compute OTP");
    }
  }

  internal static void CopyToClipboard(string totpSecret)
  {
    var (code, _) = ComputeCode(totpSecret);
    SecureClipboardService.CopySensitive(code);
  }

  internal static (string Code, int RemainingSeconds) ComputeCode(string totpSecret)
  {
    if (IsSteamSecret(totpSecret))
    {
      var key = ParseSteamKey(totpSecret);
      var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      var code = ComputeSteamCode(key, now);
      var remaining = (int)(30 - now % 30);
      return (code, remaining);
    }

    var (keyBytes, digits, period) = ParseTotpSecret(totpSecret);
    var totp = new Totp(keyBytes, step: period, totpSize: digits);
    return (totp.ComputeTotp(), totp.RemainingSeconds());
  }

  internal static bool IsSteamSecret(string secret) =>
    secret.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);

  private static byte[] ParseSteamKey(string secret) =>
    Base32Encoding.ToBytes(secret[8..].Replace(" ", "").Replace("-", ""));

  private const string SteamChars = "23456789BCDFGHJKMNPQRTVWXY";

  internal static string ComputeSteamCode(byte[] key, long unixSeconds)
  {
    var timeStep = unixSeconds / 30;
    var timeBytes = BitConverter.GetBytes(timeStep);
    if (BitConverter.IsLittleEndian)
      Array.Reverse(timeBytes);

#pragma warning disable CA5350 // HMAC-SHA1 is required by the TOTP specification
    var hash = HMACSHA1.HashData(key, timeBytes);
#pragma warning restore CA5350
    var offset = hash[^1] & 0x0F;
    var code = (hash[offset] & 0x7F) << 24
             | hash[offset + 1] << 16
             | hash[offset + 2] << 8
             | hash[offset + 3];

    return string.Create(5, code, static (span, val) =>
    {
      for (var i = 0; i < 5; i++)
      {
        span[i] = SteamChars[val % SteamChars.Length];
        val /= SteamChars.Length;
      }
    });
  }

  internal static (byte[] Key, int Digits, int Period) ParseTotpSecret(string secret)
  {
    if (secret.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
    {
      var uri = new Uri(secret);
      var query = uri.Query.TrimStart('?');
      var parameters = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
      {
        var parts = pair.Split('=', 2);
        if (parts.Length == 2)
          parameters[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
      }

      var rawSecret = parameters.TryGetValue("secret", out var s) ? s : secret;
      var key = Base32Encoding.ToBytes(rawSecret.Replace(" ", "").Replace("-", ""));
      _ = int.TryParse(parameters.TryGetValue("digits", out var d) ? d : null, out var digits);
      _ = int.TryParse(parameters.TryGetValue("period", out var p) ? p : null, out var period);

      return (key, digits > 0 ? digits : 6, period > 0 ? period : 30);
    }

    return (Base32Encoding.ToBytes(secret.Replace(" ", "").Replace("-", "")), 6, 30);
  }
}
