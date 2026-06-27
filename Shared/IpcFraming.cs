using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

// Shared message framing for the extension<->companion pipe (4-byte little-endian length + UTF-8
// JSON). Stream-based so both the server (NamedPipeServerStream) and client (NamedPipeClientStream)
// use the exact same code. Linked into both projects.
namespace HoobiBitwardenCompanionIpc;

internal static class IpcFraming
{
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    public static async Task<JsonObject?> ReadMessageAsync(Stream stream, CancellationToken token)
    {
        var lengthBuffer = new byte[4];
        if (!await ReadExactAsync(stream, lengthBuffer, token)) return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (length <= 0 || length > MaxMessageBytes) return null;

        var payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, token)) return null;

        return JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject;
    }

    public static async Task WriteMessageAsync(Stream stream, JsonObject message, CancellationToken token)
    {
        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
        await stream.WriteAsync(frame, token);
        await stream.FlushAsync(token);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), token);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }
}
