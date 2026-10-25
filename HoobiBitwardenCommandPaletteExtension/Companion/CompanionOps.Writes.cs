using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Pages;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  private static readonly IdentityEdit EmptyIdentity = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

  private static readonly (string Key, Func<IdentityEdit, string?> Get, Func<IdentityEdit, string?, IdentityEdit> Set)[] IdentityMap =
  [
    ("title", i => i.Title, (i, v) => i with { Title = v }),
    ("firstName", i => i.FirstName, (i, v) => i with { FirstName = v }),
    ("middleName", i => i.MiddleName, (i, v) => i with { MiddleName = v }),
    ("lastName", i => i.LastName, (i, v) => i with { LastName = v }),
    ("address1", i => i.Address1, (i, v) => i with { Address1 = v }),
    ("address2", i => i.Address2, (i, v) => i with { Address2 = v }),
    ("address3", i => i.Address3, (i, v) => i with { Address3 = v }),
    ("city", i => i.City, (i, v) => i with { City = v }),
    ("state", i => i.State, (i, v) => i with { State = v }),
    ("postalCode", i => i.PostalCode, (i, v) => i with { PostalCode = v }),
    ("country", i => i.Country, (i, v) => i with { Country = v }),
    ("company", i => i.Company, (i, v) => i with { Company = v }),
    ("email", i => i.Email, (i, v) => i with { Email = v }),
    ("phone", i => i.Phone, (i, v) => i with { Phone = v }),
    ("ssn", i => i.Ssn, (i, v) => i with { Ssn = v }),
    ("username", i => i.Username, (i, v) => i with { Username = v }),
    ("passportNumber", i => i.PassportNumber, (i, v) => i with { PassportNumber = v }),
    ("licenseNumber", i => i.LicenseNumber, (i, v) => i with { LicenseNumber = v }),
  ];

  internal async Task<EditableItem> GetEditableItemAsync(string? id, bool verified = false)
  {
    var item = FindGranted(id, null, verified);
    return item.EditableFromCache ? FromCache(item) : ToEditable(await _service.GetItemJsonAsync(item.Id) ?? throw new IpcException(IpcErrors.NotFound));
  }

  internal static EditableItem FromCache(BitwardenItem item) => new(
    item.Id,
    (int)item.Type,
    item.Name,
    item.FolderId,
    item.OrganizationId,
    item.CollectionIds.ToList(),
    item.Favorite,
    item.Reprompt == 1,
    item.Notes?.Reveal(),
    item.RevisionDate,
    item.CustomFields.Select(f => new CustomFieldEdit(f.Key, f.Value.Secret?.Reveal() ?? f.Value.Value, f.Value.IsHidden ? 1 : 0)).ToList(),
    item.Type == BitwardenItemType.Login
      ? new LoginEdit(item.Username, item.Password?.Reveal(), item.TotpSecret?.Reveal(),
        item.Uris.Select(u => new UriEdit(u.Uri, u.Match == UriMatchType.Default ? null : (int)u.Match)).ToList())
      : null,
    item.Type == BitwardenItemType.Card ? new CardEdit(item.CardholderName, item.CardBrand, item.CardNumber?.Reveal(), item.CardExpMonth, item.CardExpYear, item.CardCode?.Reveal()) : null,
    null,
    item.Type == BitwardenItemType.SshKey ? new SshKeyEdit(item.SshPrivateKey?.Reveal(), item.SshPublicKey, item.SshFingerprint) : null);

  internal async Task<PasswordHistoryResponse> GetPasswordHistoryAsync(string? id, bool verified = false)
  {
    var item = FindGranted(id, null, verified);
    return new(item.PasswordHistory?.Select(h => h.LastUsed).ToList()
      ?? HistoryOf(await _service.GetItemJsonAsync(item.Id)).Select(h => h.LastUsed).ToList());
  }

  internal async Task CopyPasswordHistoryAsync(string? id, string field, DateTime revisionDate, bool verified = false)
  {
    var item = FindGranted(id, revisionDate, verified);
    if (!int.TryParse(field.AsSpan(ItemFields.HistoryPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
      throw new IpcException(IpcErrors.Invalid);
    var password = item.PasswordHistory is { } cached
      ? index < cached.Count ? cached[index].Password.Reveal() : null
      : HistoryOf(await _service.GetItemJsonAsync(item.Id)).ElementAtOrDefault(index).Password;
    _copyText(password ?? throw new IpcException(IpcErrors.Invalid), true);
  }

  internal async Task<SavedResponse> CreateItemAsync(EditableItem item)
  {
    Validate(item);
    var organizationId = item.OrganizationId is null ? null : RequireGuid(item.OrganizationId);
    var collectionIds = organizationId is null ? null : RequireIds(item.CollectionIds, atLeastOne: true);
    RequireUnlocked();
    var node = NewItemJson(item.Type);
    Apply(node, item);
    node["organizationId"] = organizationId;
    node["collectionIds"] = collectionIds is null ? null : new JsonArray([.. collectionIds.Select(c => (JsonNode?)JsonValue.Create(c))]);
    return Saved(await _service.CreateItemAsync(node)) ?? throw new IpcException(IpcErrors.Failed);
  }

  internal async Task<SavedResponse> UpdateItemAsync(EditableItem item, bool verified = false)
  {
    Validate(item);
    var id = RequireGuid(item.Id);
    var revision = item.RevisionDate ?? throw new IpcException(IpcErrors.Invalid);
    RequireUnlocked();
    await RequireWriteGrantAsync(id, verified);
    var saved = await _service.EditItemAsync(id, revision, current =>
    {
      if (current["type"]?.GetValue<int>() != item.Type)
        throw new IpcException(IpcErrors.Invalid);
      Apply(current, item);
      return current;
    });
    return Saved(saved) ?? throw new IpcException(IpcErrors.Failed);
  }

  internal async Task<SavedResponse?> WriteAsync(string op, WriteRequest request, bool verified = false)
  {
    var id = RequireGuid(request.Id);
    var organizationId = op == Ops.MoveItem ? RequireGuid(request.OrganizationId) : null;
    var collectionIds = op is Ops.MoveItem or Ops.SetCollections ? RequireIds(request.CollectionIds, atLeastOne: true) : null;
    if (op is not (Ops.DeleteItem or Ops.ArchiveItem or Ops.MoveItem or Ops.SetCollections))
      throw new IpcException(IpcErrors.Invalid);
    RequireUnlocked();
    await RequireWriteGrantAsync(id, verified);
    return Saved(op switch
    {
      Ops.DeleteItem => await _service.DeleteItemAsync(id, request.RevisionDate, request.Permanent),
      Ops.ArchiveItem => await _service.ArchiveItemAsync(id, request.RevisionDate),
      Ops.MoveItem => await _service.MoveItemAsync(id, request.RevisionDate, organizationId!, collectionIds!),
      _ => await _service.SetCollectionsAsync(id, request.RevisionDate, collectionIds!),
    });
  }

  private async Task RequireWriteGrantAsync(string id, bool verified)
  {
    if (verified || RepromptPage.IsWithinGracePeriod(id))
      return;
    var reprompt = _service.GetCachedItem(id)?.Reprompt ?? Int(await _service.GetItemJsonAsync(id), "reprompt");
    if (reprompt == 1)
      throw new IpcException(IpcErrors.Reprompt);
  }

  private void RequireUnlocked()
  {
    if (!_service.IsUnlocked)
      throw new IpcException(IpcErrors.Locked);
  }

  private static string RequireGuid(string? id) => Guid.TryParse(id, out _) ? id! : throw new IpcException(IpcErrors.Invalid);

  private static List<string> RequireIds(IReadOnlyList<string>? ids, bool atLeastOne) =>
    ids is null || (atLeastOne && ids.Count == 0) ? throw new IpcException(IpcErrors.Invalid) : [.. ids.Select(RequireGuid)];

  private static SavedResponse? Saved(JsonObject saved) =>
    Str(saved, "id") is { } id ? new SavedResponse(id, BitwardenCliService.RevisionOf(saved) ?? default) : null;

  internal static void Validate(EditableItem item)
  {
    var valid = item.Type is >= 1 and <= 5
      && !string.IsNullOrWhiteSpace(item.Name)
      && (item.FolderId is null || Guid.TryParse(item.FolderId, out _))
      && item.Fields.All(f => f.Type is >= 0 and <= 3 && (f.Type == 3 ? ItemRules.IsValidLink(item.Type, f.LinkedId) : f.LinkedId is null))
      && (item.Login?.Uris.All(u => u.Match is null or (>= 0 and <= 5)) ?? true)
      && ItemRules.IsValidMonth(item.Card?.ExpMonth)
      && (item.Type != 5 || item.SshKey is { PrivateKey.Length: > 0, PublicKey.Length: > 0, Fingerprint.Length: > 0 });
    if (!valid)
      throw new IpcException(IpcErrors.Invalid);
  }

  internal static JsonObject NewItemJson(int type) => new()
  {
    ["organizationId"] = null,
    ["collectionIds"] = null,
    ["folderId"] = null,
    ["type"] = type,
    ["name"] = string.Empty,
    ["notes"] = null,
    ["favorite"] = false,
    ["fields"] = new JsonArray(),
    ["reprompt"] = 0,
    ["passwordHistory"] = new JsonArray(),
  };

  internal static void Apply(JsonObject node, EditableItem item)
  {
    node["name"] = item.Name;
    node["notes"] = Text(item.Notes);
    node["favorite"] = item.Favorite;
    node["reprompt"] = item.Reprompt ? 1 : 0;
    node["folderId"] = item.FolderId;
    node["fields"] = new JsonArray([.. item.Fields.Select(f => (JsonNode?)new JsonObject
    {
      ["name"] = Text(f.Name),
      ["value"] = Text(f.Value),
      ["type"] = f.Type,
      ["linkedId"] = f.LinkedId,
    })]);

    switch (item.Type)
    {
      case 1:
        var login = Child(node, "login");
        login["username"] = Text(item.Login?.Username);
        login["password"] = Text(item.Login?.Password);
        login["totp"] = Text(item.Login?.Totp);
        login["uris"] = new JsonArray([.. (item.Login?.Uris ?? Array.Empty<UriEdit>()).Select(u => (JsonNode?)new JsonObject { ["match"] = u.Match, ["uri"] = u.Uri })]);
        break;
      case 2:
        node["secureNote"] ??= new JsonObject { ["type"] = 0 };
        break;
      case 3:
        var card = Child(node, "card");
        var c = item.Card;
        card["cardholderName"] = Text(c?.CardholderName);
        card["brand"] = Text(c?.Brand);
        card["number"] = Text(c?.Number);
        card["expMonth"] = Text(c?.ExpMonth);
        card["expYear"] = Text(c?.ExpYear);
        card["code"] = Text(c?.Code);
        break;
      case 4:
        var identity = Child(node, "identity");
        foreach (var (key, get, _) in IdentityMap)
          identity[key] = Text(get(item.Identity ?? EmptyIdentity));
        break;
      case 5:
        var ssh = Child(node, "sshKey");
        ssh["privateKey"] = item.SshKey?.PrivateKey;
        ssh["publicKey"] = item.SshKey?.PublicKey;
        ssh["keyFingerprint"] = item.SshKey?.Fingerprint;
        break;
    }
  }

  internal static EditableItem ToEditable(JsonObject node)
  {
    var type = Int(node, "type") ?? 0;
    var login = node["login"];
    var card = node["card"];
    var identity = node["identity"];
    var ssh = node["sshKey"];
    return new EditableItem(
      Str(node, "id"),
      type,
      Str(node, "name") ?? string.Empty,
      Str(node, "folderId"),
      Str(node, "organizationId"),
      (node["collectionIds"] as JsonArray ?? []).Select(c => Str(c)).OfType<string>().ToList(),
      node["favorite"] is JsonValue favorite && favorite.TryGetValue<bool>(out var fav) && fav,
      Int(node, "reprompt") == 1,
      Str(node, "notes"),
      BitwardenCliService.RevisionOf(node),
      (node["fields"] as JsonArray ?? []).Select(f => new CustomFieldEdit(Str(f, "name") ?? string.Empty, Str(f, "value"), Int(f, "type") ?? 0, Int(f, "linkedId"))).ToList(),
      type == 1
        ? new LoginEdit(Str(login, "username"), Str(login, "password"), Str(login, "totp"),
          (login?["uris"] as JsonArray ?? []).Select(u => new UriEdit(Str(u, "uri"), Int(u, "match"))).ToList())
        : null,
      type == 3 ? new CardEdit(Str(card, "cardholderName"), Str(card, "brand"), Str(card, "number"), Str(card, "expMonth"), Str(card, "expYear"), Str(card, "code")) : null,
      type == 4 ? IdentityMap.Aggregate(EmptyIdentity, (edit, map) => map.Set(edit, Str(identity, map.Key))) : null,
      type == 5 ? new SshKeyEdit(Str(ssh, "privateKey"), Str(ssh, "publicKey"), Str(ssh, "keyFingerprint")) : null);
  }

  private static List<(string Password, DateTime LastUsed)> HistoryOf(JsonObject? node) =>
    [.. (node?["passwordHistory"] as JsonArray ?? [])
      .Select(h => (Password: Str(h, "password"), LastUsed: DateTime.TryParse(Str(h, "lastUsedDate"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var used) ? used : default))
      .Where(h => !string.IsNullOrEmpty(h.Password))
      .Select(h => (h.Password!, h.LastUsed))];

  private static JsonObject Child(JsonObject node, string key) => node[key] as JsonObject ?? (JsonObject)(node[key] = new JsonObject())!;

  private static string? Text(string? value) => string.IsNullOrEmpty(value) ? null : value;

  private static string? Str(JsonNode? node, string key) => Str(node?[key]);

  private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

  private static int? Int(JsonNode? node, string key) => node?[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;
}
