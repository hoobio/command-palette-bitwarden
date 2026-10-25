using System;
using System.Globalization;
using System.Linq;

namespace HoobiBitwarden.Ipc;

internal static class TwoFactorRules
{
  public const int Authenticator = 0;
  public const int Email = 1;
  public const int YubiKey = 3;

  public static (string Placeholder, string TypedNoun, string EmptyHint, Func<string, bool> Validator) Describe(int? method) => method switch
  {
    Authenticator => ("Enter your authenticator code...", "authenticator code", "Type the 6-digit code from your authenticator app", IsNumericCode),
    Email => ("Enter the code sent to your email...", "email code", "Check your inbox for the code Bitwarden just sent", IsNumericCode),
    YubiKey => ("Touch your YubiKey...", "YubiKey OTP", "Touch your YubiKey to insert its one-time code", IsYubiKeyOtp),
    _ => ("Enter your 2FA code...", "2FA code", "Type your 6-8 digit code above and press Enter", IsNumericCode),
  };

  public static bool IsNumericCode(string code) =>
    code.Length >= 6 && code.Length <= 8 && long.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out _);

  public static bool IsYubiKeyOtp(string code) =>
    code.Length >= 32 && code.Length <= 64 && code.All(char.IsLetterOrDigit);
}
