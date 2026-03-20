using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class DebugLogService
{
  private static readonly Lock _lock = new();
  private static readonly LinkedList<LogEntry> _entries = new();
  private const int MaxEntries = 500;

  internal static bool Enabled { get; set; }

  internal static void Log(string category, string message)
  {
    if (!Enabled) return;

    var entry = new LogEntry(DateTime.UtcNow, category, message);
    lock (_lock)
    {
      _entries.AddLast(entry);
      if (_entries.Count > MaxEntries)
        _entries.RemoveFirst();
    }
  }

  internal static string Export()
  {
    lock (_lock)
    {
      if (_entries.Count == 0) return "(no log entries)";

      var sb = new StringBuilder();
      sb.AppendLine(CultureInfo.InvariantCulture, $"Debug log ({_entries.Count} entries)");
      sb.AppendLine(new string('-', 60));

      foreach (var entry in _entries)
        sb.AppendLine(CultureInfo.InvariantCulture, $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Category}] {entry.Message}");

      return sb.ToString();
    }
  }

  internal static void Clear()
  {
    lock (_lock) _entries.Clear();
  }

  internal static int Count
  {
    get { lock (_lock) return _entries.Count; }
  }

  internal readonly record struct LogEntry(DateTime Timestamp, string Category, string Message);
}
