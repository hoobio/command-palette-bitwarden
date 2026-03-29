using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace HoobiBitwardenCommandPaletteExtension.Helpers;

internal static class FingerprintHelper
{
    internal static string ComputeFingerprint(string appId, byte[] publicKeyDer)
    {
        var keyHash = SHA256.HashData(publicKeyDer);

        // HKDF-Expand(keyHash, appId, 32): single iteration since output == hash length
        using var hmac = new HMACSHA256(keyHash);
        var infoBytes = Encoding.UTF8.GetBytes(appId);
        var input = new byte[infoBytes.Length + 1];
        Buffer.BlockCopy(infoBytes, 0, input, 0, infoBytes.Length);
        input[^1] = 1;
        var userFingerprint = hmac.ComputeHash(input);

        // Convert to unsigned big-endian BigInteger
        var buf = new byte[userFingerprint.Length + 1]; // +1 byte ensures unsigned
        for (var i = 0; i < userFingerprint.Length; i++)
            buf[userFingerprint.Length - 1 - i] = userFingerprint[i];
        var n = new BigInteger(buf);

        var wordListLen = new BigInteger(EFFWordList.Words.Length);
        var words = new string[5];
        for (var i = 0; i < 5; i++)
        {
            n = BigInteger.DivRem(n, wordListLen, out var remainder);
            words[i] = EFFWordList.Words[(int)remainder];
        }

        return string.Join("-", words);
    }
}
