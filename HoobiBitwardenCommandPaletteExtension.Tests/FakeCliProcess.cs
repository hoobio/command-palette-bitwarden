using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

internal sealed class FakeCliProcess : ICliProcess
{
  public ChannelStream StdoutStream { get; }
  public ChannelStream StderrStream { get; }
  private readonly MemoryStream _stdinBacking = new();

  public FakeCliProcess(string stdout = "", string stderr = "", int exitCode = 0, int id = 1)
  {
    StdoutStream = new ChannelStream();
    StderrStream = new ChannelStream();
    if (stdout.Length > 0) StdoutStream.Enqueue(stdout);
    StdoutStream.Complete();
    if (stderr.Length > 0) StderrStream.Enqueue(stderr);
    StderrStream.Complete();
    StandardOutput = new StreamReader(StdoutStream);
    StandardError = new StreamReader(StderrStream);
    StandardInput = new StreamWriter(_stdinBacking) { AutoFlush = true };
    ExitCode = exitCode;
    Id = id;
  }

  public FakeCliProcess(int exitCode, int id = 1)
  {
    StdoutStream = new ChannelStream();
    StderrStream = new ChannelStream();
    StandardOutput = new StreamReader(StdoutStream);
    StandardError = new StreamReader(StderrStream);
    StandardInput = new StreamWriter(_stdinBacking) { AutoFlush = true };
    ExitCode = exitCode;
    Id = id;
  }

  public StreamReader StandardOutput { get; }
  public StreamReader StandardError { get; }
  public StreamWriter StandardInput { get; }
  public int ExitCode { get; set; }
  public int Id { get; set; }
  public bool EnableRaisingEvents { get; set; }
  public event EventHandler? Exited;
  public bool Killed { get; private set; }
  public bool Disposed { get; private set; }

  public Task WaitForExitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
  public void Kill(bool entireProcessTree = false) => Killed = true;
  public void Dispose() => Disposed = true;

  public void RaiseExited() => Exited?.Invoke(this, EventArgs.Empty);

  public string GetStdinContent()
  {
    _stdinBacking.Position = 0;
    return new StreamReader(_stdinBacking).ReadToEnd();
  }
}

internal sealed class ChannelStream : Stream
{
  private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
  private byte[]? _current;
  private int _currentOffset;

  public void Enqueue(string text) => _channel.Writer.TryWrite(System.Text.Encoding.UTF8.GetBytes(text));
  public void Complete() => _channel.Writer.Complete();

  public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
  {
    if (_current != null && _currentOffset < _current.Length)
    {
      var n = Math.Min(buffer.Length, _current.Length - _currentOffset);
      _current.AsMemory(_currentOffset, n).CopyTo(buffer);
      _currentOffset += n;
      if (_currentOffset >= _current.Length) _current = null;
      return n;
    }

    try
    {
      if (!await _channel.Reader.WaitToReadAsync(cancellationToken)) return 0;
    }
    catch (ChannelClosedException) { return 0; }

    if (_channel.Reader.TryRead(out var chunk))
    {
      _current = chunk;
      _currentOffset = 0;
      return await ReadAsync(buffer, cancellationToken);
    }

    return 0;
  }

#pragma warning disable VSTHRD002 // Sync-over-async required for Stream.Read override
  public override int Read(byte[] buffer, int offset, int count) =>
      ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

  public override bool CanRead => true;
  public override bool CanWrite => false;
  public override bool CanSeek => false;
  public override long Length => throw new NotSupportedException();
  public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
  public override void Flush() { }
  public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
  public override void SetLength(long value) => throw new NotSupportedException();
  public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class FakeProcessFactory
{
  private readonly Queue<FakeCliProcess> _queue = new();

  public void Enqueue(FakeCliProcess process) => _queue.Enqueue(process);

  public ICliProcess Create(System.Diagnostics.ProcessStartInfo psi)
  {
    if (_queue.Count == 0)
      throw new InvalidOperationException($"No fake process queued for: {psi.Arguments}");
    LastArgs = psi.Arguments;
    LastPsi = psi;
    return _queue.Dequeue();
  }

  public string? LastArgs { get; private set; }
  public System.Diagnostics.ProcessStartInfo? LastPsi { get; private set; }
}
