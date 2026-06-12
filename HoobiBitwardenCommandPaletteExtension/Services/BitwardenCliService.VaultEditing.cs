using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HoobiBitwardenCommandPaletteExtension.Models;

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
    var json = await RunCliAsync($"get item {id}");
    return LooksLikeItemJson(json) ? json : null;
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

    // Verify persistence: re-fetch and confirm the intended value(s) actually landed.
    string? verifyJson;
    try
    {
      verifyJson = await GetItemRawAsync(id);
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

  // Quick Rotate targets an item with a SINGLE secret/hidden field (the common case: one login
  // password, or one hidden custom field). Sets that field to newValue in-place. Returns false with
  // a reason when there is no secret, or more than one (ambiguous - the user should edit explicitly).
  internal static bool TrySetSingleHiddenSecret(JsonObject item, string newValue, out string? error)
  {
    error = null;
    var login = item["login"]?.AsObject();
    var hasLoginPassword = login != null && !string.IsNullOrEmpty(login["password"]?.GetValue<string>());

    var hiddenFields = new List<JsonObject>();
    if (item["fields"] is JsonArray fields)
    {
      foreach (var f in fields)
      {
        if (f is JsonObject fo && (fo["type"]?.GetValue<int>() ?? 0) == 1)
          hiddenFields.Add(fo);
      }
    }

    var candidateCount = (hasLoginPassword ? 1 : 0) + hiddenFields.Count;
    if (candidateCount == 0)
    {
      error = "This item has no password or hidden field to rotate.";
      return false;
    }
    if (candidateCount > 1)
    {
      error = "This item has more than one secret field. Open it and rotate the field you want.";
      return false;
    }

    if (hasLoginPassword)
      login!["password"] = newValue;
    else
      hiddenFields[0]["value"] = newValue;

    return true;
  }

  internal static BitwardenItem? ParseSingleItem(string json)
  {
    try { return TryParseItemNode(JsonNode.Parse(json)); }
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
