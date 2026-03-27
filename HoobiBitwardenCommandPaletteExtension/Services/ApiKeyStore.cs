using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class ApiKeyStore
{
  private const string ClientIdTarget = "BitwardenCommandPaletteExtension/apikey_clientId";
  private const string ClientSecretTarget = "BitwardenCommandPaletteExtension/apikey_clientSecret";
  private const int CredTypeGeneric = 1;
  private const int CredPersistLocalMachine = 2;

  public static void Save(string clientId, string clientSecret)
  {
    WriteCredential(ClientIdTarget, "clientId", clientId);
    WriteCredential(ClientSecretTarget, "clientSecret", clientSecret);
  }

  public static (string? ClientId, string? ClientSecret) Load()
  {
    return (ReadCredential(ClientIdTarget), ReadCredential(ClientSecretTarget));
  }

  public static void Clear()
  {
    CredDelete(ClientIdTarget, CredTypeGeneric, 0);
    CredDelete(ClientSecretTarget, CredTypeGeneric, 0);
  }

  private static void WriteCredential(string targetName, string userName, string value)
  {
    var bytes = Encoding.UTF8.GetBytes(value);
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
      CredWrite(ref cred, 0);
    }
    finally
    {
      Marshal.FreeHGlobal(cred.CredentialBlob);
    }
  }

  private static string? ReadCredential(string targetName)
  {
    if (!CredRead(targetName, CredTypeGeneric, 0, out var credPtr))
      return null;

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
