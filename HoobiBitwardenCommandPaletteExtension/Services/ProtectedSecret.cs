using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal delegate T SecretFunc<out T>(ReadOnlySpan<char> secret);

// ponytail: only the extension's own long-lived copies are encrypted; plaintext still passes through bw stdout, the host's form-submit JSON, Reveal() call sites and the BW_SESSION/BW_MP env of child processes.
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

  public T Use<T>(SecretFunc<T> action)
  {
    var size = _cipher.Length;
    Span<byte> plain = size <= StackLimit ? stackalloc byte[size] : GC.AllocateUninitializedArray<byte>(size, pinned: true);
    try
    {
      lock (_lock)
      {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cipher.CopyTo(plain);
      }
      if (!CryptUnprotectMemory(ref MemoryMarshal.GetReference(plain), (uint)size, SameProcess))
        throw new CryptographicException(Marshal.GetLastPInvokeError());
      return action(MemoryMarshal.Cast<byte, char>(plain)[..Length]);
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
