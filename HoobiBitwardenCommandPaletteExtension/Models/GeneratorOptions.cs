using System;
using System.Collections.Generic;
using System.Globalization;
using HoobiBitwardenCompanionIpc;

namespace HoobiBitwardenCommandPaletteExtension.Models;

// Extension-only projection of the shared GeneratorOptions (Shared/GeneratorOptions.cs): build the
// `bw generate` argument tokens. Returned as discrete tokens so the caller quotes them for the
// command line. At least one character class is forced on so the CLI never rejects the request.
internal static class GeneratorOptionsCliExtensions
{
  public static IReadOnlyList<string> ToCliArgs(this GeneratorOptions o)
  {
    var args = new List<string> { "generate" };

    if (o.Mode == GeneratorMode.Passphrase)
    {
      args.Add("--passphrase");
      args.Add("--words");
      args.Add(Math.Clamp(o.Words, GeneratorOptions.MinWords, GeneratorOptions.MaxWords).ToString(CultureInfo.InvariantCulture));
      args.Add("--separator");
      args.Add(string.IsNullOrEmpty(o.Separator) ? "-" : o.Separator);
      if (o.Capitalize) args.Add("--capitalize");
      if (o.IncludeNumber) args.Add("--includeNumber");
      return args;
    }

    var upper = o.Uppercase;
    var lower = o.Lowercase;
    var number = o.Numbers;
    var special = o.Symbols;
    if (!upper && !lower && !number && !special)
      lower = true; // never produce an empty character set

    args.Add("--length");
    args.Add(Math.Clamp(o.Length, GeneratorOptions.MinLength, GeneratorOptions.MaxLength).ToString(CultureInfo.InvariantCulture));
    if (upper) args.Add("--uppercase");
    if (lower) args.Add("--lowercase");
    if (number) args.Add("--number");
    if (special) args.Add("--special");
    if (number && o.MinNumber > 0)
    {
      args.Add("--minNumber");
      args.Add(o.MinNumber.ToString(CultureInfo.InvariantCulture));
    }
    if (special && o.MinSpecial > 0)
    {
      args.Add("--minSpecial");
      args.Add(o.MinSpecial.ToString(CultureInfo.InvariantCulture));
    }
    // `--ambiguous` ALLOWS ambiguous characters; omit it to avoid them.
    if (!o.AvoidAmbiguous) args.Add("--ambiguous");

    return args;
  }
}
