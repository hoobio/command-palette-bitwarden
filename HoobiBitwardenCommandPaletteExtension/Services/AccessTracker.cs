using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class AccessTracker
{
  private static readonly string FilePath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "HoobiBitwardenCommandPalette", "access.json");

  private static Dictionary<string, DateTime>? _data;
  private static string? _lastCopiedId;
  private static Timer? _recentClearTimer;
  private static readonly Lock _lock = new();
  private static readonly TimeSpan RecentExpiry = TimeSpan.FromMinutes(5);

  public static event Action? ItemAccessed;

  public static void Record(string itemId)
  {
    lock (_lock)
    {
      _lastCopiedId = itemId;
      _recentClearTimer?.Dispose();
      _recentClearTimer = new Timer(ClearRecent, null, RecentExpiry, Timeout.InfiniteTimeSpan);
      var data = Load();
      data[itemId] = DateTime.UtcNow;
      Save(data);
    }
    ItemAccessed?.Invoke();
  }

  public static bool IsLastCopied(string itemId)
  {
    lock (_lock)
      return _lastCopiedId != null && _lastCopiedId == itemId;
  }

  private static void ClearRecent(object? state)
  {
    lock (_lock)
    {
      _lastCopiedId = null;
      _recentClearTimer?.Dispose();
      _recentClearTimer = null;
    }
    ItemAccessed?.Invoke();
  }

  public static DateTime GetLastAccess(string itemId)
  {
    lock (_lock)
      return Load().TryGetValue(itemId, out var dt) ? dt : DateTime.MinValue;
  }

  private static Dictionary<string, DateTime> Load()
  {
    if (_data != null)
      return _data;

    try
    {
      if (File.Exists(FilePath))
      {
        var json = File.ReadAllText(FilePath);
        _data = JsonSerializer.Deserialize(json, AccessJsonContext.Default.DictionaryStringDateTime) ?? [];
        return _data;
      }
    }
    catch (Exception ex)
    {
      DebugLogService.Log("AccessTracker", $"Load failed: {ex.GetType().Name}: {ex.Message}");
    }

    _data = [];
    return _data;
  }

  private static void Save(Dictionary<string, DateTime> data)
  {
    try
    {
      if (data.Count > 500)
        _data = data = new Dictionary<string, DateTime>(data.OrderByDescending(kvp => kvp.Value).Take(500));

      Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
      File.WriteAllText(FilePath, JsonSerializer.Serialize(data, AccessJsonContext.Default.DictionaryStringDateTime));
    }
    catch (Exception ex)
    {
      DebugLogService.Log("AccessTracker", $"Save failed: {ex.GetType().Name}: {ex.Message}");
    }
  }
}

[JsonSerializable(typeof(Dictionary<string, DateTime>))]
internal sealed partial class AccessJsonContext : JsonSerializerContext;

