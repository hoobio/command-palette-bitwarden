using System;
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
    var (key, digits, period) = ParseTotpSecret(totpSecret);
    var totp = new Totp(key, step: period, totpSize: digits);
    SecureClipboardService.CopySensitive(totp.ComputeTotp());
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
