using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using OtpNet;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Commands;

internal sealed partial class CopyOtpCommand : InvokableCommand
{
  private readonly ProtectedSecret _totpSecret;

  public CopyOtpCommand(ProtectedSecret totpSecret)
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

  internal static void CopyToClipboard(ProtectedSecret totpSecret)
  {
    var (code, _) = ComputeCode(totpSecret);
    SecureClipboardService.CopySensitive(code);
  }

  internal static (string Code, int RemainingSeconds) ComputeCode(ProtectedSecret totpSecret) =>
    totpSecret.Use(static secret => ComputeCode(secret, DateTimeOffset.UtcNow));

  internal static (string Code, int RemainingSeconds) ComputeCode(ReadOnlySpan<char> totpSecret) =>
    ComputeCode(totpSecret, DateTimeOffset.UtcNow);

  internal static (string Code, int RemainingSeconds) ComputeCode(ReadOnlySpan<char> totpSecret, DateTimeOffset at)
  {
    var steam = IsSteamSecret(totpSecret);
    var (key, digits, period, mode) = steam
      ? (DecodeBase32(totpSecret[8..]), 5, 30, OtpHashMode.Sha1)
      : ParseTotpSecret(totpSecret);
    try
    {
      var now = at.ToUnixTimeSeconds();
      var code = steam ? ComputeSteamCode(key, now) : FormatDigits(ComputeHotp(key, now / period, mode), digits);
      return (code, (int)(period - now % period));
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
    }
  }

  internal static bool IsSteamSecret(ReadOnlySpan<char> secret) =>
    secret.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);

  private const string SteamChars = "23456789BCDFGHJKMNPQRTVWXY";

  private static string FormatDigits(int code, int digits) =>
    (code % (long)Math.Pow(10, digits)).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0');

  private static int ComputeHotp(ReadOnlySpan<byte> key, long counter, OtpHashMode mode)
  {
    Span<byte> counterBytes = stackalloc byte[sizeof(long)];
    BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
    Span<byte> hash = stackalloc byte[HMACSHA512.HashSizeInBytes];
#pragma warning disable CA5350 // HMAC-SHA1 is required by the TOTP specification
    var length = mode switch
    {
      OtpHashMode.Sha256 => HMACSHA256.HashData(key, counterBytes, hash),
      OtpHashMode.Sha512 => HMACSHA512.HashData(key, counterBytes, hash),
      _ => HMACSHA1.HashData(key, counterBytes, hash),
    };
#pragma warning restore CA5350
    var offset = hash[length - 1] & 0x0F;
    return (hash[offset] & 0x7F) << 24
         | hash[offset + 1] << 16
         | hash[offset + 2] << 8
         | hash[offset + 3];
  }

  internal static string ComputeSteamCode(byte[] key, long unixSeconds)
  {
    var code = ComputeHotp(key, unixSeconds / 30, OtpHashMode.Sha1);

    return string.Create(5, code, static (span, val) =>
    {
      for (var i = 0; i < 5; i++)
      {
        span[i] = SteamChars[val % SteamChars.Length];
        val /= SteamChars.Length;
      }
    });
  }

  internal static (byte[] Key, int Digits, int Period, OtpHashMode Mode) ParseTotpSecret(ReadOnlySpan<char> secret)
  {
    if (!secret.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
      return (DecodeBase32(secret), 6, 30, OtpHashMode.Sha1);

    var query = secret[(secret.IndexOf('?') + 1)..];
    var fragment = query.IndexOf('#');
    if (fragment >= 0)
      query = query[..fragment];

    var rawSecret = secret;
    string? digitsText = null, periodText = null, algorithm = null;
    foreach (var range in query.Split('&'))
    {
      var pair = query[range];
      var separator = pair.IndexOf('=');
      if (separator < 0)
        continue;
      var value = pair[(separator + 1)..];
      switch (Uri.UnescapeDataString(pair[..separator]).ToUpperInvariant())
      {
        case "SECRET": rawSecret = value; break;
        case "DIGITS": digitsText = Uri.UnescapeDataString(value); break;
        case "PERIOD": periodText = Uri.UnescapeDataString(value); break;
        case "ALGORITHM": algorithm = Uri.UnescapeDataString(value).ToUpperInvariant(); break;
      }
    }

    _ = int.TryParse(digitsText, System.Globalization.CultureInfo.InvariantCulture, out var digits);
    _ = int.TryParse(periodText, System.Globalization.CultureInfo.InvariantCulture, out var period);
    var mode = algorithm switch
    {
      "SHA256" => OtpHashMode.Sha256,
      "SHA512" => OtpHashMode.Sha512,
      _ => OtpHashMode.Sha1,
    };

    return (DecodeEscapedBase32(rawSecret), digits > 0 ? digits : 6, period > 0 ? period : 30, mode);
  }

  private static byte[] DecodeEscapedBase32(ReadOnlySpan<char> value)
  {
    if (!value.Contains('%'))
      return DecodeBase32(value);

    var unescaped = GC.AllocateUninitializedArray<char>(value.Length, pinned: true);
    try
    {
      _ = Uri.TryUnescapeDataString(value, unescaped, out var written);
      return DecodeBase32(unescaped.AsSpan(0, written));
    }
    finally
    {
      CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(unescaped.AsSpan()));
    }
  }

  private static byte[] DecodeBase32(ReadOnlySpan<char> text)
  {
    var symbols = 0;
    foreach (var c in text)
    {
      if (c is not (' ' or '-' or '='))
        symbols++;
    }

    var key = new byte[symbols * 5 / 8];
    int buffer = 0, bits = 0, count = 0;
    foreach (var c in text)
    {
      if (c is ' ' or '-' or '=')
        continue;
      var value = c switch
      {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a',
        >= '2' and <= '7' => c - '2' + 26,
        _ => -1,
      };
      if (value < 0)
      {
        CryptographicOperations.ZeroMemory(key);
        throw new ArgumentException("Invalid Base32 character in TOTP secret", nameof(text));
      }
      buffer = ((buffer << 5) | value) & 0xFFF;
      bits += 5;
      if (bits >= 8)
      {
        bits -= 8;
        key[count++] = (byte)(buffer >> bits);
      }
    }
    return key;
  }
}
