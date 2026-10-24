using System;
using System.Collections.Generic;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Models;

internal enum BitwardenItemType
{
  Login = 1,
  SecureNote = 2,
  Card = 3,
  Identity = 4,
  SshKey = 5,
}

internal enum UriMatchType
{
  Default = -1, // null in JSON, behaves like Domain
  Domain = 0,
  Host = 1,
  StartsWith = 2,
  Exact = 3,
  RegularExpression = 4,
  Never = 5,
}

internal sealed record ItemUri(string Uri, UriMatchType Match)
{
  public string? Host { get; } = ExtractHost(Uri);

  internal static string? ExtractHost(string uri)
  {
    try
    {
      var raw = uri.Contains("://") ? uri : "https://" + uri;
      var host = new System.Uri(raw).Host;
      return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
    catch { return null; }
  }
}

internal sealed record CustomField(string? Value, ProtectedSecret? Secret = null)
{
  public bool IsHidden => Secret != null;
}

internal sealed record ItemAttachment(string Id, string FileName, long Size, string? SizeName);

internal sealed class BitwardenItem
{
  public string Id { get; init; } = string.Empty;
  public string Name { get; init; } = string.Empty;
  public BitwardenItemType Type { get; init; }
  public ProtectedSecret? Notes { get; init; }
  public DateTime RevisionDate { get; init; }
  public DateTime? CreationDate { get; init; }
  public bool Favorite { get; init; }
  public string? FolderId { get; init; }
  public string? OrganizationId { get; init; }
  public int Reprompt { get; init; }
  public IReadOnlyList<string> CollectionIds { get; init; } = [];
  public DateTime? DeletedDate { get; init; }
  public DateTime? ArchivedDate { get; init; }
  public IReadOnlyList<ItemAttachment> Attachments { get; init; } = Array.Empty<ItemAttachment>();

  public string? Username { get; init; }
  public ProtectedSecret? Password { get; init; }
  public bool HasWeakPassword => Password?.Length < 8;
  public bool HasTotp { get; init; }
  public ProtectedSecret? TotpSecret { get; init; }
  public bool HasPasskey { get; init; }
  public List<ItemUri> Uris { get; init; } = [];
  public string? FirstUri => Uris.Count > 0 ? Uris[0].Uri : null;
  public string? FirstHost => Uris.Count > 0 ? Uris[0].Host : null;
  public DateTime? PasswordRevisionDate { get; init; }

  public string? CardholderName { get; init; }
  public string? CardBrand { get; init; }
  public ProtectedSecret? CardNumber { get; init; }
  public string? CardExpMonth { get; init; }
  public string? CardExpYear { get; init; }
  public ProtectedSecret? CardCode { get; init; }
  public string? CardExpiration => CardExpMonth != null && CardExpYear != null
      ? $"{CardExpMonth} / {CardExpYear}" : null;
  public string? CardLast4 => CardNumber?.Length >= 4 ? CardNumber.UseOrDefault(static n => new string(n[^4..])) : null;

  public string? IdentityFullName { get; init; }
  public string? IdentityEmail { get; init; }
  public string? IdentityPhone { get; init; }
  public string? IdentityUsername { get; init; }
  public string? IdentityCompany { get; init; }
  public string? IdentityAddress { get; init; }

  public string? SshPublicKey { get; init; }
  public string? SshFingerprint { get; init; }
  public ProtectedSecret? SshPrivateKey { get; init; }

  public Dictionary<string, CustomField> CustomFields { get; init; } = [];
  public string? SshHost => CustomFields.TryGetValue("host", out var h) ? h.Value : null;
  internal string? RevealSshHost() => CustomFields.TryGetValue("host", out var h) ? h.Value ?? h.Secret?.UseOrDefault(static s => new string(s)) : null;
  internal bool IsSshHostHidden => CustomFields.TryGetValue("host", out var h) && h.IsHidden;

  internal void DisposeSecrets()
  {
    Notes?.Dispose();
    Password?.Dispose();
    TotpSecret?.Dispose();
    CardNumber?.Dispose();
    CardCode?.Dispose();
    SshPrivateKey?.Dispose();
    foreach (var field in CustomFields.Values)
      field.Secret?.Dispose();
  }

  public string Subtitle => Type switch
  {
    BitwardenItemType.Login => Username ?? string.Empty,
    BitwardenItemType.SecureNote when Reprompt == 1 => "Protected",
    BitwardenItemType.SecureNote => Notes?.UseOrDefault(static n => TruncateLine(n, 60)) ?? "Secure Note",
    BitwardenItemType.Card => CardLast4 is { } last4
        ? $"{CardBrand ?? "Card"} ····{last4}" : CardBrand ?? "Card",
    BitwardenItemType.Identity => IdentityEmail ?? IdentityFullName ?? "Identity",
    BitwardenItemType.SshKey => SshFingerprint ?? "SSH Key",
    _ => string.Empty,
  };

  internal static string? TruncateLine(ReadOnlySpan<char> text, int max)
  {
    if (text.IsEmpty) return null;
    var newline = text.IndexOf('\n');
    var line = (newline >= 0 ? text[..newline] : text).Trim();
    return line.Length > max ? string.Concat(line[..max], "…") : new string(line);
  }
}
