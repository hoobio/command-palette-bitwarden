using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace HoobiBitwardenCompanion.Views;

internal static partial class WindowAlertInterop
{
  private const uint MbIconHand = 0x10;
  private const uint FlashwAll = 0x3;
  private const uint FlashwTimerNoForeground = 0xC;

  internal static void Raise(TopLevel window)
  {
    MessageBeep(MbIconHand);
    if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var hwnd })
      return;
    var info = new FlashWindowInfo
    {
      Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
      Hwnd = hwnd,
      Flags = FlashwAll | FlashwTimerNoForeground,
    };
    FlashWindowEx(ref info);
  }

  internal static void ReturnForeground(nint hwnd)
  {
    if (hwnd != 0 && IsWindow(hwnd))
      SetForegroundWindow(hwnd);
  }

  internal static void TakeForeground(TopLevel window)
  {
    if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var hwnd })
      return;
    ReturnForeground(hwnd);
    var foreground = GetForegroundWindow();
    if (foreground == hwnd || foreground == 0)
      return;

    // Command Palette Settings submits forms without granting foreground rights, so join the foreground thread's input queue to take focus.
    var foregroundThread = GetWindowThreadProcessId(foreground, out _);
    var ownThread = GetCurrentThreadId();
    if (foregroundThread == 0 || foregroundThread == ownThread || !AttachThreadInput(ownThread, foregroundThread, true))
      return;
    try
    {
      BringWindowToTop(hwnd);
      SetForegroundWindow(hwnd);
    }
    finally
    {
      AttachThreadInput(ownThread, foregroundThread, false);
    }
  }

  internal static PixelPoint? WindowCentre(nint hwnd) =>
    hwnd != 0 && IsWindow(hwnd) && GetWindowRect(hwnd, out var rect)
      ? new PixelPoint((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2)
      : null;

  [StructLayout(LayoutKind.Sequential)]
  private struct Rect
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GetWindowRect(nint hWnd, out Rect lpRect);

  [StructLayout(LayoutKind.Sequential)]
  private struct FlashWindowInfo
  {
    public uint Size;
    public nint Hwnd;
    public uint Flags;
    public uint Count;
    public uint Timeout;
  }

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool MessageBeep(uint uType);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool IsWindow(nint hWnd);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool SetForegroundWindow(nint hWnd);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool FlashWindowEx(ref FlashWindowInfo pwfi);

  [LibraryImport("user32.dll")]
  private static partial nint GetForegroundWindow();

  [LibraryImport("user32.dll")]
  private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

  [LibraryImport("kernel32.dll")]
  private static partial uint GetCurrentThreadId();

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

  [LibraryImport("user32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool BringWindowToTop(nint hWnd);
}
