using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCompanionIpc;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Vault read/write/generate operations driven by the extension (the CLI/session authority).
// The companion WinUI process sends intents over IPC; these methods execute them through the
// same `bw` boundary the rest of the service uses. Save = persist + verify + refresh (3.6):
// never report success until the value is confirmed present server-side, because the user will
// immediately reuse a rotated secret downstream and a silent sync failure would lose it.
internal sealed partial class BitwardenCliService
{
  internal readonly record struct SaveResult(bool Success, string? Error, BitwardenItem? Item);

  // Raw decrypted item JSON straight from `bw get item <id>`. The companion edits this object
  // and sends it back to SaveItemAsync, so the round-trip preserves fields we don't model.
  public async Task<string?> GetItemRawAsync(string id)
  {
    if (string.IsNullOrWhiteSpace(id)) return null;

    // Serve from the in-memory vault cache when we have it - the companion gets the item instantly
    // instead of waiting ~1s for a `bw get item` Node spawn. Falls back to the CLI on a cache miss
    // (e.g. an item added since the last sync).
    var cached = GetCachedRawJson(id);
    if (cached != null)
    {
      DebugLogService.Log("Companion", $"GetItemRaw served from cache for {id}");
      return cached;
    }

    DebugLogService.Log("Companion", $"GetItemRaw cache miss for {id}; spawning bw get item");
    return await GetItemRawFromCliAsync(id);
  }

  // Authoritative read straight from the CLI, bypassing the cache. Used by the save verify step
  // (§3.6), which must confirm the value landed on the SERVER and can't trust a possibly-stale cache.
  private async Task<string?> GetItemRawFromCliAsync(string id)
  {
    if (string.IsNullOrWhiteSpace(id)) return null;
    var json = await RunCliAsync($"get item {id}");
    return LooksLikeItemJson(json) ? json : null;
  }

  private string? GetCachedRawJson(string id)
  {
    lock (_cacheLock)
    {
      foreach (var item in _cache)
        if (item.Id == id) return item.RawJson;
    }
    return null;
  }

  // Collections for an organization (id -> name), for the companion's org-item collection picker.
  public async Task<Dictionary<string, string>> ListCollectionsAsync(string organizationId)
  {
    var result = new Dictionary<string, string>();
    if (string.IsNullOrWhiteSpace(organizationId)) return result;

    try
    {
      var json = await RunCliAsync($"list collections --organizationid {organizationId}");
      if (JsonNode.Parse(json) is JsonArray array)
      {
        foreach (var node in array)
        {
          var cid = node?["id"]?.GetValue<string>();
          var name = node?["name"]?.GetValue<string>();
          if (!string.IsNullOrEmpty(cid) && name != null) result[cid] = name;
        }
      }
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Companion", $"ListCollections failed: {ex.GetType().Name}: {ex.Message}");
    }

    return result;
  }

  // Parsed item for display. Reuses the same parser the cache uses.
  public async Task<BitwardenItem?> GetItemAsync(string id)
  {
    var json = await GetItemRawAsync(id);
    return json == null ? null : ParseSingleItem(json);
  }

  // Applies an edited item via `bw edit item <id> <base64-json>`. The CLI accepts the encoded
  // JSON as a positional arg (equivalent to `bw encode | bw edit item <id>`). Returns the raw
  // JSON the CLI echoes back on success, or null on failure.
  public async Task<string?> EditItemAsync(string id, string rawItemJson)
  {
    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(rawItemJson))
      return null;

    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawItemJson));
    var result = await RunCliAsync($"edit item {id} {encoded}");
    return LooksLikeItemJson(result) ? result : null;
  }

  // The 3.6 contract: edit -> sync -> re-fetch -> verify -> refresh palette. `mustContainValues`
  // are plaintext values (e.g. a freshly generated password) that MUST be present in the
  // re-fetched item before we report success; if any is missing the save is treated as failed.
  public async Task<SaveResult> SaveItemAsync(string id, string rawItemJson, IReadOnlyList<string>? mustContainValues = null)
  {
    string? edited;
    try
    {
      edited = await EditItemAsync(id, rawItemJson);
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Edit", $"EditItemAsync threw: {ex.GetType().Name}: {ex.Message}");
      return new SaveResult(false, $"Failed to save item: {ex.Message}", null);
    }

    if (edited == null)
      return new SaveResult(false, "The Bitwarden CLI rejected the edit.", null);

    // Push to the server. Do not trust the sync exit code alone (step 3 below).
    try
    {
      await RunCliAsync("sync", CliTimeoutMs, "Syncing complete.");
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Edit", $"Sync after edit failed: {ex.GetType().Name}: {ex.Message}");
      return new SaveResult(false, $"Saved locally but the sync to the server failed: {ex.Message}. The change may not be on the server yet.", null);
    }

    // Verify persistence: re-fetch from the CLI (NOT the cache) and confirm the intended value(s)
    // actually landed on the server.
    string? verifyJson;
    try
    {
      verifyJson = await GetItemRawFromCliAsync(id);
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Edit", $"Verify re-fetch failed: {ex.GetType().Name}: {ex.Message}");
      return new SaveResult(false, $"Could not verify the save reached the server: {ex.Message}", null);
    }

    if (verifyJson == null)
      return new SaveResult(false, "Could not verify the save: the item could not be re-fetched.", null);

    if (mustContainValues != null)
    {
      foreach (var value in mustContainValues)
      {
        if (string.IsNullOrEmpty(value)) continue;
        if (!verifyJson.Contains(value, StringComparison.Ordinal))
        {
          DebugLogService.Log("Edit", "Verify failed: expected value not present in re-fetched item.");
          return new SaveResult(false, "Verification failed: the new value is not present on the server. Do NOT assume it was saved.", null);
        }
      }
    }

    // Live palette update - the extension made the change, so it owns the refresh.
    try { await RefreshCacheAsync(); }
    catch (Exception ex) { DebugLogService.Log("Edit", $"RefreshCacheAsync after save failed: {ex.GetType().Name}: {ex.Message}"); }

    return new SaveResult(true, null, ParseSingleItem(verifyJson));
  }

  // Runs `bw generate` with the given options and returns the generated value. Generation is
  // local to the CLI and needs no session, so this works whether or not the vault is unlocked.
  public async Task<string?> GenerateAsync(GeneratorOptions options)
  {
    var args = BuildArgString(options.ToCliArgs());
    var output = (await RunCliAsync(args)).Trim();
    return string.IsNullOrEmpty(output) ? null : output;
  }

  // Forwarder to the shared mutation (Shared/VaultSecretMutations.cs) so the extension and companion
  // target the rotate secret identically.
  internal static bool TrySetSingleHiddenSecret(JsonObject item, string newValue, out string? error)
    => VaultSecretMutations.TrySetSingleHiddenSecret(item, newValue, out error);

  internal static BitwardenItem? ParseSingleItem(string json)
  {
    try { return BitwardenItemParser.TryParseItemNode(JsonNode.Parse(json)); }
    catch { return null; }
  }

  private static bool LooksLikeItemJson(string? json)
  {
    if (string.IsNullOrWhiteSpace(json)) return false;
    var trimmed = json.TrimStart();
    return trimmed.StartsWith('{');
  }

  internal static string BuildArgString(IReadOnlyList<string> tokens) =>
    string.Join(' ', tokens.Select(QuoteArgIfNeeded));

  // Windows command-line quoting (CommandLineToArgvW rules). Most bw args are GUID/base64/flag
  // tokens that need none of this; the separator value in passphrase mode is the case that does.
  internal static string QuoteArgIfNeeded(string arg)
  {
    if (arg.Length > 0 && !arg.Any(c => c is ' ' or '\t' or '\n' or '\v' or '"'))
      return arg;

    var sb = new StringBuilder();
    sb.Append('"');
    var backslashes = 0;
    foreach (var c in arg)
    {
      if (c == '\\')
      {
        backslashes++;
        continue;
      }
      if (c == '"')
      {
        sb.Append('\\', backslashes * 2 + 1);
        backslashes = 0;
        sb.Append('"');
        continue;
      }
      sb.Append('\\', backslashes);
      backslashes = 0;
      sb.Append(c);
    }
    sb.Append('\\', backslashes * 2);
    sb.Append('"');
    return sb.ToString();
  }
}
