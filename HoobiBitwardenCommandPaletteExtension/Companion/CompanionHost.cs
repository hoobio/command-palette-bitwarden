using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Companion;

internal sealed partial class CompanionHost : IDisposable
{
  internal static CompanionHost? Current { get; set; }

  private readonly CompanionOps _ops;
  private readonly string _exePath;
  private readonly TimeSpan? _requestTimeout;
  private readonly Func<int, bool> _allowForeground;
  private readonly Lock _lock = new();
  private Process? _process;
  private PipeChannel? _channel;

  public CompanionHost(CompanionOps ops, string? exePath = null, TimeSpan? requestTimeout = null, Func<int, bool>? allowForeground = null)
  {
    _ops = ops;
    _exePath = exePath ?? Path.Combine(AppContext.BaseDirectory, "Companion", "HoobiBitwardenCompanion.exe");
    _requestTimeout = requestTimeout;
    _allowForeground = allowForeground ?? AllowSetForegroundWindow;
    _ops.Locked += NotifyLocked;
    _ops.VaultChanged += NotifyVaultChanged;
    _ops.VaultChanged += Prewarm;
  }

  internal Process? Process => _process;

  // The vault cache loads after warmup and after an unlock, so the companion cold start is paid before the first window is asked for.
  internal void Prewarm()
  {
    if (!_ops.IsUnlocked)
      return;
    try
    {
      EnsureStarted();
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
    {
      DebugLogService.Log("Companion", $"Prewarm failed: {ex.GetType().Name}");
    }
  }

  public void ShowItem(string id) =>
    _ = SendAsync(Ops.ShowItem, IpcJson.ToElement(new ItemRequest(id), IpcJson.Default.ItemRequest));

  public void RotateItem(string id) =>
    _ = SendAsync(Ops.RotateItem, IpcJson.ToElement(new ItemRequest(id), IpcJson.Default.ItemRequest));

  internal async Task<JsonElement?> RequestAsync(string op, JsonElement? payload)
  {
    var channel = EnsureStarted();
    GrantForeground();
    try
    {
      return await channel.RequestAsync(op, payload);
    }
    catch (TimeoutException)
    {
      MarkDead(channel, kill: true);
      throw;
    }
  }

  // Command Palette grants foreground rights to the extension for each command (CoAllowSetForegroundWindow), and only a holder can pass them on before Invoke returns.
  private void GrantForeground()
  {
    int? pid;
    lock (_lock)
      pid = _process?.Id;
    if (pid is { } id && !_allowForeground(id))
      DebugLogService.Log("Companion", $"AllowSetForegroundWindow failed: {Marshal.GetLastPInvokeError()}");
  }

  internal void NotifyLocked() => Notify(Ops.Locked);

  internal void NotifyVaultChanged() => Notify(Ops.VaultChanged);

  private void Notify(string op)
  {
    PipeChannel? channel;
    lock (_lock)
      channel = _channel is { IsClosed: false } ? _channel : null;
    if (channel is not null)
      _ = SendEventAsync(channel, op);
  }

  private static async Task SendEventAsync(PipeChannel channel, string op)
  {
    try
    {
      await channel.SendEventAsync(op);
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Companion", $"{op} event failed: {ex.GetType().Name}");
    }
  }

  private async Task WatchAsync(PipeChannel channel)
  {
    try
    {
      await channel.RunAsync();
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Companion", $"Pipe loop failed: {ex.GetType().Name}");
    }
    finally
    {
      MarkDead(channel);
    }
  }

  private void MarkDead(PipeChannel channel, bool kill = false)
  {
    lock (_lock)
    {
      if (!ReferenceEquals(channel, _channel))
        return;
      if (kill)
        Kill(_process);
      Stop();
    }
  }

  private static void Kill(Process? process)
  {
    try
    {
      process?.Kill(entireProcessTree: true);
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
      DebugLogService.Log("Companion", $"Kill failed: {ex.GetType().Name}");
    }
  }

  private async Task SendAsync(string op, JsonElement? payload)
  {
    try
    {
      await RequestAsync(op, payload);
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Companion", $"{op} failed: {ex.GetType().Name}: {ex.Message}");
    }
  }

  internal PipeChannel EnsureStarted()
  {
    lock (_lock)
    {
      if (_channel is { IsClosed: false } && _process is { HasExited: false })
        return _channel;

      Stop();
      var toCompanion = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
      var fromCompanion = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
      try
      {
        try
        {
          var startInfo = new ProcessStartInfo(_exePath) { UseShellExecute = false };
          startInfo.ArgumentList.Add(toCompanion.GetClientHandleAsString());
          startInfo.ArgumentList.Add(fromCompanion.GetClientHandleAsString());
          _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Companion process did not start");
        }
        finally
        {
          toCompanion.DisposeLocalCopyOfClientHandle();
          fromCompanion.DisposeLocalCopyOfClientHandle();
        }
      }
      catch
      {
        toCompanion.Dispose();
        fromCompanion.Dispose();
        throw;
      }

      var channel = new PipeChannel(fromCompanion, toCompanion, _ops.HandleAsync, log: message => DebugLogService.Log("Companion", message), requestTimeout: _requestTimeout);
      _channel = channel;
      DebugLogService.Log("Companion", $"Started companion process {_process.Id}");
      _ = WatchAsync(channel);
      return channel;
    }
  }

  private void Stop()
  {
    _channel?.Dispose();
    _channel = null;
    _process?.Dispose();
    _process = null;
  }

  public void Dispose()
  {
    _ops.Locked -= NotifyLocked;
    _ops.VaultChanged -= NotifyVaultChanged;
    _ops.VaultChanged -= Prewarm;
    lock (_lock)
      Stop();
    _ops.Dispose();
  }

  [LibraryImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool AllowSetForegroundWindow(int dwProcessId);
}
