using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace HoobiBitwarden.Ipc;

internal static partial class ProcessHardening
{
  private const int SeKernelObject = 6;
  private const uint DaclSecurityInformation = 0x4;
  private const uint ProtectedDaclSecurityInformation = 0x80000000;
  private const int OwnerAccess = 0x00101001;
  private const int SystemAccess = 0x1FFFFF;

  // ponytail: process object only; threads keep the token default DACL (hijackable via OpenThread) and the COM-activated extension keeps its .NET diagnostics pipe. Upgrade path: TokenDefaultDacl plus a re-exec with DOTNET_EnableDiagnostics=0, both need a COM activation test.
  internal static uint Apply() => Debugger.IsAttached ? 0 : Apply(GetCurrentProcess());

  internal static uint Apply(nint process)
  {
    var dacl = BuildDacl();
    return SetSecurityInfo(process, SeKernelObject, DaclSecurityInformation | ProtectedDaclSecurityInformation, 0, 0, ref MemoryMarshal.GetArrayDataReference(dacl), 0);
  }

  // OWNER RIGHTS (S-1-3-4) replaces the owner's implicit READ_CONTROL and WRITE_DAC, so same-user code cannot rewrite this DACL.
  internal static RawAcl BuildAcl()
  {
    var acl = new RawAcl(RawAcl.AclRevision, 2);
    acl.InsertAce(0, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, OwnerAccess, new SecurityIdentifier("S-1-3-4"), false, null));
    acl.InsertAce(1, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, SystemAccess, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), false, null));
    return acl;
  }

  private static byte[] BuildDacl()
  {
    var acl = BuildAcl();
    var bytes = new byte[acl.BinaryLength];
    acl.GetBinaryForm(bytes, 0);
    return bytes;
  }

  [LibraryImport("kernel32.dll")]
  private static partial nint GetCurrentProcess();

  [LibraryImport("advapi32.dll")]
  private static partial uint SetSecurityInfo(nint handle, int objectType, uint securityInfo, nint owner, nint group, ref byte dacl, nint sacl);
}
