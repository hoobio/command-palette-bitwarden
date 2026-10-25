using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class DebugLogService
{
  private static readonly Lock _lock = new();
  private static readonly LinkedList<LogEntry> _entries = new();
  private const int MaxEntries = 500;
  internal const int RetentionDays = 7;
  internal const long MaxFileBytes = 10 * 1024 * 1024;
  // Dev and release builds share the settings folder, so each channel writes its own files.
#if DEBUG
  internal const string FilePrefix = "debug-dev-";
#else
  internal const string FilePrefix = "debug-";
#endif
  private static string? _openFile;

  internal static bool Enabled { get; set; }

  internal static string? LogDirectory { get; set; }

  internal static void Log(string category, string message)
  {
    var entry = new LogEntry(DateTime.UtcNow, category, message);
    lock (_lock)
    {
      _entries.AddLast(entry);
      if (_entries.Count > MaxEntries)
        _entries.RemoveFirst();
      if (Enabled && LogDirectory is { } directory)
        WriteToDisk(directory, entry);
    }
  }

  private static void WriteToDisk(string directory, LogEntry entry)
  {
    try
    {
      var path = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{FilePrefix}{entry.Timestamp:yyyyMMdd}.log"));
      if (path != _openFile)
      {
        Directory.CreateDirectory(directory);
        Purge(directory, entry.Timestamp);
        _openFile = path;
      }

      var file = new FileInfo(path);
      if (file.Exists && file.Length >= MaxFileBytes)
        return;
      File.AppendAllText(path, Format(entry) + Environment.NewLine);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      LogDirectory = null;
      _openFile = null;
      _entries.AddLast(new LogEntry(DateTime.UtcNow, "Log", $"Writing the debug log to disk stopped: {ex.GetType().Name}"));
    }
  }

  internal static void Purge(string directory, DateTime nowUtc)
  {
    foreach (var file in Directory.EnumerateFiles(directory, FilePrefix + "*.log"))
    {
      if (nowUtc - File.GetLastWriteTimeUtc(file) > TimeSpan.FromDays(RetentionDays))
        File.Delete(file);
    }
  }

  private static string Format(LogEntry entry) =>
    string.Create(CultureInfo.InvariantCulture, $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Category}] {entry.Message}");

  internal static string Export()
  {
    lock (_lock)
    {
      if (_entries.Count == 0) return "(no log entries)";

      var sb = new StringBuilder();
      sb.AppendLine(CultureInfo.InvariantCulture, $"Debug log ({_entries.Count} entries)");
      sb.AppendLine(new string('-', 60));

      foreach (var entry in _entries)
        sb.AppendLine(Format(entry));

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
