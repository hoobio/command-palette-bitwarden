using System.Runtime.InteropServices;
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
}
