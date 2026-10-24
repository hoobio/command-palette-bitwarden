using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed partial class BitwardenCliService
{
  internal Task<JsonObject> CreateFolderAsync(string name) =>
    WriteAsync(null, null, _ => RunWriteAsync("create folder", Encode(new JsonObject { ["name"] = name })),
      (_, saved) => NameOf(saved) == name, noun: "folder", commit: UpdateCachedFolder);

  internal Task<JsonObject> RenameFolderAsync(string id, string name) =>
    WriteAsync(id, null, _ => RunWriteAsync($"edit folder {id}", Encode(new JsonObject { ["name"] = name })),
      (_, saved) => NameOf(saved) == name, noun: "folder", commit: UpdateCachedFolder);

  internal async Task DeleteFolderAsync(string id)
  {
    await WriteAsync(id, null, _ => RunWriteAsync($"delete folder {id}"), (_, _) => true, gone: true, noun: "folder", commit: UpdateCachedFolder);
    // bw moves the folder's items to No folder in data.json, so the cached items still carry the deleted folder id until a reload.
    try { await RefreshCacheAsync(); }
    catch (Exception ex) { DebugLogService.Log("CLI", $"Refresh after a folder delete failed: {ex.GetType().Name}"); }
  }

  private void UpdateCachedFolder(string id, JsonObject? saved, int generation)
  {
    lock (_cacheLock)
    {
      if (generation != _sessionGeneration)
        return;
      var folders = new Dictionary<string, string>(_folders);
      if (NameOf(saved) is { } name)
        folders[id] = name;
      else
        folders.Remove(id);
      _folders = folders;
      Interlocked.Increment(ref _writeVersion);
    }

    CacheUpdated?.Invoke();
  }

  private static string? NameOf(JsonObject? node) => node?["name"] is JsonValue value && value.TryGetValue<string>(out var name) ? name : null;
}
