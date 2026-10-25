using System.Runtime.InteropServices;
using System.Text;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class CredentialStore
{
  private const int CredTypeGeneric = 1;
  private const int CredPersistLocalMachine = 2;

  public static void Write(string targetName, string userName, string secret)
  {
    var bytes = Encoding.UTF8.GetBytes(secret);
    var cred = new CREDENTIAL
    {
      Type = CredTypeGeneric,
      TargetName = targetName,
      CredentialBlobSize = bytes.Length,
      CredentialBlob = Marshal.AllocHGlobal(bytes.Length),
      Persist = CredPersistLocalMachine,
      UserName = userName,
    };

    try
    {
      Marshal.Copy(bytes, 0, cred.CredentialBlob, bytes.Length);
      if (!CredWrite(ref cred, 0))
      {
        var err = Marshal.GetLastWin32Error();
        DebugLogService.Log("Session", $"CredWrite failed: Win32 error {err} (target: {targetName})");
      }
      else
      {
        DebugLogService.Log("Session", $"Credential saved to Credential Manager (target: {targetName})");
      }
    }
    finally
    {
      Marshal.FreeHGlobal(cred.CredentialBlob);
    }
  }

  public static (string? UserName, string Secret)? Read(string targetName)
  {
    if (!CredRead(targetName, CredTypeGeneric, 0, out var credPtr))
    {
      var err = Marshal.GetLastWin32Error();
      DebugLogService.Log("Session", $"CredRead failed: Win32 error {err} (target: {targetName})");
      return null;
    }

    try
    {
      var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
      if (cred.CredentialBlob == nint.Zero || cred.CredentialBlobSize == 0)
        return null;

      var bytes = new byte[cred.CredentialBlobSize];
      Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
      return (cred.UserName, Encoding.UTF8.GetString(bytes));
    }
    finally
    {
      CredFree(credPtr);
    }
  }

  public static void Delete(string targetName)
  {
    CredDelete(targetName, CredTypeGeneric, 0);
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

  // Legacy [DllImport] on purpose: the CREDENTIAL struct's LPWStr fields do not work with the source-generated [LibraryImport] marshaller.
  [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

  [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern bool CredRead(string targetName, int type, int flags, out nint credential);

  [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern bool CredDelete(string targetName, int type, int flags);

  [DllImport("advapi32.dll", SetLastError = true)]
  private static extern void CredFree(nint buffer);
}
