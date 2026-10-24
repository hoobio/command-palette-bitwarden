using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HoobiBitwarden.Ipc;

internal static class Ops
{
  public const string GetItem = "getItem";
  public const string Reveal = "reveal";
  public const string Copy = "copy";
  public const string GetTotp = "getTotp";
  public const string OpenUri = "openUri";
  public const string VerifyReprompt = "verifyReprompt";
  public const string ShowItem = "showItem";
  public const string ShowVault = "showVault";
  public const string Locked = "locked";
  public const string ListItems = "listItems";
  public const string ListFilters = "listFilters";
  public const string VaultChanged = "vaultChanged";
  public const string GetEditableItem = "getEditableItem";
  public const string GetPasswordHistory = "getPasswordHistory";
  public const string CreateItem = "createItem";
  public const string UpdateItem = "updateItem";
  public const string DeleteItem = "deleteItem";
  public const string RestoreItem = "restoreItem";
  public const string ArchiveItem = "archiveItem";
  public const string MoveItem = "moveItem";
  public const string SetCollections = "setCollections";
}

internal static class IpcErrors
{
  public const string Reprompt = "reprompt";
  public const string Locked = "locked";
  public const string NotFound = "notFound";
  public const string Invalid = "invalid";
  public const string Failed = "failed";
  public const string Stale = "stale";
  public const string Conflict = "conflict";
  public const string Unknown = "unknown";
}

internal static class MessageTypes
{
  public const string Request = "req";
  public const string Response = "res";
  public const string Event = "evt";
}

internal static class ItemFields
{
  public const string Username = "username";
  public const string Password = "password";
  public const string Totp = "totp";
  public const string Notes = "notes";
  public const string CardholderName = "cardholderName";
  public const string CardBrand = "cardBrand";
  public const string CardNumber = "cardNumber";
  public const string CardExpiration = "cardExpiration";
  public const string CardCode = "cardCode";
  public const string IdentityName = "identityName";
  public const string IdentityUsername = "identityUsername";
  public const string IdentityCompany = "identityCompany";
  public const string IdentityEmail = "identityEmail";
  public const string IdentityPhone = "identityPhone";
  public const string IdentityAddress = "identityAddress";
  public const string SshPrivateKey = "sshPrivateKey";
  public const string SshPublicKey = "sshPublicKey";
  public const string SshFingerprint = "sshFingerprint";
  public const string UriPrefix = "uri:";
  public const string CustomPrefix = "custom:";
  public const string HistoryPrefix = "history:";
}

internal static class ItemRules
{
  public static readonly IReadOnlyDictionary<int, (int Id, string Label)[]> LinkTargets = new Dictionary<int, (int Id, string Label)[]>
  {
    [1] = [(100, "Username"), (101, "Password")],
    [3] = [(300, "Cardholder name"), (301, "Expiration month"), (302, "Expiration year"), (303, "Security code"), (304, "Brand"), (305, "Number")],
    [4] =
    [
      (400, "Title"), (416, "First name"), (401, "Middle name"), (417, "Last name"), (418, "Full name"), (413, "Username"), (409, "Company"),
      (412, "National insurance number"), (414, "Passport number"), (415, "Licence number"), (410, "Email"), (411, "Phone"),
      (402, "Address 1"), (403, "Address 2"), (404, "Address 3"), (405, "City / Town"), (406, "County / State"), (407, "Postcode"), (408, "Country"),
    ],
  };

  public static bool IsValidMonth(string? month) =>
    string.IsNullOrEmpty(month) || (int.TryParse(month, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= 12);

  public static bool IsValidLink(int type, int? linkedId) =>
    linkedId is { } id && LinkTargets.TryGetValue(type, out var targets) && targets.Any(t => t.Id == id);
}

internal sealed class IpcException : Exception
{
  public IpcException() : this(IpcErrors.Failed) { }

  public IpcException(string code) : base(code) { }

  public IpcException(string code, Exception innerException) : base(code, innerException) { }

  public string Code => Message;
}

internal sealed record Envelope(long Id, string Type, string? Op = null, JsonElement? Payload = null, string? Error = null);

internal sealed record ItemRequest(string Id);

internal sealed record FieldRequest(string Id, string Field, DateTime RevisionDate);

internal sealed record UriRequest(string Id, int Index, DateTime RevisionDate);

internal sealed record RepromptRequest(string Id, string Password, string? Op = null, JsonElement? Payload = null);

internal sealed record SecretResponse(string Value);

internal sealed record TotpResponse(string Code, int Remaining, int Period);

internal sealed record RepromptResponse(bool Verified, int CooldownSeconds, JsonElement? Result = null);

internal sealed record FieldDto(string Key, string Label, string Group, string? Value, bool IsSecret, bool CanOpen = false, bool IsMultiline = false);

internal sealed record ItemDto(
  string Id,
  string Name,
  int Type,
  string? FolderName,
  string? OrganizationName,
  bool Favorite,
  bool Reprompt,
  bool RepromptRequired,
  DateTime? CreationDate,
  DateTime RevisionDate,
  DateTime? PasswordRevisionDate,
  IReadOnlyList<FieldDto> Fields,
  bool Deleted = false,
  bool Archived = false);

internal sealed record ItemSummary(
  string Id,
  int Type,
  string Name,
  string? Subtext,
  bool Favorite,
  string? FolderId,
  string? OrganizationId,
  IReadOnlyList<string> CollectionIds,
  bool Deleted,
  bool Archived,
  DateTime RevisionDate,
  bool HasTotp,
  bool Reprompt,
  bool HasNotes,
  IReadOnlyList<string> Hosts,
  string? IconPath = null);

internal sealed record ItemListResponse(IReadOnlyList<ItemSummary> Items);

internal sealed record FilterRef(string Id, string Name, string? OrganizationId = null);

internal sealed record FiltersResponse(IReadOnlyList<FilterRef> Folders, IReadOnlyList<FilterRef> Collections, IReadOnlyList<FilterRef> Organizations);

internal sealed record UriEdit(string? Uri, int? Match = null);

internal sealed record CustomFieldEdit(string Name, string? Value, int Type, int? LinkedId = null);

internal sealed record LoginEdit(string? Username, string? Password, string? Totp, IReadOnlyList<UriEdit> Uris);

internal sealed record CardEdit(string? CardholderName, string? Brand, string? Number, string? ExpMonth, string? ExpYear, string? Code);

internal sealed record IdentityEdit(
  string? Title,
  string? FirstName,
  string? MiddleName,
  string? LastName,
  string? Address1,
  string? Address2,
  string? Address3,
  string? City,
  string? State,
  string? PostalCode,
  string? Country,
  string? Company,
  string? Email,
  string? Phone,
  string? Ssn,
  string? Username,
  string? PassportNumber,
  string? LicenseNumber);

internal sealed record SshKeyEdit(string? PrivateKey, string? PublicKey, string? Fingerprint);

internal sealed record EditableItem(
  string? Id,
  int Type,
  string Name,
  string? FolderId,
  string? OrganizationId,
  IReadOnlyList<string> CollectionIds,
  bool Favorite,
  bool Reprompt,
  string? Notes,
  DateTime? RevisionDate,
  IReadOnlyList<CustomFieldEdit> Fields,
  LoginEdit? Login = null,
  CardEdit? Card = null,
  IdentityEdit? Identity = null,
  SshKeyEdit? SshKey = null);

internal sealed record WriteRequest(string Id, DateTime RevisionDate, bool Permanent = false, string? OrganizationId = null, IReadOnlyList<string>? CollectionIds = null);

internal sealed record SavedResponse(string Id, DateTime RevisionDate);

internal sealed record PasswordHistoryResponse(IReadOnlyList<DateTime> LastUsed);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Envelope))]
[JsonSerializable(typeof(ItemRequest))]
[JsonSerializable(typeof(FieldRequest))]
[JsonSerializable(typeof(UriRequest))]
[JsonSerializable(typeof(RepromptRequest))]
[JsonSerializable(typeof(SecretResponse))]
[JsonSerializable(typeof(TotpResponse))]
[JsonSerializable(typeof(RepromptResponse))]
[JsonSerializable(typeof(ItemDto))]
[JsonSerializable(typeof(ItemListResponse))]
[JsonSerializable(typeof(FiltersResponse))]
[JsonSerializable(typeof(EditableItem))]
[JsonSerializable(typeof(WriteRequest))]
[JsonSerializable(typeof(SavedResponse))]
[JsonSerializable(typeof(PasswordHistoryResponse))]
internal sealed partial class IpcJson : JsonSerializerContext
{
  public static JsonElement ToElement<T>(T value, JsonTypeInfo<T> info) => JsonSerializer.SerializeToElement(value, info);

  public static T Read<T>(JsonElement? payload, JsonTypeInfo<T> info) =>
    payload is { ValueKind: JsonValueKind.Object } element && element.Deserialize(info) is { } value
      ? value
      : throw new IpcException(IpcErrors.Invalid);
}
