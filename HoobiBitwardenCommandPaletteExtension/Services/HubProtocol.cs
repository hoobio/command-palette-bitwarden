using System;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal enum HubFrameKind { Other, VaultChanged, Close }

internal static class HubProtocol
{
    // Vaultwarden accepts only the MessagePack hub protocol; the Bitwarden server accepts it too.
    public const string Handshake = "{\"protocol\":\"messagepack\",\"version\":1}\u001e";
    public static ReadOnlyMemory<byte> PingFrame { get; } = new byte[] { 0x02, 0x91, 0x06 };
    public const int MaxFrameSize = 1024 * 1024;

    private static readonly HashSet<long> VaultChangeTypes = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 17, 18, 19];

    public static string CliDataFile(string? dataDirectory) => Path.Combine(
        dataDirectory
            ?? (Environment.GetEnvironmentVariable("BITWARDENCLI_APPDATA_DIR") is { Length: > 0 } inherited
                ? inherited
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bitwarden CLI")),
        "data.json");

    public static (Uri HubUri, string AccessToken)? ReadConnectionInfo(string dataFile)
    {
        using var stream = new FileStream(dataFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (StringProperty(root, "global_account_activeAccountId") is not { } userId
            || StringProperty(root, $"user_{userId}_token_accessToken") is not { } accessToken)
            return null;

        return (ResolveHubUri(ActiveEnvironment(root, userId)), accessToken);
    }

    // global_account_activeAccountId first appears in CLI state version 60; older layouts misread as logged out.
    public const int MinLoginStateVersion = 60;

    public static (bool LoggedIn, string? ServerUrl)? ReadLoginState(string dataFile)
    {
        using var stream = new FileStream(dataFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("stateVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var stateVersion)
            || stateVersion < MinLoginStateVersion)
            return null;
        var userId = StringProperty(root, "global_account_activeAccountId");
        var loggedIn = userId != null && StringProperty(root, $"user_{userId}_token_accessToken") != null;
        return (loggedIn, ResolveServerUrl(ActiveEnvironment(root, userId)));
    }

    private static JsonElement ActiveEnvironment(JsonElement root, string? userId) =>
        userId != null && root.TryGetProperty($"user_{userId}_environment_environment", out var userEnvironment) && userEnvironment.ValueKind == JsonValueKind.Object
            ? userEnvironment
            : root.TryGetProperty("global_environment_environment", out var globalEnvironment) ? globalEnvironment : default;

    // Matches `bw status` serverUrl (environment urls.base): the CLI cloud regions set base to null.
    public static string? ResolveServerUrl(JsonElement environment)
    {
        var urls = environment.ValueKind == JsonValueKind.Object && environment.TryGetProperty("urls", out var u) ? u : default;
        return StringProperty(environment, "region") is not ("US" or "EU") && StringProperty(urls, "base") is { Length: > 0 } baseUrl
            ? baseUrl.TrimEnd('/')
            : null;
    }

    public static Uri ResolveHubUri(JsonElement environment)
    {
        var region = StringProperty(environment, "region");
        var urls = environment.ValueKind == JsonValueKind.Object && environment.TryGetProperty("urls", out var u) ? u : default;
        var notifications = StringProperty(urls, "notifications")?.TrimEnd('/')
            ?? (region is not ("US" or "EU") && StringProperty(urls, "base") is { } baseUrl ? baseUrl.TrimEnd('/') + "/notifications" : null)
            ?? (region == "EU" ? "https://notifications.bitwarden.eu" : "https://notifications.bitwarden.com");
        return new Uri(notifications.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? "ws" + notifications["http".Length..] + "/hub"
            : notifications + "/hub");
    }

    public static bool TokenExpiresWithin(string accessToken, TimeSpan margin)
    {
        try
        {
            var parts = accessToken.Split('.');
            if (parts.Length != 3) return true;
            using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            return !payload.RootElement.TryGetProperty("exp", out var exp)
                || DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()) - DateTimeOffset.UtcNow < margin;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            return true;
        }
    }

    public static HubFrameKind Classify(ReadOnlySpan<byte> message)
    {
        try
        {
            var reader = new MessagePackReader(message);
            var length = reader.ReadArrayHeader();
            var type = reader.ReadInt();
            if (type == 7) return HubFrameKind.Close;
            if (type != 1 || length < 5) return HubFrameKind.Other;
            reader.Skip();
            reader.Skip();
            if (reader.ReadString() != "ReceiveMessage" || reader.ReadArrayHeader() < 1) return HubFrameKind.Other;
            var fields = reader.ReadMapHeader();
            for (var i = 0; i < fields; i++)
            {
                if (reader.ReadString() == "Type")
                    return VaultChangeTypes.Contains(reader.ReadInt()) ? HubFrameKind.VaultChanged : HubFrameKind.Other;
                reader.Skip();
            }
            return HubFrameKind.Other;
        }
        catch (InvalidDataException)
        {
            return HubFrameKind.Other;
        }
    }

    public static bool TryReadVarInt(ReadOnlySpan<byte> data, out int value, out int size)
    {
        value = 0;
        for (var i = 0; i < data.Length && i < 5; i++)
        {
            value |= (data[i] & 0x7f) << (7 * i);
            if ((data[i] & 0x80) == 0)
            {
                if (value is < 0 or > MaxFrameSize)
                    throw new InvalidDataException($"Hub frame length {value} is out of range");
                size = i + 1;
                return true;
            }
        }
        if (data.Length >= 5)
            throw new InvalidDataException("Hub frame length prefix is longer than 5 bytes");
        size = 0;
        return false;
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

internal sealed class HubFrameReader
{
    private const byte RecordSeparator = 0x1e;
    private byte[] _pending = [];

    public bool HandshakeCompleted { get; private set; }

    public List<HubFrameKind> Append(ReadOnlySpan<byte> received)
    {
        ReadOnlySpan<byte> data = _pending = [.. _pending, .. received];
        var frames = new List<HubFrameKind>();
        var consumed = 0;
        if (!HandshakeCompleted)
        {
            var end = data.IndexOf(RecordSeparator);
            if (end < 0) return frames;
            if (data[..end].IndexOf("\"error\""u8) >= 0)
                throw new InvalidDataException("Notifications hub rejected the handshake: " + Encoding.UTF8.GetString(data[..end]));
            consumed = end + 1;
            HandshakeCompleted = true;
        }

        while (HubProtocol.TryReadVarInt(data[consumed..], out var length, out var size) && data.Length - consumed - size >= length)
        {
            frames.Add(HubProtocol.Classify(data.Slice(consumed + size, length)));
            consumed += size + length;
        }

        _pending = data[consumed..].ToArray();
        return frames;
    }
}

internal ref struct MessagePackReader(ReadOnlySpan<byte> data)
{
    private const int MaxDepth = 32;
    private readonly ReadOnlySpan<byte> _data = data;
    private int _position;

    public int ReadArrayHeader()
    {
        var b = ReadByte();
        return b switch
        {
            >= 0x90 and <= 0x9f => b & 0x0f,
            0xdc => ReadLength(2),
            0xdd => ReadLength(4),
            _ => throw Unexpected(b),
        };
    }

    public int ReadMapHeader()
    {
        var b = ReadByte();
        return b switch
        {
            >= 0x80 and <= 0x8f => b & 0x0f,
            0xde => ReadLength(2),
            0xdf => ReadLength(4),
            _ => throw Unexpected(b),
        };
    }

    public long ReadInt()
    {
        var b = ReadByte();
        return b switch
        {
            <= 0x7f => b,
            >= 0xe0 => (sbyte)b,
            0xcc => Take(1)[0],
            0xcd => BinaryPrimitives.ReadUInt16BigEndian(Take(2)),
            0xce => BinaryPrimitives.ReadUInt32BigEndian(Take(4)),
            0xcf => (long)BinaryPrimitives.ReadUInt64BigEndian(Take(8)),
            0xd0 => (sbyte)Take(1)[0],
            0xd1 => BinaryPrimitives.ReadInt16BigEndian(Take(2)),
            0xd2 => BinaryPrimitives.ReadInt32BigEndian(Take(4)),
            0xd3 => BinaryPrimitives.ReadInt64BigEndian(Take(8)),
            _ => throw Unexpected(b),
        };
    }

    public string? ReadString()
    {
        var b = ReadByte();
        var length = b switch
        {
            >= 0xa0 and <= 0xbf => b & 0x1f,
            0xd9 => ReadLength(1),
            0xda => ReadLength(2),
            0xdb => ReadLength(4),
            0xc0 => -1,
            _ => throw Unexpected(b),
        };
        return length < 0 ? null : Encoding.UTF8.GetString(Take(length));
    }

    public void Skip(int depth = 0)
    {
        if (depth > MaxDepth)
            throw new InvalidDataException("MessagePack nesting is too deep");
        var b = ReadByte();
        switch (b)
        {
            case <= 0x7f or >= 0xe0 or 0xc0 or 0xc2 or 0xc3: return;
            case >= 0x80 and <= 0x8f: SkipItems(2 * (b & 0x0f), depth); return;
            case >= 0x90 and <= 0x9f: SkipItems(b & 0x0f, depth); return;
            case >= 0xa0 and <= 0xbf: Advance(b & 0x1f); return;
            case 0xc4 or 0xd9: Advance(ReadLength(1)); return;
            case 0xc5 or 0xda: Advance(ReadLength(2)); return;
            case 0xc6 or 0xdb: Advance(ReadLength(4)); return;
            case 0xc7: Advance(ReadLength(1) + 1); return;
            case 0xc8: Advance(ReadLength(2) + 1); return;
            case 0xc9: Advance(ReadLength(4) + 1); return;
            case 0xcc or 0xd0: Advance(1); return;
            case 0xcd or 0xd1: Advance(2); return;
            case 0xca or 0xce or 0xd2: Advance(4); return;
            case 0xcb or 0xcf or 0xd3: Advance(8); return;
            case 0xd4: Advance(2); return;
            case 0xd5: Advance(3); return;
            case 0xd6: Advance(5); return;
            case 0xd7: Advance(9); return;
            case 0xd8: Advance(17); return;
            case 0xdc: SkipItems(ReadLength(2), depth); return;
            case 0xdd: SkipItems(ReadLength(4), depth); return;
            case 0xde: SkipItems(2 * ReadLength(2), depth); return;
            case 0xdf: SkipItems(2L * ReadLength(4), depth); return;
            default: throw Unexpected(b);
        }
    }

    private void SkipItems(long count, int depth)
    {
        for (var i = 0L; i < count; i++)
            Skip(depth + 1);
    }

    private int ReadLength(int size) => size switch
    {
        1 => Take(1)[0],
        2 => BinaryPrimitives.ReadUInt16BigEndian(Take(2)),
        _ => (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(Take(4)), int.MaxValue),
    };

    private byte ReadByte() => Take(1)[0];

    private ReadOnlySpan<byte> Take(int count)
    {
        var start = _position;
        Advance(count);
        return _data.Slice(start, count);
    }

    private void Advance(int count)
    {
        if (count < 0 || count > _data.Length - _position)
            throw new InvalidDataException("Truncated MessagePack data");
        _position += count;
    }

    private static InvalidDataException Unexpected(byte b) => new($"Unexpected MessagePack format byte 0x{b:x2}");
}
