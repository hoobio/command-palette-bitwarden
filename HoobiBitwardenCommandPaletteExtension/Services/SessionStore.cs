using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class SessionStore
{
  // MSIX scopes Win32 CredWrite/CredRead per package family name. Dev and
  // Release builds have different identities (.Debug suffix vs not) so they
  // can't see each other's credentials — and the saved credential from one
  // looks like a stale orphan to the other. Use a different target name per
  // build configuration so each owns its own slot, with no surprising
  // cross-build behavior when switching between them.
#if DEBUG
  private const string TargetName = "BitwardenCommandPaletteExtension.Debug/default_sessionToken";
#else
  private const string TargetName = "BitwardenCommandPaletteExtension/default_sessionToken";
#endif
  private const int CredTypeGeneric = 1;
  private const int CredPersistLocalMachine = 2;

  public static void Save(string sessionKey)
  {
    var bytes = Encoding.UTF8.GetBytes(sessionKey);
    var cred = new CREDENTIAL
    {
      Type = CredTypeGeneric,
      TargetName = TargetName,
      CredentialBlobSize = bytes.Length,
      CredentialBlob = Marshal.AllocHGlobal(bytes.Length),
      Persist = CredPersistLocalMachine,
      UserName = "session",
    };

    try
    {
      Marshal.Copy(bytes, 0, cred.CredentialBlob, bytes.Length);
      if (!CredWrite(ref cred, 0))
      {
        var err = Marshal.GetLastWin32Error();
        DebugLogService.Log("Session", $"CredWrite failed: Win32 error {err}");
      }
      else
      {
        DebugLogService.Log("Session", $"Session saved to Credential Manager (target: {TargetName})");
      }
    }
    finally
    {
      Marshal.FreeHGlobal(cred.CredentialBlob);
    }
  }

  public static string? Load()
  {
    if (!CredRead(TargetName, CredTypeGeneric, 0, out var credPtr))
    {
      var err = Marshal.GetLastWin32Error();
      DebugLogService.Log("Session", $"CredRead failed: Win32 error {err} (target: {TargetName})");
      return null;
    }

    try
    {
      var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
      if (cred.CredentialBlob == nint.Zero || cred.CredentialBlobSize == 0)
        return null;

      var bytes = new byte[cred.CredentialBlobSize];
      Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
      return Encoding.UTF8.GetString(bytes);
    }
    finally
    {
      CredFree(credPtr);
    }
  }

  public static void Clear()
  {
    CredDelete(TargetName, CredTypeGeneric, 0);
  }

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private struct CREDENTIAL
  {
    public int Flags;
    public int Type;
    [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
    [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
    public long LastWritten;
    public int CredentialBlobSize;
    public nint CredentialBlob;
    public int Persist;
    public int AttributeCount;
    public nint Attributes;
    [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
    [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
  }

  // NOTE: Using legacy [DllImport] instead of [LibraryImport] because the CREDENTIAL struct
  // has [MarshalAs(UnmanagedType.LPWStr)] fields and ref-struct parameters that require
  // non-trivial rework to be compatible with the source-generated marshaller.
  [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

  [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern bool CredRead(string targetName, int type, int flags, out nint credential);

  [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern bool CredDelete(string targetName, int type, int flags);

  [DllImport("advapi32.dll", SetLastError = true)]
  private static extern void CredFree(nint buffer);
}
