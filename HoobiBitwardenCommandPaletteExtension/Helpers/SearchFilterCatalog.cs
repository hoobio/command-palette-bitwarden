using System;
using System.Collections.Generic;
using System.Linq;

namespace HoobiBitwardenCommandPaletteExtension.Helpers;

internal sealed record SearchFilterEntry(string Id, string Name, string Glyph, string Section, (string Key, string Value)? Filter);

internal static class SearchFilterCatalog
{
  internal const string AllId = "all";
  private const string AllSection = "";
  private const string FilterSection = "Filters";
  private const string VaultSection = "Vaults";
  private const int MaxNameLength = 24;

  internal static readonly SearchFilterEntry All = new(AllId, "All items", "", AllSection, null);

  internal static readonly SearchFilterEntry[] Static =
  [
    Fixed("is", "fav", "Favourites", ""),
    Fixed("is", "protected", "Protected", ""),
    Fixed("is", "weak", "Weak passwords", ""),
    Fixed("is", "old", "Old passwords", ""),
    Fixed("is", "insecure", "Insecure URLs", ""),
    Fixed("is", "watchtower", "Watchtower", ""),
    Fixed("has", "totp", "Has TOTP", ""),
    Fixed("has", "passkey", "Has passkey", ""),
    Fixed("has", "password", "Has password", ""),
    Fixed("has", "url", "Has URL", ""),
    Fixed("has", "notes", "Has notes", ""),
    Fixed("has", "folder", "In a folder", ""),
    Fixed("type", "login", "Logins", ""),
    Fixed("type", "card", "Cards", ""),
    Fixed("type", "identity", "Identities", ""),
    Fixed("type", "securenote", "Secure notes", ""),
    Fixed("type", "sshkey", "SSH keys", ""),
  ];

  internal static List<SearchFilterEntry> Build(IReadOnlyDictionary<string, string> organisations)
  {
    var entries = new List<SearchFilterEntry> { All };
    entries.AddRange(Static);
    entries.Add(new("org:none", "My vault", "", VaultSection, ("org", "none")));
    entries.AddRange(organisations.OrderBy(o => o.Value, StringComparer.OrdinalIgnoreCase)
      .Select(o => new SearchFilterEntry($"org:{o.Key}", Truncate(o.Value), "", VaultSection, ("org", o.Key))));
    return entries;
  }

  internal static string Truncate(string name) => name.Length <= MaxNameLength ? name : string.Concat(name.AsSpan(0, MaxNameLength - 1).TrimEnd(), "…");

  internal static SearchFilterEntry Resolve(IReadOnlyList<SearchFilterEntry> entries, string? id)
    => entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal)) ?? All;

  private static SearchFilterEntry Fixed(string key, string value, string name, string glyph)
    => new($"{key}:{value}", name, glyph, FilterSection, (key, value));
}
