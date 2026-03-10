using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromHours(1);

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // null value = negative cache (host has no icon); missing key = not yet tried
    private static readonly ConcurrentDictionary<string, byte[]?> _memCache = new();
    private static readonly HashSet<string> _pending = [];
    private static readonly Lock _pendingLock = new();

    /// <summary>Fired on the thread-pool when any new icon is successfully cached.</summary>
    public static event Action? IconCached;

    static FaviconService()
    {
        Directory.CreateDirectory(CacheDir);
    }

    /// <summary>
    /// Returns an <see cref="IconInfo"/> for the given host.
    /// Uses the disk cache when available; otherwise returns the fallback icon and
    /// schedules a background download.
    /// </summary>
    public static IconInfo GetOrQueue(string host, string iconUrl)
    {
        if (_memCache.TryGetValue(host, out var cachedBytes))
            return cachedBytes is not null ? MakeIconInfo(cachedBytes) : Fallback();

        var posPath = GetPositivePath(host);
        var negPath = GetNegativePath(host);

        if (File.Exists(posPath) && !IsExpired(posPath, PositiveTtl))
        {
            var bytes = File.ReadAllBytes(posPath);
            _memCache[host] = bytes;
            return MakeIconInfo(bytes);
        }

        if (File.Exists(negPath) && !IsExpired(negPath, NegativeTtl))
        {
            _memCache[host] = null;
            return Fallback();
        }

        bool shouldFetch;
        lock (_pendingLock)
            shouldFetch = _pending.Add(host);

        if (shouldFetch)
            _ = Task.Run(() => DownloadAsync(host, iconUrl));

        return Fallback();
    }

    private static async Task DownloadAsync(string host, string iconUrl)
    {
        var negPath = GetNegativePath(host);
        try
        {
            using var resp = await _http.GetAsync(iconUrl);
            if (!resp.IsSuccessStatusCode)
            {
                await NegCacheAsync(negPath);
                _memCache[host] = null;
                return;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            if (bytes.Length < 10)
            {
                await NegCacheAsync(negPath);
                _memCache[host] = null;
                return;
            }

            var posPath = GetPositivePath(host);
            await File.WriteAllBytesAsync(posPath, bytes);
            _memCache[host] = bytes;
            IconCached?.Invoke();
        }
        catch
        {
            try { await NegCacheAsync(negPath); } catch { }
            _memCache[host] = null;
        }
        finally
        {
            lock (_pendingLock)
                _pending.Remove(host);
        }
    }

    private static Task NegCacheAsync(string path) => File.WriteAllBytesAsync(path, []);

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

    private static IconInfo Fallback() => new("\uE774");

    [GeneratedRegex(@"[^\w\-\.]", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex InvalidFilenameChars();
}
