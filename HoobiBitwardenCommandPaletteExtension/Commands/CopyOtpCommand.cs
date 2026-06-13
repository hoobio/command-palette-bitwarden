using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using HoobiBitwardenCompanionIpc;
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

  // Thin delegators to the shared TotpCalculator so the extension and companion compute identical
  // codes. Kept on this type for the existing call sites and unit tests.
  internal static (string Code, int RemainingSeconds) ComputeCode(string totpSecret)
  {
    var (code, remaining, _) = TotpCalculator.ComputeCode(totpSecret);
    return (code, remaining);
  }

  internal static bool IsSteamSecret(string secret) => TotpCalculator.IsSteamSecret(secret);

  internal static string ComputeSteamCode(byte[] key, long unixSeconds) =>
    TotpCalculator.ComputeSteamCode(key, unixSeconds);

  internal static (byte[] Key, int Digits, int Period) ParseTotpSecret(string secret) =>
    TotpCalculator.ParseTotpSecret(secret);
}
