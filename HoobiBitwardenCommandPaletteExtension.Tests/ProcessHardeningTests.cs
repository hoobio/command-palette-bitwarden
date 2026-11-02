using System;
using System.Security.AccessControl;
using System.Security.Principal;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class ProcessHardeningTests
{
  [Fact]
  public void BuildAcl_GrantsOwnerRightsOnlyTheLimitedMaskAndSystemEverything()
  {
    var acl = ProcessHardening.BuildAcl();

    Assert.Equal(2, acl.Count);
    var owner = Assert.IsType<CommonAce>(acl[0]);
    Assert.Equal(new SecurityIdentifier("S-1-3-4"), owner.SecurityIdentifier);
    Assert.Equal(0x00101001, owner.AccessMask);
    var system = Assert.IsType<CommonAce>(acl[1]);
    Assert.Equal(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), system.SecurityIdentifier);
    Assert.Equal(0x1FFFFF, system.AccessMask);
  }

  [Fact]
  public void Apply_DeniesMemoryReadsAndKeepsLimitedQuery()
  {
    var cmd = System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe");
    using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cmd, "/c pause") { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true })!;
    try
    {
      Assert.Equal(0u, ProcessHardening.Apply(child.SafeHandle.DangerousGetHandle()));

      using var identity = WindowsIdentity.GetCurrent();
      if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
      {
        using var reopened = System.Diagnostics.Process.GetProcessById(child.Id);
        Assert.Throws<System.ComponentModel.Win32Exception>(() => reopened.MainModule);
      }
      Assert.Equal(cmd, DesktopIpcService.GetProcessImagePath((uint)child.Id), ignoreCase: true);
    }
    finally
    {
      child.Kill();
    }
  }
}
