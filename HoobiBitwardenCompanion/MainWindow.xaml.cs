using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.Views;
using Windows.Graphics;

namespace HoobiBitwardenCompanion;

[SuppressMessage("Reliability", "CA1001", Justification = "_ipc is disposed in the Closed handler; Window cannot implement IDisposable.")]
public sealed partial class MainWindow : Window
{
    private readonly LaunchOptions _options;
    private ExtensionIpcClient? _ipc;

    internal MainWindow(LaunchOptions options)
    {
        _options = options;
        InitializeComponent();

        // Share the extension's clipboard behaviour and surface a "copied" toast for in-app copies.
        ClipboardHelper.Configure(_options.ClipboardAutoClear, _options.ClipboardClearSeconds);
        ClipboardHelper.Copied += OnClipboardCopied;
        Closed += (_, _) =>
        {
            ClipboardHelper.Copied -= OnClipboardCopied;
            _ipc?.Dispose();
        };

        ApplyBackdrop(_options.Backdrop);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Taskbar / window icon (the package logo). The window is launched by the extension, not its
        // tile, so set it explicitly or there's no taskbar icon. Resolve relative to the install root.
        try { AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); }
        catch { /* best-effort */ }

        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtonColors();
        UpdateCaptionButtonColors();

        // Compact default that fits the title bar, item details and login credentials; the rest of
        // the fields scroll. A DPI-aware minimum keeps those always visible as the user resizes.
        ResizeAndCenter(460, 560);
        EnforceMinimumSize(420, 500);
        Activated += OnFirstActivated;
        _ = InitializeAsync();
    }

    // Clamp the window so it can't be resized below the minimum that keeps the header + item + login
    // visible. AppWindow has no built-in minimum, so re-grow it from the Changed event.
    private void EnforceMinimumSize(int minWidthDip, int minHeightDip)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var minWidth = (int)(minWidthDip * scale);
        var minHeight = (int)(minHeightDip * scale);

        AppWindow.Changed += (sender, args) =>
        {
            if (!args.DidSizeChange) return;
            var size = sender.Size;
            var width = Math.Max(size.Width, minWidth);
            var height = Math.Max(size.Height, minHeight);
            if (width != size.Width || height != size.Height)
                sender.Resize(new SizeInt32(width, height));
        };
    }

    // Set the title-bar icon + name for the current item (the item windows have no separate header
    // row). Falls back to a generic glyph when there's no favicon.
    public void SetItemHeader(IconSource? icon, string title)
    {
        AppTitleBar.IconSource = icon;
        AppTitleBar.Title = title;
    }

    // Bring the freshly launched window to the foreground on its current monitor. It's spawned by the
    // extension (a different process), so the OS won't let a bare SetForegroundWindow steal focus -
    // a brief topmost toggle plus a restore is the reliable way to pull it to the front.
    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        BringToFront();
    }

    private void BringToFront()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, SW_SHOWNORMAL);
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            SetForegroundWindow(hwnd);
        }
        catch { /* best-effort */ }
    }

    private void OnClipboardCopied() => DispatcherQueue.TryEnqueue(ShowCopyToast);

    private void ShowCopyToast()
    {
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var fadeIn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 1, Duration = TimeSpan.FromMilliseconds(120) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeIn, CopyToast);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeIn, "Opacity");
        var fadeOut = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), BeginTime = TimeSpan.FromMilliseconds(1300) };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeOut, CopyToast);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeOut, "Opacity");
        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(fadeOut);
        storyboard.Begin();
    }

    private async Task InitializeAsync()
    {
        VaultClient? client = null;
        if (!string.IsNullOrEmpty(_options.PipeName))
        {
            _ipc = new ExtensionIpcClient(_options.PipeName);
            try
            {
                await _ipc.ConnectAsync();
                client = new VaultClient(_ipc);
            }
            catch (Exception ex)
            {
                ContentFrame.Navigate(typeof(PlaceholderPage),
                    $"Could not connect to the Bitwarden extension.\n{ex.Message}");
                return;
            }
        }

        var context = new CompanionContext(client, _options, Close, this);
        switch (_options.Mode)
        {
            case CompanionMode.Login:
                ContentFrame.Navigate(typeof(LoginPage), context);
                break;
            case CompanionMode.Generate:
                ContentFrame.Navigate(typeof(GeneratePage), context);
                break;
            case CompanionMode.ItemDetail:
                ContentFrame.Navigate(typeof(ItemDetailPage), context);
                break;
            case CompanionMode.QuickRotate:
                ContentFrame.Navigate(typeof(QuickRotatePage), context);
                break;
            default:
                ContentFrame.Navigate(typeof(PlaceholderPage),
                    $"Mode '{_options.Mode}' is not built yet (Phase 1 in progress).");
                break;
        }
    }

    private void ResizeAndCenter(int width, int height)
    {
        // Center on the monitor under the cursor (where the user is working), not the primary.
        var area = GetCursorPos(out var cursor)
            ? DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest)
            : DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        width = Math.Min(width, work.Width - 80);
        height = Math.Min(height, work.Height - 80);
        var x = work.X + ((work.Width - width) / 2);
        var y = work.Y + ((work.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int SW_SHOWNORMAL = 1;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hWnd);

    // Window background material, chosen in the Command Palette settings and passed at launch.
    // Mica/Acrylic use the built-in Window backdrops (they track the app theme themselves); Solid
    // drops the backdrop and paints an opaque themed fill so the window stays readable.
    private void ApplyBackdrop(BackdropMode mode)
    {
        switch (mode)
        {
            case BackdropMode.Acrylic when DesktopAcrylicController.IsSupported():
                SystemBackdrop = new DesktopAcrylicBackdrop();
                break;
            case BackdropMode.Mica when MicaController.IsSupported():
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
                break;
            default:
                SystemBackdrop = null;
                RootGrid.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
                break;
        }
    }

    private void UpdateCaptionButtonColors()
    {
        if (AppWindow?.TitleBar is not { } titleBar) return;

        var isLight = RootGrid.ActualTheme == ElementTheme.Light;
        var foreground = isLight ? Colors.Black : Colors.White;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
    }
}
