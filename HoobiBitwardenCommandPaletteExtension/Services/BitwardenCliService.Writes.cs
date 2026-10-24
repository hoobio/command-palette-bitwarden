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

internal enum VaultWriteError { Failed, NotFound, Conflict, Unknown, Premium, TooLarge, NoStorage }

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

  internal int WriteTimeoutMs { get; set; } = 300_000;

  internal static bool IsDataWrite(string args) =>
    args == "sync" || IsItemWrite(args);

  private static bool IsItemWrite(string args) => WriteVerbs.Any(v => args.StartsWith(v, StringComparison.Ordinal));

  internal async Task<JsonObject?> GetItemJsonAsync(string id) =>
    ParseObject(await RunCliAsync($"get item {RequireId(id)}", verbOnlyLog: true));

  internal Task<JsonObject> EditItemAsync(string id, DateTime expectedRevision, Func<JsonObject, JsonObject> build)
  {
    JsonObject? sent = null;
    return WriteAsync(id, expectedRevision, current =>
    {
      sent = build(current!);
      return RunWriteAsync($"edit item {id}", Encode(sent));
    }, (_, saved) => ContentMatches(sent!, saved));
  }

  internal Task<JsonObject> DeleteItemAsync(string id, DateTime expectedRevision, bool permanent) =>
    WriteAsync(id, expectedRevision, _ => RunWriteAsync(permanent ? $"delete item {id} --permanent" : $"delete item {id}"),
      (_, saved) => saved?["deletedDate"] is not null, gone: permanent);

  internal Task<JsonObject> ArchiveItemAsync(string id, DateTime expectedRevision) =>
    WriteAsync(id, expectedRevision, _ => RunWriteAsync($"archive item {id}"), (_, saved) => saved?["archivedDate"] is not null);

  internal Task<JsonObject> MoveItemAsync(string id, DateTime expectedRevision, string organizationId, IReadOnlyList<string> collectionIds) =>
    WriteAsync(id, expectedRevision, _ => RunWriteAsync($"move {id} {RequireId(organizationId)}", EncodeIds(collectionIds)),
      (_, saved) => string.Equals(saved?["organizationId"]?.GetValue<string>(), organizationId, StringComparison.OrdinalIgnoreCase) && SameIds(saved, collectionIds));

  internal Task<JsonObject> SetCollectionsAsync(string id, DateTime expectedRevision, IReadOnlyList<string> collectionIds) =>
    WriteAsync(id, expectedRevision, _ => RunWriteAsync($"edit item-collections {id}", EncodeIds(collectionIds)), (_, saved) => SameIds(saved, collectionIds));

  private Task<string> RunWriteAsync(string args, string? stdin = null) => RunCliAsync(args, WriteTimeoutMs, stdin: stdin, verbOnlyLog: true);

  private async Task<JsonObject> WriteAsync(string id, DateTime expectedRevision, Func<JsonObject?, Task<string>> write, Func<JsonObject?, JsonObject?, bool> verify, bool gone = false)
  {
    RequireId(id);
    await _writeGate.WaitAsync();
    try
    {
      int generation;
      lock (_cacheLock) generation = _sessionGeneration;
      var current = await GetItemJsonAsync(id) ?? throw new VaultWriteException(VaultWriteError.NotFound);
      if (RevisionOf(current) != expectedRevision)
      {
        UpdateCachedItem(id, current, generation);
        throw new VaultWriteException(VaultWriteError.Conflict);
      }

      string output;
      var timedOut = false;
      try
      {
        output = await write(current);
      }
      catch (TimeoutException)
      {
        DebugLogService.Log("CLI", "Write outcome unknown: bw did not answer in time");
        timedOut = true;
        output = string.Empty;
      }

      if (ParseObject(output) is null && output.Contains("out of date", StringComparison.OrdinalIgnoreCase))
      {
        await RunCliAsync("sync", CliTimeoutMs, "Syncing complete.");
        UpdateCachedItem(id, await GetItemJsonAsync(id), generation);
        throw new VaultWriteException(VaultWriteError.Conflict);
      }

      // The write process holds the sync gate until it exits, and bw flushes data.json after printing its result.
      await _syncGate.WaitAsync();
      _syncGate.Release();

      if (timedOut)
        await RunCliAsync("sync", CliTimeoutMs, "Syncing complete.");

      var savedOutput = await RunCliAsync($"get item {id}", verbOnlyLog: true);
      var saved = ParseObject(savedOutput);
      if (gone ? saved is not null || !savedOutput.Contains("Not found", StringComparison.OrdinalIgnoreCase) : !verify(current, saved))
      {
        DebugLogService.Log("CLI", "Write verification failed");
        throw new VaultWriteException(timedOut ? VaultWriteError.Unknown : VaultWriteError.Failed);
      }

      UpdateCachedItem(id, saved, generation);
      return saved ?? new JsonObject();
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
