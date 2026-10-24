using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension.Services;

// Command Palette hides on every deactivation unless its window is disabled, the exception it keeps for extension dialogs (MainWindow.xaml.cs Deactivated handler, PowerToys v0.102).
internal static partial class CommandPaletteModalScope
{
  private const string HostProcessName = "Microsoft.CmdPal.UI";
  private const string MainWindowClass = "WinUIDesktopWin32WindowClass";
  private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);
  private static readonly TimeSpan DialogWait = TimeSpan.FromSeconds(30);
  private static readonly TimeSpan DeactivationSettle = TimeSpan.FromSeconds(1);

  internal static IDisposable Enter()
  {
    var hwnd = FindVisibleMainWindow();
    if (hwnd == 0)
    {
      DebugLogService.Log("ModalScope", "Command Palette window not visible; not holding it open");
      return new Scope(0);
    }

    EnableWindow(hwnd, false);
    DebugLogService.Log("ModalScope", IsWindowEnabled(hwnd) ? "Could not disable the Command Palette window" : "Command Palette window disabled for the Windows Hello prompt");
    var scope = new Scope(hwnd);
    _ = Task.Run(() => ReenableAfterDialogTakesForegroundAsync(scope));
    return scope;
  }

  private static async Task ReenableAfterDialogTakesForegroundAsync(Scope scope)
  {
    var started = Stopwatch.GetTimestamp();
    while (!scope.Released && GetForegroundWindow() == scope.Hwnd && Stopwatch.GetElapsedTime(started) < DialogWait)
      await Task.Delay(PollInterval);
    if (scope.Released)
      return;
    DebugLogService.Log("ModalScope", $"Foreground left Command Palette after {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}ms");
    await Task.Delay(DeactivationSettle);
    scope.Release("dialog has the foreground");
  }

  private sealed partial class Scope(nint hwnd) : IDisposable
  {
    private int _released;

    public nint Hwnd { get; } = hwnd;

    public bool Released => Volatile.Read(ref _released) != 0;

    public void Release(string reason)
    {
      if (Hwnd == 0 || Interlocked.Exchange(ref _released, 1) != 0)
        return;
      EnableWindow(Hwnd, true);
      DebugLogService.Log("ModalScope", $"Command Palette window re-enabled ({reason})");
    }

    public void Dispose()
    {
      Release("Windows Hello finished");
      if (Hwnd != 0)
        _ = Task.Run(LogOutcomeAsync);
    }

    private async Task LogOutcomeAsync()
    {
      await Task.Delay(TimeSpan.FromMilliseconds(500));
      var visible = IsWindowVisible(Hwnd) && !IsCloaked(Hwnd);
      DebugLogService.Log("ModalScope", $"After Windows Hello: Command Palette visible={visible}, foreground={GetForegroundWindow() == Hwnd}");
    }
  }

  private static unsafe nint FindVisibleMainWindow()
  {
    nint found = 0;
    EnumWindows(&FindMainWindowProc, (nint)(&found));
    return found;
  }

  [UnmanagedCallersOnly]
  private static unsafe int FindMainWindowProc(nint hwnd, nint lParam)
  {
    try
    {
      if (IsWindowVisible(hwnd) && !IsCloaked(hwnd) && HasClass(hwnd, MainWindowClass) && IsHostProcess(hwnd))
      {
        *(nint*)lParam = hwnd;
        return 0;
      }
    }
    catch { }
    return 1;
  }

  private static unsafe bool HasClass(nint hwnd, string className)
  {
    var buffer = stackalloc char[64];
    var length = GetClassNameW(hwnd, buffer, 64);
    return new ReadOnlySpan<char>(buffer, length).SequenceEqual(className);
  }

  private static bool IsHostProcess(nint hwnd)
  {
    GetWindowThreadProcessId(hwnd, out var pid);
    using var process = Process.GetProcessById((int)pid);
    return process.ProcessName.Equals(HostProcessName, StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsCloaked(nint hwnd) =>
    DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

  private const uint DWMWA_CLOAKED = 14;

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool IsWindowEnabled(nint hWnd);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool IsWindowVisible(nint hWnd);

  [LibraryImport("user32.dll")]
  private static partial nint GetForegroundWindow();

  [LibraryImport("user32.dll")]
  private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

  [LibraryImport("user32.dll")]
  private static unsafe partial int GetClassNameW(nint hWnd, char* lpClassName, int nMaxCount);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> lpEnumFunc, nint lParam);

  [LibraryImport("dwmapi.dll")]
  private static partial int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);
}
