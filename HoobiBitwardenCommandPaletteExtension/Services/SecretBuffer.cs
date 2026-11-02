using System;
using System.Security.Cryptography;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal sealed class SecretBuffer : IDisposable
{
  private readonly byte[] _buffer;
  private readonly int _start;
  private readonly int _length;

  public SecretBuffer(byte[] buffer, int start, int length)
  {
    _buffer = buffer;
    _start = start;
    _length = length;
  }

  public SecretBuffer(string text)
  {
    _buffer = [];
    Text = text;
  }

  public ReadOnlyMemory<byte> Json => _buffer.AsMemory(_start, _length);

  public string? Text { get; }

  public void Dispose() => CryptographicOperations.ZeroMemory(_buffer);
}
