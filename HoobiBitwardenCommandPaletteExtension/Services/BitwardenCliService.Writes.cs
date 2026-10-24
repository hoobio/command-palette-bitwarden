using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwardenCommandPaletteExtension.Models;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal enum VaultWriteError { Failed, NotFound, Conflict }

internal sealed class VaultWriteException : Exception
{
  public VaultWriteException() : this(VaultWriteError.Failed) { }

  public VaultWriteException(VaultWriteError error) : base(error.ToString()) => Error = error;

  public VaultWriteException(string message) : base(message) { }

  public VaultWriteException(string message, Exception innerException) : base(message, innerException) { }

  public VaultWriteError Error { get; }
}

internal sealed partial class BitwardenCliService
{
  private static readonly string[] WriteVerbs = ["create ", "edit ", "delete ", "restore ", "move ", "archive "];
  private static readonly string[] LoginKeysVerifiedApart = ["uris", "fido2Credentials", "passwordRevisionDate"];
  private static readonly Dictionary<int, string> TypeObjects = new() { [1] = "login", [2] = "secureNote", [3] = "card", [4] = "identity", [5] = "sshKey" };
  private readonly SemaphoreSlim _writeGate = new(1, 1);
  private int _writeVersion;

  internal int WriteVersion => Volatile.Read(ref _writeVersion);

  internal static bool IsDataWrite(string args) =>
    args == "sync" || WriteVerbs.Any(v => args.StartsWith(v, StringComparison.Ordinal));

  internal async Task<JsonObject?> GetItemJsonAsync(string id) =>
    ParseObject(await RunCliAsync($"get item {RequireId(id)}", verbOnlyLog: true));

  internal Task<JsonObject> CreateItemAsync(JsonObject item) =>
    WriteAsync(null, null, _ => RunCliAsync("create item", stdin: Encode(item), verbOnlyLog: true), (_, saved) => ContentMatches(item, saved));

  internal Task<JsonObject> EditItemAsync(string id, DateTime expectedRevision, Func<JsonObject, JsonObject> build)
  {
    JsonObject? sent = null;
    return WriteAsync(id, expectedRevision, current =>
    {
      sent = build(current!);
      return RunCliAsync($"edit item {id}", stdin: Encode(sent), verbOnlyLog: true);
    }, (_, saved) => ContentMatches(sent!, saved));
  }

  internal Task<JsonObject> DeleteItemAsync(string id, DateTime expectedRevision, bool permanent) =>
    WriteAsync(id, expectedRevision, _ => RunCliAsync(permanent ? $"delete item {id} --permanent" : $"delete item {id}", verbOnlyLog: true),
      (_, saved) => permanent ? saved is null : saved?["deletedDate"] is not null, allowGone: permanent);

  internal Task<JsonObject> RestoreItemAsync(string id, DateTime expectedRevision) =>
    WriteAsync(id, expectedRevision, _ => RunCliAsync($"restore item {id}", verbOnlyLog: true),
      (current, saved) => current!["deletedDate"] is not null ? saved?["deletedDate"] is null : saved?["archivedDate"] is null);

  internal Task<JsonObject> ArchiveItemAsync(string id, DateTime expectedRevision) =>
    WriteAsync(id, expectedRevision, _ => RunCliAsync($"archive item {id}", verbOnlyLog: true), (_, saved) => saved?["archivedDate"] is not null);

  internal Task<JsonObject> MoveItemAsync(string id, DateTime expectedRevision, string organizationId, IReadOnlyList<string> collectionIds) =>
    WriteAsync(id, expectedRevision, _ => RunCliAsync($"move {id} {RequireId(organizationId)}", stdin: EncodeIds(collectionIds), verbOnlyLog: true),
      (_, saved) => string.Equals(saved?["organizationId"]?.GetValue<string>(), organizationId, StringComparison.OrdinalIgnoreCase) && SameIds(saved, collectionIds));

  internal Task<JsonObject> SetCollectionsAsync(string id, DateTime expectedRevision, IReadOnlyList<string> collectionIds) =>
    WriteAsync(id, expectedRevision, _ => RunCliAsync($"edit item-collections {id}", stdin: EncodeIds(collectionIds), verbOnlyLog: true), (_, saved) => SameIds(saved, collectionIds));

  private async Task<JsonObject> WriteAsync(string? id, DateTime? expectedRevision, Func<JsonObject?, Task<string>> write, Func<JsonObject?, JsonObject?, bool> verify, bool allowGone = false)
  {
    if (id is not null)
      RequireId(id);
    await _writeGate.WaitAsync();
    try
    {
      int generation;
      lock (_cacheLock) generation = _sessionGeneration;
      JsonObject? current = null;
      if (id is not null)
      {
        current = await GetItemJsonAsync(id) ?? throw new VaultWriteException(VaultWriteError.NotFound);
        if (RevisionOf(current) != expectedRevision)
          throw new VaultWriteException(VaultWriteError.Conflict);
      }

      var output = await write(current);
      var result = ParseObject(output);
      if (result is null && output.Contains("out of date", StringComparison.OrdinalIgnoreCase))
        throw new VaultWriteException(VaultWriteError.Conflict);
      var savedId = id ?? (result?["id"]?.GetValue<string>() is { } created && Guid.TryParse(created, out _) ? created : throw new VaultWriteException(VaultWriteError.Failed));

      // The write process holds the sync gate until it exits, and bw flushes data.json after printing its result.
      await _syncGate.WaitAsync();
      _syncGate.Release();

      var saved = await GetItemJsonAsync(savedId);
      if (!verify(current, saved))
      {
        DebugLogService.Log("CLI", "Write verification failed");
        throw new VaultWriteException(VaultWriteError.Failed);
      }

      UpdateCachedItem(savedId, saved, generation);
      return saved ?? (allowGone ? new JsonObject() : throw new VaultWriteException(VaultWriteError.Failed));
    }
    finally
    {
      _writeGate.Release();
    }
  }

  private void UpdateCachedItem(string id, JsonObject? saved, int generation)
  {
    var item = saved is null ? null : TryParseItemNode(saved);
    var active = item is { DeletedDate: null, ArchivedDate: null };
    lock (_cacheLock)
    {
      if (generation != _sessionGeneration)
      {
        item?.DisposeSecrets();
        return;
      }

      var items = new List<BitwardenItem>(_cache);
      var index = items.FindIndex(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
      if (index >= 0)
        items.RemoveAt(index);
      if (active)
        items.Insert(index >= 0 ? index : items.Count, item!);
      _cache = items;
      Interlocked.Increment(ref _writeVersion);
    }

    if (!active)
      item?.DisposeSecrets();
    CacheUpdated?.Invoke();
  }

  internal static string RequireId(string? id) =>
    Guid.TryParse(id, out _) ? id! : throw new ArgumentException("Not a valid id", nameof(id));

  internal static DateTime? RevisionOf(JsonNode? item) =>
    DateTime.TryParse(item?["revisionDate"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var revision)
      ? revision
      : null;

  internal static JsonObject? ParseObject(string output)
  {
    try
    {
      return JsonNode.Parse(output) as JsonObject;
    }
    catch (System.Text.Json.JsonException)
    {
      return null;
    }
  }

  internal static string Encode(JsonNode node) => Convert.ToBase64String(Encoding.UTF8.GetBytes(node.ToJsonString()));

  private static string EncodeIds(IReadOnlyList<string> ids) =>
    Encode(new JsonArray([.. ids.Select(id => (JsonNode?)JsonValue.Create(RequireId(id)))]));

  private static bool SameIds(JsonObject? saved, IReadOnlyList<string> expected) =>
    saved?["collectionIds"] is JsonArray actual
    && actual.Select(c => c?.GetValue<string>()?.ToLowerInvariant()).ToHashSet().SetEquals(expected.Select(e => e.ToLowerInvariant()));

  internal static bool ContentMatches(JsonObject sent, JsonObject? saved)
  {
    if (saved is null)
      return false;
    foreach (var key in new[] { "type", "name", "notes", "favorite", "reprompt", "folderId" })
    {
      if (!Same(sent[key], saved[key]))
        return false;
    }

    if (!Same(Project(sent["fields"], "name", "value", "type", "linkedId"), Project(saved["fields"], "name", "value", "type", "linkedId")))
      return false;
    if (!TypeObjects.TryGetValue(sent["type"]?.GetValue<int>() ?? 0, out var typeKey) || sent[typeKey] is not JsonObject expected)
      return true;
    var actual = saved[typeKey] as JsonObject;
    foreach (var (key, value) in expected)
    {
      if (!LoginKeysVerifiedApart.Contains(key) && !Same(value, actual?[key]))
        return false;
    }

    return typeKey != "login"
      || (Same(Project(expected["uris"], "uri", "match"), Project(actual?["uris"], "uri", "match"))
        && CountOf(expected["fido2Credentials"]) == CountOf(actual?["fido2Credentials"]));
  }

  private static int CountOf(JsonNode? node) => (node as JsonArray)?.Count ?? 0;

  private static JsonArray? Project(JsonNode? array, params string[] keys) =>
    array is JsonArray items && items.Count > 0
      ? new JsonArray([.. items.Select(i => (JsonNode?)new JsonObject(keys.Select(k => KeyValuePair.Create(k, i?[k]?.DeepClone()))))])
      : null;

  private static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(Normalize(a), Normalize(b));

  private static JsonNode? Normalize(JsonNode? node) => node switch
  {
    JsonValue value when value.TryGetValue<string>(out var text) && text.Length == 0 => null,
    JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Normalize(p.Value))).Where(p => p.Value is not null)),
    JsonArray array => new JsonArray([.. array.Select(Normalize)]),
    _ => node?.DeepClone(),
  };
}
