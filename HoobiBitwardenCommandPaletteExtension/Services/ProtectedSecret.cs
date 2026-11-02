using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal delegate T SecretFunc<out T>(ReadOnlySpan<char> secret);

// ponytail: bw stdout is read into a zeroed pinned buffer and secrets go bytes to ProtectedSecret; plaintext still passes through the stdout pipe's kernel buffers, the get-item JsonObject used for edits, Reveal() call sites, the companion pipe JSON and the BW_SESSION/BW_MP env of child processes.
internal sealed partial class ProtectedSecret : IDisposable
{
  private const int BlockSize = 16;
  private const int StackLimit = 1024;
  private const uint SameProcess = 0;

  private readonly byte[] _cipher;
  private readonly Lock _lock = new();
  private bool _disposed;

  public int Length { get; }

  private ProtectedSecret(ReadOnlySpan<char> plaintext)
  {
    Length = plaintext.Length;
    var bytes = MemoryMarshal.AsBytes(plaintext);
    _cipher = GC.AllocateArray<byte>((bytes.Length + BlockSize - 1) / BlockSize * BlockSize, pinned: true);
    bytes.CopyTo(_cipher);
    if (!CryptProtectMemory(ref MemoryMarshal.GetArrayDataReference(_cipher), (uint)_cipher.Length, SameProcess))
    {
      CryptographicOperations.ZeroMemory(_cipher);
      throw new CryptographicException(Marshal.GetLastPInvokeError());
    }
  }

  public static ProtectedSecret? From(ReadOnlySpan<char> plaintext) => plaintext.IsEmpty ? null : new(plaintext);

  public static ProtectedSecret? FromJson(JsonElement value)
  {
    if (value.ValueKind == JsonValueKind.Null) return null;
    var reader = new Utf8JsonReader(JsonMarshal.GetRawUtf8Value(value));
    reader.Read();
    var size = reader.ValueSpan.Length;
    Span<char> chars = size <= StackLimit / sizeof(char) ? stackalloc char[size] : GC.AllocateUninitializedArray<char>(size, pinned: true);
    try
    {
      return From(chars[..reader.CopyString(chars)]);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars));
    }
  }

  public T Use<T>(SecretFunc<T> action) =>
    TryUse(action, out var result) ? result : throw new ObjectDisposedException(GetType().FullName);

  public T? UseOrDefault<T>(SecretFunc<T> action) => TryUse(action, out var result) ? result : default;

  public bool TryUse<T>(SecretFunc<T> action, out T result)
  {
    var size = _cipher.Length;
    Span<byte> plain = size <= StackLimit ? stackalloc byte[size] : GC.AllocateUninitializedArray<byte>(size, pinned: true);
    try
    {
      lock (_lock)
      {
        if (_disposed)
        {
          result = default!;
          return false;
        }
        _cipher.CopyTo(plain);
      }
      if (!CryptUnprotectMemory(ref MemoryMarshal.GetReference(plain), (uint)size, SameProcess))
        throw new CryptographicException(Marshal.GetLastPInvokeError());
      result = action(MemoryMarshal.Cast<byte, char>(plain)[..Length]);
      return true;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(plain);
    }
  }

  public string Reveal() => Use(static secret => new string(secret));

  public void Dispose()
  {
    lock (_lock)
    {
      _disposed = true;
      CryptographicOperations.ZeroMemory(_cipher);
    }
  }

  [LibraryImport("crypt32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool CryptProtectMemory(ref byte pDataIn, uint cbDataIn, uint dwFlags);

  [LibraryImport("crypt32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool CryptUnprotectMemory(ref byte pDataIn, uint cbDataIn, uint dwFlags);
}
