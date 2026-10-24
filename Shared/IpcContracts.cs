using System;
using System.Collections.Generic;
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
}

internal static class IpcErrors
{
  public const string Reprompt = "reprompt";
  public const string Locked = "locked";
  public const string NotFound = "notFound";
  public const string Invalid = "invalid";
  public const string Failed = "failed";
  public const string Stale = "stale";
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
  IReadOnlyList<FieldDto> Fields);

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
internal sealed partial class IpcJson : JsonSerializerContext
{
  public static JsonElement ToElement<T>(T value, JsonTypeInfo<T> info) => JsonSerializer.SerializeToElement(value, info);

  public static T Read<T>(JsonElement? payload, JsonTypeInfo<T> info) =>
    payload is { ValueKind: JsonValueKind.Object } element && element.Deserialize(info) is { } value
      ? value
      : throw new IpcException(IpcErrors.Invalid);
}
