using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class PasswordGenerator
{
  internal const string SeparatorAllowlist = "-_.,:;+=#!@$~";
  internal const string Special = "!@#$%^&*";

  private static readonly Lazy<string[]> LazyWordList = new(LoadWordList);
  private static readonly Lazy<string[]> LazyPassphraseWords = new(() => [.. WordList.Where(w => !w.Contains('-', StringComparison.Ordinal))]);

  internal static string[] WordList => LazyWordList.Value;

  internal static string[] PassphraseWords => LazyPassphraseWords.Value;

  internal static string Lowercase(bool avoidAmbiguous) => avoidAmbiguous ? "abcdefghijkmnopqrstuvwxyz" : "abcdefghijkmnopqrstuvwxyzl";

  internal static string Uppercase(bool avoidAmbiguous) => avoidAmbiguous ? "ABCDEFGHJKLMNPQRSTUVWXYZ" : "ABCDEFGHJKLMNPQRSTUVWXYZIO";

  internal static string Numbers(bool avoidAmbiguous) => avoidAmbiguous ? "23456789" : "2345678901";

  internal static void Validate(GeneratorOptions options)
  {
    var valid = options.Kind switch
    {
      GeneratorKinds.Passphrase => options.Words is >= 3 and <= 20
        && (options.Separator is "" or " " || (options.Separator is [var c] && SeparatorAllowlist.Contains(c))),
      GeneratorKinds.Password => options.Length is >= 5 and <= 128 && options.MinNumber is >= 0 and <= 9 && options.MinSpecial is >= 0 and <= 9
        && (options.Uppercase || options.Lowercase || options.Numbers || options.Special),
      _ => false,
    };
    if (!valid)
      throw new IpcException(IpcErrors.Invalid);
  }

  internal static string Generate(GeneratorOptions options)
  {
    Validate(options);
    return options.Kind == GeneratorKinds.Passphrase ? Passphrase(options) : Password(options);
  }

  private static string Password(GeneratorOptions options)
  {
    var lower = options.Lowercase ? Lowercase(options.AvoidAmbiguous) : string.Empty;
    var upper = options.Uppercase ? Uppercase(options.AvoidAmbiguous) : string.Empty;
    var number = options.Numbers ? Numbers(options.AvoidAmbiguous) : string.Empty;
    var special = options.Special ? Special : string.Empty;
    var any = upper + lower + number + special;
    var required = Enumerable.Repeat(number, options.Numbers ? options.MinNumber : 0)
      .Concat(Enumerable.Repeat(special, options.Special ? options.MinSpecial : 0))
      .Concat(Enumerable.Repeat(upper, options.Uppercase ? 1 : 0))
      .Concat(Enumerable.Repeat(lower, options.Lowercase ? 1 : 0))
      .ToList();
    var positions = required.Concat(Enumerable.Repeat(any, Math.Max(options.Length - required.Count, 0))).ToArray();
    RandomNumberGenerator.Shuffle(positions.AsSpan());
    return string.Concat(positions.Select(set => set[RandomNumberGenerator.GetInt32(set.Length)]));
  }

  private static string Passphrase(GeneratorOptions options)
  {
    var pool = PassphraseWords;
    var words = Enumerable.Range(0, options.Words).Select(_ => pool[RandomNumberGenerator.GetInt32(pool.Length)]).ToArray();
    if (options.Capitalize)
      words = [.. words.Select(w => char.ToUpperInvariant(w[0]) + w[1..])];
    if (options.IncludeNumber)
      words[RandomNumberGenerator.GetInt32(words.Length)] += (char)('0' + RandomNumberGenerator.GetInt32(10));
    return string.Join(options.Separator, words);
  }

  private static string[] LoadWordList()
  {
    using var stream = typeof(PasswordGenerator).Assembly.GetManifestResourceStream("eff_large_wordlist.txt")
      ?? throw new InvalidOperationException("The EFF word list resource is missing.");
    using var reader = new StreamReader(stream);
    return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line[(line.IndexOf('\t', StringComparison.Ordinal) + 1)..])];
  }
}
