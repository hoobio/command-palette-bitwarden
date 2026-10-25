using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static partial class FaviconService
{
    internal static string CacheDir { get; set; } = ResolveCacheDir();

    private static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(5);

    private const int MaxConcurrentDownloads = 8;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // null value = negative cache (host has no icon); missing key = not yet tried
    private static readonly ConcurrentDictionary<string, IconInfo?> _memCache = new();

    // A re-enqueued host leaves its old heap entry as a tombstone, dropped at dequeue when its priority no longer matches _queuedHosts.
    private static readonly Lock _queueLock = new();
    private static readonly PriorityQueue<string, int> _queue = new();
    private static readonly Dictionary<string, (string IconUrl, int Priority)> _queuedHosts = [];
    private static readonly HashSet<string> _inFlight = [];
    private static readonly SemaphoreSlim _signal = new(0);
    private static int _workersInitialized;

    public static event Action? IconCached;

    static FaviconService()
    {
        Directory.CreateDirectory(CacheDir);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"HoobiBitwarden/{version}");
    }

    public static IconInfo GetOrQueue(string host, string iconUrl, IconInfo? fallback = null, int priority = int.MaxValue)
    {
        fallback ??= new IconInfo("");
        var posPath = GetPositivePath(host);
        var negPath = GetNegativePath(host);

        if (_memCache.TryGetValue(host, out var cachedIcon))
        {
            if (cachedIcon is not null)
                return cachedIcon;
            if (File.Exists(negPath) && !IsExpired(negPath, NegativeTtl))
                return fallback;
            _memCache.TryRemove(host, out _);
        }

        if (File.Exists(posPath) && !IsExpired(posPath, PositiveTtl))
            return _memCache.GetOrAdd(host, _ => new IconInfo(posPath)) ?? fallback;

        if (File.Exists(negPath) && !IsExpired(negPath, NegativeTtl))
        {
            _memCache[host] = null;
            return fallback;
        }

        Enqueue(host, iconUrl, priority);
        return fallback;
    }

    private static void Enqueue(string host, string iconUrl, int priority)
    {
        bool needsSignal;
        lock (_queueLock)
        {
            if (_inFlight.Contains(host)) return;

            if (_queuedHosts.TryGetValue(host, out var existing) && priority >= existing.Priority)
                return;

            _queuedHosts[host] = (iconUrl, priority);
            _queue.Enqueue(host, priority);
            needsSignal = true;
        }
        if (needsSignal)
        {
            EnsureWorkersStarted();
            _signal.Release();
        }
    }

    private static void EnsureWorkersStarted()
    {
        if (Interlocked.Exchange(ref _workersInitialized, 1) != 0) return;
        for (var i = 0; i < MaxConcurrentDownloads; i++)
            _ = Task.Run(WorkerLoopAsync);
    }

    private static async Task WorkerLoopAsync()
    {
        while (true)
        {
            await _signal.WaitAsync().ConfigureAwait(false);
            var job = TryDequeueJob();
            if (job is null) continue;
            var (host, iconUrl) = job.Value;
            try { await DownloadAsync(host, iconUrl).ConfigureAwait(false); }
            finally
            {
                lock (_queueLock) _inFlight.Remove(host);
            }
        }
    }

    private static (string Host, string IconUrl)? TryDequeueJob()
    {
        lock (_queueLock)
        {
            while (_queue.TryDequeue(out var host, out var priority))
            {
                if (_queuedHosts.TryGetValue(host, out var entry) && entry.Priority == priority)
                {
                    _queuedHosts.Remove(host);
                    _inFlight.Add(host);
                    return (host, entry.IconUrl);
                }
            }
            return null;
        }
    }

    private static async Task DownloadAsync(string host, string iconUrl)
    {
        var negPath = GetNegativePath(host);
        try
        {
            using var resp = await _http.GetAsync(iconUrl);
            if (!resp.IsSuccessStatusCode)
            {
                await NegCacheAsync(negPath, $"{iconUrl}\nHTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                _memCache[host] = null;
                return;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 10)
            {
                await NegCacheAsync(negPath, $"{iconUrl}\nEmpty response ({bytes.Length} bytes)");
                _memCache[host] = null;
                return;
            }

            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
            byte[] pngBytes;
            if (IsSvg(contentType, bytes))
            {
                var rasterized = SvgRasterizer.TryRasterize(bytes);
                if (rasterized is null)
                {
                    await NegCacheAsync(negPath, $"{iconUrl}\nSVG rasterization failed");
                    _memCache[host] = null;
                    return;
                }

                pngBytes = rasterized;
            }
            else
            {
                pngBytes = bytes;
            }

            var posPath = GetPositivePath(host);
            await File.WriteAllBytesAsync(posPath, pngBytes);
            _memCache[host] = new IconInfo(posPath);

            try { File.Delete(negPath); } catch { }
            IconCached?.Invoke();
        }
        catch (Exception ex)
        {
            try { await NegCacheAsync(negPath, $"{iconUrl}\nException: {ex.GetType().Name}: {ex.Message}"); } catch { }
            _memCache[host] = null;
        }
    }

    private static Task NegCacheAsync(string path, string? reason = null) =>
        File.WriteAllTextAsync(path, reason ?? "");

    private static bool IsExpired(string path, TimeSpan ttl)
    {
        try { return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > ttl; }
        catch { return true; }
    }

    internal static string? TryGetCachedIconPath(string host) =>
        GetPositivePath(host) is var path && File.Exists(path) ? path : null;

    private static string GetPositivePath(string host) =>
        Path.Combine(CacheDir, $"{Sanitize(host)}.png");

    private static string GetNegativePath(string host) =>
        Path.Combine(CacheDir, $"{Sanitize(host)}.miss");

    private static string Sanitize(string host) =>
        InvalidFilenameChars().Replace(host, "_");

    public static void ClearMemCache() => _memCache.Clear();

    public static void ClearDiskCache()
    {
        try
        {
            if (Directory.Exists(CacheDir))
                Directory.Delete(CacheDir, recursive: true);
            Directory.CreateDirectory(CacheDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLogService.Log("Favicon", $"ClearDiskCache failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // MSIX redirects new AppData writes to a per-package location that the Command Palette process cannot see, so a path icon must name the real package folder.
    private static string ResolveCacheDir()
    {
        try
        {
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path, "Icons");
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HoobiBitwardenCommandPalette", "Icons");
        }
    }

    internal static bool IsSvg(string contentType, byte[] bytes)
    {
        if (contentType.Contains("svg", StringComparison.OrdinalIgnoreCase))
            return true;
        var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256));
        return head.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"[^\w\-\.]", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex InvalidFilenameChars();
}
