using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace HoobiBitwardenCompanion.Views;

internal static partial class ToastWindowInterop
{
  private const int GwlExStyle = -20;
  private const nint WsExToolWindow = 0x80;
  private const nint WsExNoActivate = 0x08000000;

  internal static void MakeNonActivating(TopLevel window)
  {
    if (window.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: var hwnd })
      SetWindowLongPtrW(hwnd, GwlExStyle, GetWindowLongPtrW(hwnd, GwlExStyle) | WsExToolWindow | WsExNoActivate);
  }

  [LibraryImport("user32.dll")]
  private static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

  [LibraryImport("user32.dll")]
  private static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);
}
