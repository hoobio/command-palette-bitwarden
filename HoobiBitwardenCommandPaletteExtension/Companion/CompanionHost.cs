using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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
  private readonly Lock _lock = new();
  private Process? _process;
  private PipeChannel? _channel;

  public CompanionHost(CompanionOps ops, string? exePath = null)
  {
    _ops = ops;
    _exePath = exePath ?? Path.Combine(AppContext.BaseDirectory, "Companion", "HoobiBitwardenCompanion.exe");
  }

  internal Process? Process => _process;

  public void ShowItem(string id) =>
    _ = SendAsync(Ops.ShowItem, IpcJson.ToElement(new ItemRequest(id), IpcJson.Default.ItemRequest));

  public void ShowVault() => _ = SendAsync(Ops.ShowVault, null);

  internal Task<JsonElement?> RequestAsync(string op, JsonElement? payload) => EnsureStarted().RequestAsync(op, payload);

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

      _channel = new PipeChannel(fromCompanion, toCompanion, _ops.HandleAsync);
      _ = _channel.RunAsync();
      DebugLogService.Log("Companion", $"Started companion process {_process.Id}");
      return _channel;
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
    lock (_lock)
      Stop();
    _ops.Dispose();
  }
}
