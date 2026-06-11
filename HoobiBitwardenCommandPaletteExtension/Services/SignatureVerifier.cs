using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Authenticode verification for a downloaded executable. Used by the direct-download
// fallback in BitwardenCliInstaller: a binary we fetch and then execute has to be
// proven (a) Authenticode-valid against a trusted chain and (b) actually published
// by Bitwarden, not just any validly-signed exe. winget-installed binaries are
// verified by winget itself and don't go through here.
[SupportedOSPlatform("windows")]
internal static partial class SignatureVerifier
{
  // Subjects Bitwarden has shipped the CLI under. Matched case-insensitively as a
  // substring of the signing certificate's subject.
  private static readonly string[] TrustedPublishers =
  [
    "Bitwarden",
    "8bit Solutions",
  ];

  public static bool IsTrustedBitwardenBinary(string filePath) =>
      IsAuthenticodeValid(filePath) && IsBitwardenPublisher(filePath);

  private static bool IsBitwardenPublisher(string filePath)
  {
    try
    {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile is the supported way to read the Authenticode signer
      using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
      var subject = cert.Subject;
      foreach (var publisher in TrustedPublishers)
      {
        if (subject.Contains(publisher, StringComparison.OrdinalIgnoreCase))
          return true;
      }
      DebugLogService.Log("Installer", $"Downloaded binary signer not trusted: {subject}");
      return false;
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Installer", $"Signer extraction failed: {ex.GetType().Name}: {ex.Message}");
      return false;
    }
  }

  private static bool IsAuthenticodeValid(string filePath)
  {
    var pathPtr = Marshal.StringToHGlobalUni(filePath);
    var fileInfo = new WinTrustFileInfo
    {
      cbStruct = (uint)Unsafe.SizeOf<WinTrustFileInfo>(),
      pcwszFilePath = pathPtr,
    };
    var fileHandle = GCHandle.Alloc(fileInfo, GCHandleType.Pinned);
    try
    {
      var data = new WinTrustData
      {
        cbStruct = (uint)Unsafe.SizeOf<WinTrustData>(),
        dwUIChoice = WTD_UI_NONE,
        fdwRevocationChecks = WTD_REVOKE_NONE,
        dwUnionChoice = WTD_CHOICE_FILE,
        dwStateAction = WTD_STATEACTION_VERIFY,
        pFile = fileHandle.AddrOfPinnedObject(),
      };
      var action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
      var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

      data.dwStateAction = WTD_STATEACTION_CLOSE;
      _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data);

      if (result != 0)
        DebugLogService.Log("Installer", $"WinVerifyTrust rejected download (0x{result:X8})");
      return result == 0;
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Installer", $"WinVerifyTrust failed: {ex.GetType().Name}: {ex.Message}");
      return false;
    }
    finally
    {
      fileHandle.Free();
      Marshal.FreeHGlobal(pathPtr);
    }
  }

  private const uint WTD_UI_NONE = 2;
  private const uint WTD_REVOKE_NONE = 0;
  private const uint WTD_CHOICE_FILE = 1;
  private const uint WTD_STATEACTION_VERIFY = 1;
  private const uint WTD_STATEACTION_CLOSE = 2;

  private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

  [LibraryImport("wintrust.dll")]
  private static partial int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);

  [StructLayout(LayoutKind.Sequential)]
  private struct WinTrustFileInfo
  {
    public uint cbStruct;
    public IntPtr pcwszFilePath;
    public IntPtr hFile;
    public IntPtr pgKnownSubject;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct WinTrustData
  {
    public uint cbStruct;
    public IntPtr pPolicyCallbackData;
    public IntPtr pSIPClientData;
    public uint dwUIChoice;
    public uint fdwRevocationChecks;
    public uint dwUnionChoice;
    public IntPtr pFile;
    public uint dwStateAction;
    public IntPtr hWVTStateData;
    public IntPtr pwszURLReference;
    public uint dwProvFlags;
    public uint dwUIContext;
    public IntPtr pSignatureSettings;
  }
}
