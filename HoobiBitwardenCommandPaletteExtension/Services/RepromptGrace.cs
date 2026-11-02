using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class RepromptGrace
{
  internal static int GracePeriodSeconds { get; set; } = 60;

  private static readonly ConcurrentDictionary<string, long> _verifiedItems = new();
  private static int _failureCount;
  private static long _cooldownUntilTicks;
  private const int MaxFailuresBeforeCooldown = 5;
  private const int CooldownSeconds = 30;

  internal static event Action? GraceStarted;

  internal static bool IsWithinGracePeriod(string itemId)
  {
    if (GracePeriodSeconds <= 0 || string.IsNullOrEmpty(itemId)) return false;
    if (!_verifiedItems.TryGetValue(itemId, out var ts)) return false;
    if (Stopwatch.GetElapsedTime(ts).TotalSeconds < GracePeriodSeconds)
      return true;

    _verifiedItems.TryRemove(itemId, out _);
    return false;
  }

  internal static GraceDto? GetGrace(string itemId)
  {
    if (!IsWithinGracePeriod(itemId) || !_verifiedItems.TryGetValue(itemId, out var ts)) return null;
    var remaining = TimeSpan.FromSeconds(GracePeriodSeconds) - Stopwatch.GetElapsedTime(ts);
    return new GraceDto(DateTime.UtcNow + remaining, GracePeriodSeconds);
  }

  internal static void RecordVerification(string itemId)
  {
    if (string.IsNullOrEmpty(itemId)) return;
    _verifiedItems[itemId] = Stopwatch.GetTimestamp();
    Interlocked.Exchange(ref _failureCount, 0);
    GraceStarted?.Invoke();
  }

  internal static void ClearGracePeriod()
  {
    _verifiedItems.Clear();
    Interlocked.Exchange(ref _failureCount, 0);
    Interlocked.Exchange(ref _cooldownUntilTicks, 0);
  }

  internal static int GetCooldownSecondsRemaining()
  {
    var ticks = Interlocked.Read(ref _cooldownUntilTicks);
    if (ticks == 0) return 0;
    var remaining = (new DateTime(ticks, DateTimeKind.Utc) - DateTime.UtcNow).TotalSeconds;
    return remaining > 0 ? (int)Math.Ceiling(remaining) : 0;
  }

  internal static void RecordFailure()
  {
    var failures = Interlocked.Increment(ref _failureCount);
    if (failures >= MaxFailuresBeforeCooldown)
      Interlocked.Exchange(ref _cooldownUntilTicks, DateTime.UtcNow.AddSeconds(CooldownSeconds).Ticks);
  }
}
