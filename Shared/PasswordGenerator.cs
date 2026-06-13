using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using HoobiBitwardenCommandPaletteExtension.Helpers;

// Local password/passphrase generator using a CSPRNG (RandomNumberGenerator). Generation is pure
// local math, so there's no reason to shell out to `bw generate` (≈1s of Node startup per call).
// Mirrors Bitwarden's policy (character classes, minimum counts, avoid-ambiguous; passphrase words,
// separator, capitalize, include-number). Linked into both projects via Shared.
namespace HoobiBitwardenCompanionIpc;

internal static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    private const string Special = "!@#$%^&*";
    // Characters Bitwarden drops when "avoid ambiguous" is on.
    private const string AmbiguousChars = "Il1O0";

    public static string Generate(GeneratorOptions o) =>
        o.Mode == GeneratorMode.Passphrase ? GeneratePassphrase(o) : GeneratePassword(o);

    private static string GeneratePassword(GeneratorOptions o)
    {
        string Filter(string set) => o.AvoidAmbiguous ? Without(set, AmbiguousChars) : set;

        var upper = o.Uppercase ? Filter(Upper) : string.Empty;
        var lower = o.Lowercase ? Filter(Lower) : string.Empty;
        var digits = o.Numbers ? Filter(Digits) : string.Empty;
        var special = o.Symbols ? Special : string.Empty;
        if (upper.Length == 0 && lower.Length == 0 && digits.Length == 0 && special.Length == 0)
            lower = Filter(Lower); // never an empty character set

        var length = Math.Clamp(o.Length, GeneratorOptions.MinLength, GeneratorOptions.MaxLength);

        var minNumber = digits.Length > 0 ? Math.Max(0, o.MinNumber) : 0;
        var minSpecial = special.Length > 0 ? Math.Max(0, o.MinSpecial) : 0;
        // Don't require more mandatory characters than the total length.
        if (minNumber + minSpecial > length)
        {
            minSpecial = Math.Min(minSpecial, length);
            minNumber = Math.Min(minNumber, length - minSpecial);
        }

        var all = upper + lower + digits + special;
        var chars = new List<char>(length);
        for (var i = 0; i < minNumber; i++) chars.Add(Pick(digits));
        for (var i = 0; i < minSpecial; i++) chars.Add(Pick(special));
        while (chars.Count < length) chars.Add(Pick(all));

        Shuffle(chars);
        return new string(chars.ToArray());
    }

    private static string GeneratePassphrase(GeneratorOptions o)
    {
        var count = Math.Clamp(o.Words, GeneratorOptions.MinWords, GeneratorOptions.MaxWords);
        var separator = string.IsNullOrEmpty(o.Separator) ? "-" : o.Separator;
        var words = new string[count];
        for (var i = 0; i < count; i++)
        {
            var word = EFFWordList.Words[RandomNumberGenerator.GetInt32(EFFWordList.Words.Length)];
            words[i] = o.Capitalize ? char.ToUpperInvariant(word[0]) + word[1..] : word;
        }

        if (o.IncludeNumber)
        {
            var index = RandomNumberGenerator.GetInt32(count);
            words[index] += RandomNumberGenerator.GetInt32(10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return string.Join(separator, words);
    }

    private static string Without(string set, string remove)
    {
        var sb = new System.Text.StringBuilder(set.Length);
        foreach (var c in set)
            if (!remove.Contains(c, StringComparison.Ordinal)) sb.Append(c);
        return sb.ToString();
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];

    private static void Shuffle(List<char> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
