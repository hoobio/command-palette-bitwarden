using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HoobiBitwarden.Ipc;

internal delegate Task<JsonElement?> IpcRequestHandler(string op, JsonElement? payload, CancellationToken cancellationToken);

internal sealed partial class PipeChannel : IDisposable
{
  private static readonly byte[] Newline = [(byte)'\n'];

  private readonly StreamReader _reader;
  private readonly Stream _output;
  private readonly IpcRequestHandler _onRequest;
  private readonly Action<string, JsonElement?>? _onEvent;
  private readonly ConcurrentDictionary<long, TaskCompletionSource<Envelope>> _pending = new();
  private readonly SemaphoreSlim _writeLock = new(1, 1);
  private readonly CancellationTokenSource _closing = new();
  private long _nextId;
  private volatile bool _closed;
  private int _disposed;

  public PipeChannel(Stream input, Stream output, IpcRequestHandler onRequest, Action<string, JsonElement?>? onEvent = null)
  {
    _reader = new StreamReader(input, new UTF8Encoding(false));
    _output = output;
    _onRequest = onRequest;
    _onEvent = onEvent;
  }

  public bool IsClosed => _closed;

  public async Task RunAsync()
  {
    try
    {
      while (await _reader.ReadLineAsync(_closing.Token) is { } line)
        Dispatch(line);
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
    {
    }
    finally
    {
      Close();
    }
  }

  public async Task<JsonElement?> RequestAsync(string op, JsonElement? payload, CancellationToken cancellationToken = default)
  {
    var id = Interlocked.Increment(ref _nextId);
    var response = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
    _pending[id] = response;
    if (_closed && _pending.TryRemove(id, out _))
      throw new IOException("Channel closed");

    using var registration = cancellationToken.Register(() =>
    {
      if (_pending.TryRemove(id, out var pending))
        pending.TrySetCanceled(cancellationToken);
    });
    try
    {
      await WriteAsync(new Envelope(id, MessageTypes.Request, op, payload));
    }
    catch
    {
      _pending.TryRemove(id, out _);
      throw;
    }

    var reply = await response.Task;
    return reply.Error is { } error ? throw new IpcException(error) : reply.Payload;
  }

  public Task SendEventAsync(string op, JsonElement? payload = null) =>
    WriteAsync(new Envelope(0, MessageTypes.Event, op, payload));

  private void Dispatch(string line)
  {
    Envelope? message;
    try { message = JsonSerializer.Deserialize(line, IpcJson.Default.Envelope); }
    catch (JsonException) { return; }

    switch (message?.Type)
    {
      case MessageTypes.Response when _pending.TryRemove(message.Id, out var pending):
        pending.TrySetResult(message);
        break;
      case MessageTypes.Request:
        _ = RespondAsync(message);
        break;
      case MessageTypes.Event when message.Op is { } op:
        _onEvent?.Invoke(op, message.Payload);
        break;
    }
  }

  private async Task RespondAsync(Envelope request)
  {
    JsonElement? result = null;
    string? error = null;
    try
    {
      result = await Task.Run(() => _onRequest(request.Op ?? string.Empty, request.Payload, _closing.Token));
    }
    catch (IpcException ex)
    {
      error = ex.Code;
    }
    catch (Exception)
    {
      error = IpcErrors.Failed;
    }

    try
    {
      await WriteAsync(new Envelope(request.Id, MessageTypes.Response, request.Op, result, error));
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
    {
    }
  }

  private async Task WriteAsync(Envelope message)
  {
    if (_closed)
      throw new IOException("Channel closed");
    var bytes = JsonSerializer.SerializeToUtf8Bytes(message, IpcJson.Default.Envelope);
    await _writeLock.WaitAsync(_closing.Token);
    try
    {
      await _output.WriteAsync(bytes, _closing.Token);
      await _output.WriteAsync(Newline, _closing.Token);
      await _output.FlushAsync(_closing.Token);
    }
    finally
    {
      _writeLock.Release();
    }
  }

  private void Close()
  {
    _closed = true;
    foreach (var id in _pending.Keys)
    {
      if (_pending.TryRemove(id, out var pending))
        pending.TrySetException(new IOException("Channel closed"));
    }
  }

  public void Dispose()
  {
    if (Interlocked.Exchange(ref _disposed, 1) == 1)
      return;
    Close();
    _closing.Cancel();
    _output.Dispose();
    _reader.Dispose();
    _closing.Dispose();
    _writeLock.Dispose();
  }
}
