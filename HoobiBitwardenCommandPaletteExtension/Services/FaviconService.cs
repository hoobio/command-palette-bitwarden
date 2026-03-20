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

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Cached icon: raster bytes streamed via COM. null = negative cache (host has no icon); missing key = not yet tried
    private static readonly ConcurrentDictionary<string, byte[]?> _memCache = new();
    private static readonly HashSet<string> _pending = [];
    private static readonly Lock _pendingLock = new();

    /// <summary>Fired on the thread-pool when any new icon is successfully cached.</summary>
    public static event Action? IconCached;

    static FaviconService()
    {
        Directory.CreateDirectory(CacheDir);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"HoobiBitwarden/{version}");
    }

    /// <summary>
    /// Returns an <see cref="IconInfo"/> for the given host.
    /// Uses the disk cache when available; otherwise returns the fallback icon and
    /// schedules a background download.
    /// </summary>
    public static IconInfo GetOrQueue(string host, string iconUrl, IconInfo? fallback = null)
    {
        fallback ??= new IconInfo("\uE774");
        if (_memCache.TryGetValue(host, out var cachedBytes))
            return cachedBytes is not null ? MakeIconInfo(cachedBytes) : fallback;

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
            return fallback;
        }

        bool shouldFetch;
        lock (_pendingLock)
            shouldFetch = _pending.Add(host);

        if (shouldFetch)
            _ = Task.Run(() => DownloadAsync(host, iconUrl));

        return fallback;
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

            try { File.Delete(negPath); } catch { }
            IconCached?.Invoke();
        }
        catch (Exception ex)
        {
            try { await NegCacheAsync(negPath, $"{iconUrl}\nException: {ex.GetType().Name}: {ex.Message}"); } catch { }
            _memCache[host] = null;
        }
        finally
        {
            lock (_pendingLock)
                _pending.Remove(host);
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
