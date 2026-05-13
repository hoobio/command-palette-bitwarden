using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Storage.Streams;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static partial class FaviconService
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HoobiBitwardenCommandPalette", "Icons");

    private static readonly TimeSpan PositiveTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(5);

    // Bounded concurrency: 8 in-flight downloads saturates a typical home/office
    // connection without overwhelming the icons host or HttpClient's default per-host
    // connection limit. Higher values get head-of-line-blocked by the server anyway
    // and increase the chance of timeouts on a cold-vault load.
    private const int MaxConcurrentDownloads = 8;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Cached icon: raster bytes streamed via COM. null = negative cache (host has no icon); missing key = not yet tried
    private static readonly ConcurrentDictionary<string, byte[]?> _memCache = new();
    private static readonly ConcurrentDictionary<string, IconInfo> _iconInfoCache = new();

    // Priority queue gated by a worker pool of size MaxConcurrentDownloads.
    // Lower Priority value = downloaded sooner. Callers pass the item's index in the
    // visible list so head-of-list icons load before tail. When a host is re-enqueued
    // with a better priority (e.g. it now appears earlier in a new search result),
    // the old heap entry stays put as a tombstone and is dropped at dequeue time
    // because its priority no longer matches the current value in _queuedHosts.
    private static readonly Lock _queueLock = new();
    private static readonly PriorityQueue<string, int> _queue = new();
    private static readonly Dictionary<string, (string IconUrl, int Priority)> _queuedHosts = [];
    private static readonly HashSet<string> _inFlight = [];
    private static readonly SemaphoreSlim _signal = new(0);
    private static int _workersInitialized;

    /// <summary>Fired on the thread-pool when any new icon is successfully cached.</summary>
    public static event Action? IconCached;

    static FaviconService()
    {
        Directory.CreateDirectory(CacheDir);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"HoobiBitwarden/{version}");
    }

    /// <summary>
    /// Returns an <see cref="IconInfo"/> for the given host. Uses the disk cache when
    /// available; otherwise returns the fallback icon and schedules a bounded-concurrency
    /// background download. <paramref name="priority"/> orders pending downloads
    /// (lower = sooner). Default <see cref="int.MaxValue"/> sends the host to the tail.
    /// </summary>
    public static IconInfo GetOrQueue(string host, string iconUrl, IconInfo? fallback = null, int priority = int.MaxValue)
    {
        fallback ??= new IconInfo("");
        if (_iconInfoCache.TryGetValue(host, out var cachedIcon))
            return cachedIcon;
        if (_memCache.TryGetValue(host, out var cachedBytes))
            return cachedBytes is not null ? CacheAndReturnIcon(host, cachedBytes) : fallback;

        var posPath = GetPositivePath(host);
        var negPath = GetNegativePath(host);

        if (File.Exists(posPath) && !IsExpired(posPath, PositiveTtl))
        {
            var bytes = File.ReadAllBytes(posPath);
            _memCache[host] = bytes;
            return CacheAndReturnIcon(host, bytes);
        }

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
                // Stale entry from a priority bump; skip and try the next.
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

            await File.WriteAllBytesAsync(GetPositivePath(host), pngBytes);
            _memCache[host] = pngBytes;
            CacheAndReturnIcon(host, pngBytes);

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

    private static string GetPositivePath(string host) =>
        Path.Combine(CacheDir, $"{Sanitize(host)}.png");

    private static string GetNegativePath(string host) =>
        Path.Combine(CacheDir, $"{Sanitize(host)}.miss");

    private static string Sanitize(string host) =>
        InvalidFilenameChars().Replace(host, "_");

    public static void ClearMemCache()
    {
        _iconInfoCache.Clear();
        _memCache.Clear();
    }

    private static IconInfo CacheAndReturnIcon(string host, byte[] bytes)
    {
        return _iconInfoCache.GetOrAdd(host, _ => MakeIconInfo(bytes));
    }

    // Serve icon bytes via IRandomAccessStreamReference so the data is streamed
    // back through the extension process — works correctly across the COM process boundary.
    private static IconInfo MakeIconInfo(byte[] bytes)
    {
        var ras = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(ras.GetOutputStreamAt(0));
        writer.WriteBytes(bytes);
#pragma warning disable VSTHRD002 // In-memory stream write completes synchronously
        writer.StoreAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        writer.DetachStream();
        return IconInfo.FromStream(ras);
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
