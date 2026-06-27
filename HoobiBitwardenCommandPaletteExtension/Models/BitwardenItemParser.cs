using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace HoobiBitwardenCommandPaletteExtension.Models;

// Parses `bw` item/folder/organization JSON into the models. Process-agnostic and log-free so it is
// linked into BOTH the extension (cache + edit round-trip) and the companion (item detail display),
// keeping a single source of truth for the Bitwarden JSON shape. The extension exposes thin
// forwarders (BitwardenCliService.ParseItems etc.) so existing call sites/tests are unchanged.
internal static class BitwardenItemParser
{
  public static List<BitwardenItem> ParseItems(string json)
  {
    var items = new List<BitwardenItem>();
    try
    {
      var array = JsonNode.Parse(json)?.AsArray();
      if (array == null) return items;

      foreach (var node in array)
      {
        var item = TryParseItemNode(node);
        if (item != null) items.Add(item);
      }
    }
    catch
    {
      // Malformed output - return whatever parsed cleanly.
    }
    return items;
  }

  public static BitwardenItem? TryParseItemNode(JsonNode? node)
  {
    if (node == null) return null;

    var typeInt = node["type"]?.GetValue<int>() ?? 0;
    if (typeInt < 1 || typeInt > 5) return null;

    var type = (BitwardenItemType)typeInt;
    var id = node["id"]?.GetValue<string>() ?? string.Empty;
    var name = node["name"]?.GetValue<string>() ?? string.Empty;
    var notes = node["notes"]?.GetValue<string>();
    var revisionDate = DateTime.TryParse(node["revisionDate"]?.GetValue<string>(), out var rd) ? rd.ToUniversalTime() : DateTime.MinValue;
    var customFields = ParseCustomFields(node["fields"]);
    var favorite = node["favorite"]?.GetValue<bool>() ?? false;
    var folderId = node["folderId"]?.GetValue<string>();
    var organizationId = node["organizationId"]?.GetValue<string>();
    var reprompt = node["reprompt"]?.GetValue<int>() ?? 0;

    var item = type switch
    {
      BitwardenItemType.Login => ParseLogin(node["login"], id, name, notes, revisionDate, customFields, favorite, folderId, organizationId, reprompt),
      BitwardenItemType.SecureNote => new BitwardenItem { Id = id, Name = name, Type = type, Notes = notes, RevisionDate = revisionDate, CustomFields = customFields, Favorite = favorite, FolderId = folderId, OrganizationId = organizationId, Reprompt = reprompt },
      BitwardenItemType.Card => ParseCard(node["card"], id, name, notes, revisionDate, customFields, favorite, folderId, organizationId, reprompt),
      BitwardenItemType.Identity => ParseIdentity(node["identity"], id, name, notes, revisionDate, customFields, favorite, folderId, organizationId, reprompt),
      BitwardenItemType.SshKey => ParseSshKey(node["sshKey"], id, name, notes, revisionDate, customFields, favorite, folderId, organizationId, reprompt),
      _ => null,
    };

    // Keep the source JSON so the companion's edit round-trip can be served from the cache.
    if (item != null) item.RawJson = node.ToJsonString();
    return item;
  }

  private static BitwardenItem ParseLogin(JsonNode? login, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, CustomField> customFields, bool favorite, string? folderId, string? organizationId, int reprompt)
  {
    var uris = login?["uris"]?.AsArray()
        ?.Select(u =>
        {
          var uri = u?["uri"]?.GetValue<string>();
          if (string.IsNullOrEmpty(uri)) return null;
          var matchVal = u?["match"];
          var match = matchVal is null
              ? UriMatchType.Default
              : (UriMatchType)matchVal.GetValue<int>();
          return new ItemUri(uri, match);
        })
        .Where(u => u != null)
        .Cast<ItemUri>()
        .ToList() ?? [];

    var passwordRevision = DateTime.TryParse(login?["passwordRevisionDate"]?.GetValue<string>(), out var prd) ? (DateTime?)prd.ToUniversalTime() : null;

    return new BitwardenItem
    {
      Id = id,
      Name = name,
      Type = BitwardenItemType.Login,
      Notes = notes,
      RevisionDate = revisionDate,
      CustomFields = customFields,
      Favorite = favorite,
      FolderId = folderId,
      OrganizationId = organizationId,
      Reprompt = reprompt,
      Username = login?["username"]?.GetValue<string>(),
      Password = login?["password"]?.GetValue<string>(),
      HasTotp = !string.IsNullOrEmpty(login?["totp"]?.GetValue<string>()),
      TotpSecret = login?["totp"]?.GetValue<string>(),
      HasPasskey = login?["fido2Credentials"] is JsonArray fido && fido.Count > 0,
      Uris = uris,
      PasswordRevisionDate = passwordRevision,
    };
  }

  private static BitwardenItem ParseCard(JsonNode? card, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, CustomField> customFields, bool favorite, string? folderId, string? organizationId, int reprompt) => new()
  {
    Id = id,
    Name = name,
    Type = BitwardenItemType.Card,
    Notes = notes,
    RevisionDate = revisionDate,
    CustomFields = customFields,
    Favorite = favorite,
    FolderId = folderId,
    OrganizationId = organizationId,
    Reprompt = reprompt,
    CardholderName = card?["cardholderName"]?.GetValue<string>(),
    CardBrand = card?["brand"]?.GetValue<string>(),
    CardNumber = card?["number"]?.GetValue<string>(),
    CardExpMonth = card?["expMonth"]?.GetValue<string>(),
    CardExpYear = card?["expYear"]?.GetValue<string>(),
    CardCode = card?["code"]?.GetValue<string>(),
  };

  private static BitwardenItem ParseIdentity(JsonNode? id_node, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, CustomField> customFields, bool favorite, string? folderId, string? organizationId, int reprompt)
  {
    var parts = new[] { id_node?["firstName"]?.GetValue<string>(), id_node?["middleName"]?.GetValue<string>(), id_node?["lastName"]?.GetValue<string>() };
    var fullName = string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));

    var addrParts = new[] { id_node?["address1"]?.GetValue<string>(), id_node?["address2"]?.GetValue<string>(), id_node?["address3"]?.GetValue<string>() };
    var addrLine = string.Join(", ", addrParts.Where(p => !string.IsNullOrEmpty(p)));
    var cityParts = new[] { id_node?["city"]?.GetValue<string>(), id_node?["state"]?.GetValue<string>(), id_node?["postalCode"]?.GetValue<string>() };
    var cityLine = string.Join(", ", cityParts.Where(p => !string.IsNullOrEmpty(p)));
    var country = id_node?["country"]?.GetValue<string>();
    var address = string.Join("\n", new[] { addrLine, cityLine, country }.Where(p => !string.IsNullOrEmpty(p)));

    return new BitwardenItem
    {
      Id = id,
      Name = name,
      Type = BitwardenItemType.Identity,
      Notes = notes,
      RevisionDate = revisionDate,
      CustomFields = customFields,
      Favorite = favorite,
      FolderId = folderId,
      OrganizationId = organizationId,
      Reprompt = reprompt,
      IdentityFullName = string.IsNullOrEmpty(fullName) ? null : fullName,
      IdentityEmail = id_node?["email"]?.GetValue<string>(),
      IdentityPhone = id_node?["phone"]?.GetValue<string>(),
      IdentityUsername = id_node?["username"]?.GetValue<string>(),
      IdentityCompany = id_node?["company"]?.GetValue<string>(),
      IdentityAddress = string.IsNullOrEmpty(address) ? null : address,
      IdentitySsn = id_node?["ssn"]?.GetValue<string>(),
      IdentityPassportNumber = id_node?["passportNumber"]?.GetValue<string>(),
      IdentityLicenseNumber = id_node?["licenseNumber"]?.GetValue<string>(),
    };
  }

  private static BitwardenItem ParseSshKey(JsonNode? ssh, string id, string name, string? notes, DateTime revisionDate, Dictionary<string, CustomField> customFields, bool favorite, string? folderId, string? organizationId, int reprompt) => new()
  {
    Id = id,
    Name = name,
    Type = BitwardenItemType.SshKey,
    Notes = notes,
    RevisionDate = revisionDate,
    CustomFields = customFields,
    Favorite = favorite,
    FolderId = folderId,
    OrganizationId = organizationId,
    Reprompt = reprompt,
    SshPublicKey = ssh?["publicKey"]?.GetValue<string>(),
    SshFingerprint = ssh?["keyFingerprint"]?.GetValue<string>(),
    SshPrivateKey = ssh?["privateKey"]?.GetValue<string>(),
  };

  public static Dictionary<string, CustomField> ParseCustomFields(JsonNode? fields)
  {
    var result = new Dictionary<string, CustomField>(StringComparer.OrdinalIgnoreCase);
    if (fields is not JsonArray arr) return result;

    foreach (var field in arr)
    {
      var fieldName = field?["name"]?.GetValue<string>();
      var fieldValue = field?["value"]?.GetValue<string>();
      var fieldType = field?["type"]?.GetValue<int>() ?? 0;
      if (!string.IsNullOrEmpty(fieldName) && fieldValue != null)
        result.TryAdd(fieldName, new CustomField(fieldValue, fieldType == 1));
    }

    return result;
  }

  public static Dictionary<string, string> ParseFolders(string json) => ParseIdNameMap(json);

  public static Dictionary<string, string> ParseOrganizations(string json) => ParseIdNameMap(json);

  private static Dictionary<string, string> ParseIdNameMap(string json)
  {
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    try
    {
      var array = JsonNode.Parse(json)?.AsArray();
      if (array == null) return result;

      foreach (var node in array)
      {
        var id = node?["id"]?.GetValue<string>();
        var name = node?["name"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
          result[id] = name;
      }
    }
    catch
    {
      // Malformed output - return whatever parsed cleanly.
    }
    return result;
  }
}
