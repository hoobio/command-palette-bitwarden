using System.IO;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class CompanionHostTests
{
  [Fact]
  public async Task Companion_StartsAsChild_AnswersOverPipes_AndExitsOnPipeEof()
  {
    Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Companion", "HoobiBitwardenCompanion.exe")));
    var host = new CompanionHost(new CompanionOps(new BitwardenCliService()));
    try
    {
      var error = await Assert.ThrowsAsync<IpcException>(() => host.RequestAsync("unknown", null).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
      Assert.Equal(IpcErrors.Invalid, error.Code);
      Assert.Same(host.EnsureStarted(), host.EnsureStarted());
      using var companion = System.Diagnostics.Process.GetProcessById(host.Process!.Id);

      host.Dispose();

      await companion.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
      Assert.True(companion.HasExited);
    }
    finally
    {
      host.Dispose();
    }
  }
}
