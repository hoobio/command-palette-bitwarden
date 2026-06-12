using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwardenCompanionIpc;

namespace HoobiBitwardenCompanion.Services;

internal readonly record struct IpcResponse(bool Ok, string? Error, JsonObject Data)
{
    public string? GetString(string field) => Data[field]?.GetValue<string>();
    public bool GetBool(string field) => Data[field]?.GetValue<bool>() ?? false;
}

// Client end of the extension <-> companion channel. One persistent connection per companion process,
// requests serialised (the UI issues one intent at a time). Mirrors the server's framing: 4-byte
// little-endian length prefix + UTF-8 JSON.
internal sealed class ExtensionIpcClient : IDisposable
{
    private const int MaxMessageBytes = 4 * 1024 * 1024;
    private const int ConnectTimeoutMs = 5_000;

    private readonly string _pipeName;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private int _nextId;

    public ExtensionIpcClient(string pipeName) => _pipeName = pipeName;

    public bool IsConnected => _pipe?.IsConnected == true;

    public async Task ConnectAsync(CancellationToken token = default)
    {
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(ConnectTimeoutMs);
        await _pipe.ConnectAsync(cts.Token);
    }

    public async Task<IpcResponse> SendAsync(string command, JsonObject? args = null, CancellationToken token = default)
    {
        if (_pipe is not { IsConnected: true })
            throw new InvalidOperationException("Not connected to the Bitwarden extension.");

        var id = Interlocked.Increment(ref _nextId);
        var request = new JsonObject
        {
            [IpcFields.Id] = id,
            [IpcFields.Command] = command,
            [IpcFields.Args] = args ?? new JsonObject(),
        };

        await _sendLock.WaitAsync(token);
        try
        {
            await WriteMessageAsync(request, token);
            var response = await ReadMessageAsync(token)
                ?? throw new IOException("The extension closed the connection.");

            return new IpcResponse(
                response[IpcFields.Ok]?.GetValue<bool>() ?? false,
                response[IpcFields.Error]?.GetValue<string>(),
                response[IpcFields.Data]?.AsObject() ?? new JsonObject());
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task WriteMessageAsync(JsonObject message, CancellationToken token)
    {
        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
        await _pipe!.WriteAsync(frame, token);
        await _pipe.FlushAsync(token);
    }

    private async Task<JsonObject?> ReadMessageAsync(CancellationToken token)
    {
        var lengthBuffer = new byte[4];
        if (!await ReadExactAsync(lengthBuffer, token)) return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (length <= 0 || length > MaxMessageBytes) return null;

        var payload = new byte[length];
        if (!await ReadExactAsync(payload, token)) return null;

        return JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject;
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await _pipe!.ReadAsync(buffer.AsMemory(offset), token);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    public void Dispose()
    {
        _sendLock.Dispose();
        _pipe?.Dispose();
    }
}
