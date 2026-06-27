using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;

namespace HoobiBitwardenCompanionIpc;

// Dependency-free TOTP (RFC 6238) and Steam Guard code calculation, shared by the extension (palette
// tags, copy-OTP) and the companion (live code + countdown in the item window). HMAC-SHA1 + an inline
// RFC 4648 Base32 decoder only, so the companion needs no extra NuGet package. Linked into both
// projects via <Compile Include .. Link>.
internal static class TotpCalculator
{
    // Current code plus seconds until it rolls over and the configured period. Handles otpauth://
    // URIs, steam:// secrets, and bare Base32 seeds.
    public static (string Code, int RemainingSeconds, int Period) ComputeCode(string totpSecret)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (IsSteamSecret(totpSecret))
        {
            var steamKey = DecodeBase32(Strip(totpSecret[8..]));
            return (ComputeSteamCode(steamKey, now), (int)(30 - now % 30), 30);
        }

        var (key, digits, period) = ParseTotpSecret(totpSecret);
        return (ComputeHotp(key, now / period, digits), (int)(period - now % period), period);
    }

    public static bool IsSteamSecret(string secret) =>
        secret.StartsWith("steam://", StringComparison.OrdinalIgnoreCase);

    public static (byte[] Key, int Digits, int Period) ParseTotpSecret(string secret)
    {
        if (secret.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(secret);
            var query = uri.Query.TrimStart('?');
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2)
                    parameters[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }

            var rawSecret = parameters.TryGetValue("secret", out var s) ? s : secret;
            var key = DecodeBase32(Strip(rawSecret));
            _ = int.TryParse(parameters.TryGetValue("digits", out var d) ? d : null, out var digits);
            _ = int.TryParse(parameters.TryGetValue("period", out var p) ? p : null, out var period);
            return (key, digits > 0 ? digits : 6, period > 0 ? period : 30);
        }

        return (DecodeBase32(Strip(secret)), 6, 30);
    }

    public static string ComputeHotp(byte[] key, long counter, int digits)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

#pragma warning disable CA5350 // HMAC-SHA1 is required by the TOTP/HOTP specification
        var hash = HMACSHA1.HashData(key, counterBytes);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = (hash[offset] & 0x7F) << 24
                   | hash[offset + 1] << 16
                   | hash[offset + 2] << 8
                   | hash[offset + 3];
        var otp = binary % (int)Math.Pow(10, digits);
        return otp.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    private const string SteamChars = "23456789BCDFGHJKMNPQRTVWXY";

    public static string ComputeSteamCode(byte[] key, long unixSeconds)
    {
        var timeStep = unixSeconds / 30;
        var timeBytes = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian) Array.Reverse(timeBytes);

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

    private static string Strip(string s) => s.Replace(" ", "").Replace("-", "");

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    // RFC 4648 Base32 decode, ignoring padding and any non-alphabet characters.
    public static byte[] DecodeBase32(string input)
    {
        input = input.TrimEnd('=').ToUpperInvariant();
        if (input.Length == 0) return [];

        var output = new List<byte>(input.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;
        foreach (var c in input)
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0) continue;
            buffer = (buffer << 5) | value;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return [.. output];
    }
}
