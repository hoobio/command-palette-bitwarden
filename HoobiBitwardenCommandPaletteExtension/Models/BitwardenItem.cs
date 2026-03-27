using System;
using System.Collections.Generic;
using System.Linq;

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
  Default = -1, // null in JSON — behaves like Domain
  Domain = 0,
  Host = 1,
  StartsWith = 2,
  Exact = 3,
  RegularExpression = 4,
  Never = 5,
}

internal sealed record ItemUri(string Uri, UriMatchType Match);

internal sealed record CustomField(string Value, bool IsHidden);

internal sealed class BitwardenItem
{
  public string Id { get; init; } = string.Empty;
  public string Name { get; init; } = string.Empty;
  public BitwardenItemType Type { get; init; }
  public string? Notes { get; init; }
  public DateTime RevisionDate { get; init; }
  public bool Favorite { get; init; }
  public string? FolderId { get; init; }
  public string? OrganizationId { get; init; }
  public int Reprompt { get; init; }

  // Login
  public string? Username { get; init; }
  public string? Password { get; init; }
  public bool HasTotp { get; init; }
  public string? TotpSecret { get; init; }
  public bool HasPasskey { get; init; }
  public List<ItemUri> Uris { get; init; } = [];
  public string? FirstUri => Uris.Count > 0 ? Uris[0].Uri : null;
  public DateTime? PasswordRevisionDate { get; init; }

  // Card
  public string? CardholderName { get; init; }
  public string? CardBrand { get; init; }
  public string? CardNumber { get; init; }
  public string? CardExpMonth { get; init; }
  public string? CardExpYear { get; init; }
  public string? CardCode { get; init; }
  public string? CardExpiration => CardExpMonth != null && CardExpYear != null
      ? $"{CardExpMonth} / {CardExpYear}" : null;
  public string? CardLast4 => CardNumber?.Length >= 4 ? CardNumber[^4..] : null;

  // Identity
  public string? IdentityFullName { get; init; }
  public string? IdentityEmail { get; init; }
  public string? IdentityPhone { get; init; }
  public string? IdentityUsername { get; init; }
  public string? IdentityCompany { get; init; }
  public string? IdentityAddress { get; init; }
  public string? IdentitySsn { get; init; }
  public string? IdentityPassportNumber { get; init; }
  public string? IdentityLicenseNumber { get; init; }

  // SSH Key
  public string? SshPublicKey { get; init; }
  public string? SshFingerprint { get; init; }
  public string? SshPrivateKey { get; init; }

  // Custom fields
  public Dictionary<string, CustomField> CustomFields { get; init; } = [];
  public string? SshHost => CustomFields.TryGetValue("host", out var h) ? h.Value : null;

  public string Subtitle => Type switch
  {
    BitwardenItemType.Login => Username ?? string.Empty,
    BitwardenItemType.SecureNote when Reprompt == 1 => "Protected",
    BitwardenItemType.SecureNote => TruncateLine(Notes, 60) ?? "Secure Note",
    BitwardenItemType.Card => CardBrand != null && CardLast4 != null
        ? $"{CardBrand} ····{CardLast4}" : CardBrand ?? "Card",
    BitwardenItemType.Identity => IdentityEmail ?? IdentityFullName ?? "Identity",
    BitwardenItemType.SshKey => SshFingerprint ?? "SSH Key",
    _ => string.Empty,
  };

  internal static string? TruncateLine(string? text, int max)
  {
    if (string.IsNullOrEmpty(text)) return null;
    var line = text.Split('\n').FirstOrDefault()?.Trim();
    return line?.Length > max ? line[..max] + "…" : line;
  }
}
