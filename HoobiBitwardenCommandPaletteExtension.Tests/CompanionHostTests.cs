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
  public async Task TrimmedCompanion_ShowsVaultAndItemWindows_AndExitsOnPipeEof()
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
      await channel.RequestAsync(Ops.ShowVault, null, TestContext.Current.CancellationToken).WaitAsync(timeout, TestContext.Current.CancellationToken);
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
