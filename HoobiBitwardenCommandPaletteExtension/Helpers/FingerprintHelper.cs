using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace HoobiBitwardenCommandPaletteExtension.Helpers;

internal static class FingerprintHelper
{
    internal static string ComputeFingerprint(string appId, byte[] publicKeyDer)
    {
        var userFingerprint = HKDF.Expand(HashAlgorithmName.SHA256, SHA256.HashData(publicKeyDer), SHA256.HashSizeInBytes, Encoding.UTF8.GetBytes(appId));
        var n = new BigInteger(userFingerprint, isUnsigned: true, isBigEndian: true);

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
