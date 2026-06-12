using System;
using System.Collections.Generic;
using System.Globalization;

namespace HoobiBitwardenCommandPaletteExtension.Models;

internal enum GeneratorMode
{
  Password = 0,
  Passphrase = 1,
}

// Mirrors Bitwarden's own generator. Shaped so a future settings UI can source these
// (see GeneratorOptionsProvider, which today returns constants). Do not hardcode option
// values at call sites - go through the provider.
internal sealed record GeneratorOptions
{
  public GeneratorMode Mode { get; init; } = GeneratorMode.Password;

  // Password mode
  public int Length { get; init; } = 20;
  public bool Uppercase { get; init; } = true;
  public bool Lowercase { get; init; } = true;
  public bool Numbers { get; init; } = true;
  public bool Symbols { get; init; } = true;
  public int MinNumber { get; init; } = 1;
  public int MinSpecial { get; init; } = 1;
  public bool AvoidAmbiguous { get; init; } = true;

  // Passphrase mode
  public int Words { get; init; } = 6;
  public string Separator { get; init; } = "-";
  public bool Capitalize { get; init; }
  public bool IncludeNumber { get; init; } = true;

  public const int MinLength = 5;
  public const int MaxLength = 128;
  public const int MinWords = 3;
  public const int MaxWords = 20;

  // Builds the argument list for `bw generate`. Returned as discrete tokens so the caller
  // can quote them correctly for the process command line. At least one character class is
  // forced on so the CLI never rejects the request.
  public IReadOnlyList<string> ToCliArgs()
  {
    var args = new List<string> { "generate" };

    if (Mode == GeneratorMode.Passphrase)
    {
      args.Add("--passphrase");
      args.Add("--words");
      args.Add(Math.Clamp(Words, MinWords, MaxWords).ToString(CultureInfo.InvariantCulture));
      args.Add("--separator");
      args.Add(string.IsNullOrEmpty(Separator) ? "-" : Separator);
      if (Capitalize) args.Add("--capitalize");
      if (IncludeNumber) args.Add("--includeNumber");
      return args;
    }

    var upper = Uppercase;
    var lower = Lowercase;
    var number = Numbers;
    var special = Symbols;
    if (!upper && !lower && !number && !special)
      lower = true; // never produce an empty character set

    args.Add("--length");
    args.Add(Math.Clamp(Length, MinLength, MaxLength).ToString(CultureInfo.InvariantCulture));
    if (upper) args.Add("--uppercase");
    if (lower) args.Add("--lowercase");
    if (number) args.Add("--number");
    if (special) args.Add("--special");
    if (number && MinNumber > 0)
    {
      args.Add("--min-number");
      args.Add(MinNumber.ToString(CultureInfo.InvariantCulture));
    }
    if (special && MinSpecial > 0)
    {
      args.Add("--min-special");
      args.Add(MinSpecial.ToString(CultureInfo.InvariantCulture));
    }
    // `--ambiguous` ALLOWS ambiguous characters; omit it to avoid them.
    if (!AvoidAmbiguous) args.Add("--ambiguous");

    return args;
  }
}

// Single accessor for generator defaults. Returns secure constants today; a future settings
// page will make these configurable without touching call sites.
internal static class GeneratorOptionsProvider
{
  public static GeneratorOptions DefaultPassword() => new()
  {
    Mode = GeneratorMode.Password,
    Length = 20,
    Uppercase = true,
    Lowercase = true,
    Numbers = true,
    Symbols = true,
    MinNumber = 1,
    MinSpecial = 1,
    AvoidAmbiguous = true,
  };

  public static GeneratorOptions DefaultPassphrase() => new()
  {
    Mode = GeneratorMode.Passphrase,
    Words = 6,
    Separator = "-",
    Capitalize = false,
    IncludeNumber = true,
  };
}
