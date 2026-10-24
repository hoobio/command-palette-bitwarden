using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed record VaultCollection(string Id, string Name, string? OrganizationId);

internal sealed partial class BitwardenCliService
{
  internal bool ShowWebsiteIcons => _settings?.ShowWebsiteIcons.Value != false;

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

  internal async Task<List<VaultCollection>?> LoadCollectionsAsync()
  {
    int generation;
    lock (_cacheLock) generation = _sessionGeneration;
    var collections = ParseCollections(await RunCliAsync("list collections"));
    lock (_cacheLock)
      return generation == _sessionGeneration ? collections : null;
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
