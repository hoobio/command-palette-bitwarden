using System.IO;
using System.IO.Pipes;
using System.Text.Json;
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
      host.NotifyLocked();
      Assert.Equal(IpcErrors.Invalid, (await Assert.ThrowsAsync<IpcException>(() => host.RequestAsync("unknown", null))).Code);

      host.Dispose();

      await companion.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
      Assert.True(companion.HasExited);
    }
    finally
    {
      host.Dispose();
    }
  }

  [Fact]
  public async Task Request_GrantsTheCompanionForegroundRights_BeforeItReturns()
  {
    var granted = new List<int>();
    using var host = new CompanionHost(new CompanionOps(new BitwardenCliService()), allowForeground: pid =>
    {
      granted.Add(pid);
      return false;
    });

    var request = host.RequestAsync("unknown", null);

    Assert.Equal([host.Process!.Id], granted);
    Assert.Equal(IpcErrors.Invalid, (await Assert.ThrowsAsync<IpcException>(() => request.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken))).Code);
  }

  [Fact]
  public void NotifyLocked_DoesNotStartTheCompanion()
  {
    using var host = new CompanionHost(new CompanionOps(new BitwardenCliService()));
    host.NotifyLocked();
    Assert.Null(host.Process);
  }

  [Fact]
  public void Prewarm_WhenLockedOrLoggedOut_DoesNotStartTheCompanion()
  {
    var ops = new CompanionOps(new BitwardenCliService());
    using var host = new CompanionHost(ops);
    host.Prewarm();
    ops.RaiseVaultChanged();
    Assert.Null(host.Process);
  }

  [Fact]
  public async Task Prewarm_AfterTheCacheLoads_StartsTheCompanionOnce_WithNoWindow()
  {
    var service = new BitwardenCliService();
    service.SetSession("session-key");
    var ops = new CompanionOps(service);
    var host = new CompanionHost(ops);
    try
    {
      ops.RaiseVaultChanged();
      var started = host.Process!.Id;
      ops.RaiseVaultChanged();
      host.Prewarm();
      Assert.Equal(started, host.Process!.Id);
      using var companion = System.Diagnostics.Process.GetProcessById(started);
      Assert.Equal(IpcErrors.Invalid, (await Assert.ThrowsAsync<IpcException>(() => host.RequestAsync("unknown", null).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken))).Code);
      companion.Refresh();
      Assert.Equal(IntPtr.Zero, companion.MainWindowHandle);

      host.Dispose();
      await companion.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
    }
    finally
    {
      host.Dispose();
    }
  }

  [Fact]
  public void Prewarm_WithoutTheCompanionExe_LogsAndDoesNotThrow()
  {
    var service = new BitwardenCliService();
    service.SetSession("session-key");
    using var host = new CompanionHost(new CompanionOps(service), exePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"));
    host.Prewarm();
    Assert.Null(host.Process);
  }

  [Fact]
  public void ShowApiKeyLogin_WithoutTheCompanionExe_DoesNotThrow()
  {
    using var host = new CompanionHost(new CompanionOps(new BitwardenCliService()), exePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"));
    host.ShowApiKeyLogin();
    Assert.Null(host.Process);
  }

  [Fact]
  public async Task TimedOutRequest_KillsTheCompanion_BeforeStartingANewOne()
  {
    using var host = new CompanionHost(new CompanionOps(new BitwardenCliService()), requestTimeout: TimeSpan.FromMilliseconds(1));
    host.EnsureStarted();
    using var companion = System.Diagnostics.Process.GetProcessById(host.Process!.Id);
    // The exit code is readable later only through a handle opened while the process runs.
    _ = companion.Handle;

    var ex = await Assert.ThrowsAsync<TimeoutException>(() => host.RequestAsync("unknown", null).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));

    Assert.Equal("unknown timed out", ex.Message);
    Assert.Null(host.Process);
    await companion.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
    Assert.Equal(-1, companion.ExitCode);

    host.EnsureStarted();
    Assert.NotEqual(companion.Id, host.Process!.Id);
  }

  [Fact]
  public async Task TrimmedCompanion_ShowsAnItemWindow_AndExitsOnPipeEof()
  {
    var exePath = Path.Combine(AppContext.BaseDirectory, "Companion", "HoobiBitwardenCompanion.exe");
    var itemId = Guid.NewGuid().ToString();
    var itemRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var item = new ItemDto(itemId, "Smoke", 1, null, null, false, false, false, null, DateTime.UtcNow, null,
      [new FieldDto(ItemFields.Username, "Username", "Login", "smoke-user", false)]);
    var toCompanion = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
    var fromCompanion = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
    var startInfo = new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = false };
    startInfo.ArgumentList.Add(toCompanion.GetClientHandleAsString());
    startInfo.ArgumentList.Add(fromCompanion.GetClientHandleAsString());
    using var companion = System.Diagnostics.Process.Start(startInfo)!;
    toCompanion.DisposeLocalCopyOfClientHandle();
    fromCompanion.DisposeLocalCopyOfClientHandle();
    var channel = new PipeChannel(fromCompanion, toCompanion, (op, _, _) =>
    {
      if (op != Ops.GetItem)
        throw new IpcException(IpcErrors.Invalid);
      itemRequested.TrySetResult();
      return Task.FromResult<JsonElement?>(IpcJson.ToElement(item, IpcJson.Default.ItemDto));
    });
    var loop = channel.RunAsync();
    try
    {
      var timeout = TimeSpan.FromSeconds(60);
      await channel.RequestAsync(Ops.ShowItem, IpcJson.ToElement(new ItemRequest(itemId), IpcJson.Default.ItemRequest), TestContext.Current.CancellationToken).WaitAsync(timeout, TestContext.Current.CancellationToken);
      await itemRequested.Task.WaitAsync(timeout, TestContext.Current.CancellationToken);
      await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
      Assert.False(companion.HasExited);

      channel.Dispose();
      await toCompanion.DisposeAsync();
      await fromCompanion.DisposeAsync();

      await companion.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
      Assert.True(companion.HasExited);
    }
    finally
    {
      channel.Dispose();
      await toCompanion.DisposeAsync();
      await fromCompanion.DisposeAsync();
      if (!companion.HasExited)
        companion.Kill();
      await loop;
    }
  }
}
