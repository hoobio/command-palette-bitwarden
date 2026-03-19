using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal interface ICliProcess : IDisposable
{
  StreamReader StandardOutput { get; }
  StreamReader StandardError { get; }
  StreamWriter StandardInput { get; }
  int ExitCode { get; }
  int Id { get; }
  bool EnableRaisingEvents { get; set; }
  event EventHandler? Exited;
  Task WaitForExitAsync(CancellationToken cancellationToken = default);
  void Kill(bool entireProcessTree = false);
}

internal sealed partial class RealCliProcess(Process process) : ICliProcess
{
  public StreamReader StandardOutput => process.StandardOutput;
  public StreamReader StandardError => process.StandardError;
  public StreamWriter StandardInput => process.StandardInput;
  public int ExitCode => process.ExitCode;
  public int Id => process.Id;
  public bool EnableRaisingEvents
  {
    get => process.EnableRaisingEvents;
    set => process.EnableRaisingEvents = value;
  }
  public event EventHandler? Exited
  {
    add => process.Exited += value;
    remove => process.Exited -= value;
  }
  public Task WaitForExitAsync(CancellationToken cancellationToken = default) =>
      process.WaitForExitAsync(cancellationToken);
  public void Kill(bool entireProcessTree = false) => process.Kill(entireProcessTree);
  public void Dispose() => process.Dispose();
}

internal delegate ICliProcess CliProcessFactory(ProcessStartInfo psi);
