using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionOps
{
  private static readonly string[] IdentityKeys =
    ["title", "firstName", "middleName", "lastName", "address1", "address2", "address3", "city", "state", "postalCode", "country", "company", "email", "phone", "ssn", "username", "passportNumber", "licenseNumber"];

  internal async Task<EditableItem> GetEditableItemAsync(string? id, bool verified = false)
  {
    var item = FindGranted(id, null, verified);
    return ToEditable(await _service.GetItemJsonAsync(item.Id) ?? throw new IpcException(IpcErrors.NotFound));
  }

  internal async Task<PasswordHistoryResponse> GetPasswordHistoryAsync(string? id, bool verified = false)
  {
    var item = FindGranted(id, null, verified);
    return new(HistoryOf(await _service.GetItemJsonAsync(item.Id)).Select(h => h.LastUsed).ToList());
  }

  internal async Task CopyPasswordHistoryAsync(string? id, string field, DateTime revisionDate, bool verified = false)
  {
    var item = FindGranted(id, revisionDate, verified);
    var history = HistoryOf(await _service.GetItemJsonAsync(item.Id));
    if (!int.TryParse(field.AsSpan(ItemFields.HistoryPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= history.Count)
      throw new IpcException(IpcErrors.Invalid);
    _copyText(history[index].Password, true);
  }

  internal async Task<SavedResponse> CreateItemAsync(EditableItem item)
  {
    Validate(item);
    RequireUnlocked();
    var node = NewItemJson(item.Type);
    Apply(node, item);
    return Saved(await _service.CreateItemAsync(node)) ?? throw new IpcException(IpcErrors.Failed);
  }

  internal async Task<SavedResponse> UpdateItemAsync(EditableItem item)
  {
    Validate(item);
    var id = RequireGuid(item.Id);
    var revision = item.RevisionDate ?? throw new IpcException(IpcErrors.Invalid);
    RequireUnlocked();
    var saved = await _service.EditItemAsync(id, revision, current =>
    {
      if (current["type"]?.GetValue<int>() != item.Type)
        throw new IpcException(IpcErrors.Invalid);
      Apply(current, item);
      return current;
    });
    return Saved(saved) ?? throw new IpcException(IpcErrors.Failed);
  }

  internal async Task<SavedResponse?> WriteAsync(string op, WriteRequest request)
  {
    var id = RequireGuid(request.Id);
    RequireUnlocked();
    return Saved(op switch
    {
      Ops.DeleteItem => await _service.DeleteItemAsync(id, request.RevisionDate, request.Permanent),
      Ops.RestoreItem => await _service.RestoreItemAsync(id, request.RevisionDate),
      Ops.ArchiveItem => await _service.ArchiveItemAsync(id, request.RevisionDate),
      Ops.MoveItem => await _service.MoveItemAsync(id, request.RevisionDate, RequireGuid(request.OrganizationId), RequireIds(request.CollectionIds, atLeastOne: true)),
      Ops.SetCollections => await _service.SetCollectionsAsync(id, request.RevisionDate, RequireIds(request.CollectionIds, atLeastOne: false)),
      _ => throw new IpcException(IpcErrors.Invalid),
    });
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
      && item.Fields.All(f => !string.IsNullOrWhiteSpace(f.Name) && f.Type is >= 0 and <= 3 && (f.Type == 3 ? ItemRules.IsValidLink(item.Type, f.LinkedId) : f.LinkedId is null))
      && (item.Login?.Uris.All(u => ItemRules.IsValidUri(u.Uri, u.Match) && u.Match is null or (>= 0 and <= 5)) ?? true)
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
      ["name"] = f.Name,
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
        var values = IdentityValues(item.Identity);
        for (var i = 0; i < IdentityKeys.Length; i++)
          identity[IdentityKeys[i]] = Text(values[i]);
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
          (login?["uris"] as JsonArray ?? []).Select(u => new UriEdit(Str(u, "uri") ?? string.Empty, Int(u, "match"))).ToList())
        : null,
      type == 3 ? new CardEdit(Str(card, "cardholderName"), Str(card, "brand"), Str(card, "number"), Str(card, "expMonth"), Str(card, "expYear"), Str(card, "code")) : null,
      type == 4 ? IdentityFrom(identity) : null,
      type == 5 ? new SshKeyEdit(Str(ssh, "privateKey"), Str(ssh, "publicKey"), Str(ssh, "keyFingerprint")) : null);
  }

  private static IdentityEdit IdentityFrom(JsonNode? node)
  {
    var v = IdentityKeys.Select(k => Str(node, k)).ToArray();
    return new IdentityEdit(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15], v[16], v[17]);
  }

  private static string?[] IdentityValues(IdentityEdit? i) => i is null
    ? new string?[IdentityKeys.Length]
    : [i.Title, i.FirstName, i.MiddleName, i.LastName, i.Address1, i.Address2, i.Address3, i.City, i.State, i.PostalCode, i.Country, i.Company, i.Email, i.Phone, i.Ssn, i.Username, i.PassportNumber, i.LicenseNumber];

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
