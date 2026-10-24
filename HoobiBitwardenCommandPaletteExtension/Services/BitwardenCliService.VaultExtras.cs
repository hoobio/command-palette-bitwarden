using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HoobiBitwardenCommandPaletteExtension.Models;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed record VaultCollection(string Id, string Name, string? OrganizationId);

internal sealed record VaultExtras(List<BitwardenItem> Items, List<VaultCollection> Collections);

internal sealed partial class BitwardenCliService
{
  internal bool ShowWebsiteIcons => _settings?.ShowWebsiteIcons.Value != false;

  internal List<BitwardenItem> GetCachedItems()
  {
    lock (_cacheLock)
      return [.. _cache];
  }

  internal Dictionary<string, string> GetFolders()
  {
    lock (_cacheLock)
      return new(_folders);
  }

  internal Dictionary<string, string> GetOrganizations()
  {
    lock (_cacheLock)
      return new(_organizations);
  }

  internal async Task<VaultExtras?> LoadVaultExtrasAsync()
  {
    int generation;
    lock (_cacheLock) generation = _sessionGeneration;
    var trashTask = RunCliAsync("list items --trash");
    var archivedTask = RunCliAsync("list items --archived");
    var collectionsTask = RunCliAsync("list collections");
    await Task.WhenAll(trashTask, archivedTask, collectionsTask);

    var items = new List<BitwardenItem>();
    Keep(items, ParseItems(await trashTask), static i => i.DeletedDate != null);
    // An account that cannot archive gets the active list back from --archived.
    Keep(items, ParseItems(await archivedTask), static i => i.ArchivedDate != null);
    var collections = ParseCollections(await collectionsTask);
    lock (_cacheLock)
    {
      if (generation == _sessionGeneration)
        return new VaultExtras(items, collections);
    }

    foreach (var item in items)
      item.DisposeSecrets();
    return null;
  }

  private static void Keep(List<BitwardenItem> target, List<BitwardenItem> parsed, Func<BitwardenItem, bool> keep)
  {
    foreach (var item in parsed)
    {
      if (keep(item))
        target.Add(item);
      else
        item.DisposeSecrets();
    }
  }

  internal static List<VaultCollection> ParseCollections(string json)
  {
    var result = new List<VaultCollection>();
    try
    {
      if (JsonNode.Parse(json) is not JsonArray array)
        return result;
      foreach (var node in array)
      {
        var id = node?["id"]?.GetValue<string>();
        var name = node?["name"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
          result.Add(new VaultCollection(id, name, node?["organizationId"]?.GetValue<string>()));
      }
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Cache", $"ParseCollections failed: {ex.GetType().Name}");
    }
    return result;
  }
}
