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
        // Tall caption buttons to match the pinned 48px TitleBar height (so the caption and the
        // reserved Row 0 are the same height and content sits cleanly below them).
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        // Window icon (title bar + Alt-Tab) from the loose .ico.
        try { AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); }
        catch { /* best-effort */ }

        // Taskbar icon: a packaged window normally takes it from the app tile logo via its AUMID, but
        // that doesn't resolve for a Process.Start-launched window, so set it directly on the window's
        // property store via RelaunchIconResource (pointing at the exe's embedded icon).
        TrySetTaskbarIcon();

        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtonColors();
        UpdateCaptionButtonColors();

        // Size for the mode: the generator (Generate / Quick Rotate) is tall, the item view fits
        // through login credentials by default and scrolls the rest. A DPI-aware minimum keeps the
        // essentials visible as the user resizes.
        var (width, height) = _options.Mode switch
        {
            CompanionMode.Generate or CompanionMode.QuickRotate => (460, 620),
            CompanionMode.Login => (420, 560),
            _ => (440, 440),
        };
        ResizeAndCenter(width, height);
        EnforceMinimumSize(380, 400);
        Activated += OnFirstActivated;
        _ = InitializeAsync();
    }

    // Enforce a minimum window size via WM_GETMINMAXINFO (a window subclass), the same way the OS
    // limits any resizable window - so the drag stops cleanly at the minimum instead of the visual
    // artifacting you get from re-growing the window after the fact.
    private SubclassProc? _subclassProc; // kept alive for the lifetime of the window
    private int _minWidthPx, _minHeightPx;

    private void EnforceMinimumSize(int minWidthDip, int minHeightDip)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        _minWidthPx = (int)(minWidthDip * scale);
        _minHeightPx = (int)(minHeightDip * scale);
        _subclassProc = MinSizeSubclassProc;
        SetWindowSubclass(hwnd, _subclassProc, 1, IntPtr.Zero);
    }

    private IntPtr MinSizeSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize.X = _minWidthPx;
            info.ptMinTrackSize.Y = _minHeightPx;
            Marshal.StructureToPtr(info, lParam, false);
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // Set the title-bar icon + name for the current item (the item windows have no separate header
    // row). Uses the TitleBar's leading IconSource + Title slots so the icon + name sit at the left
    // and are vertically centred by the control; falls back to a generic glyph when there's no favicon.
    public void SetItemHeader(string? faviconUrl, string title)
    {
        IconSource? icon = null;
        if (!string.IsNullOrEmpty(faviconUrl))
        {
            try { icon = new ImageIconSource { ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(faviconUrl)) }; }
            catch { /* fall through to the glyph */ }
        }
        icon ??= new FontIconSource { Glyph = "" };

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

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _toastTimer;

    private void OnClipboardCopied(string label) => DispatcherQueue.TryEnqueue(() => ShowCopyToast(label));

    // Show the toast ("Copied X to clipboard") and schedule it back out. No Storyboard - a code-built
    // one fail-fasts the dispatcher if the path doesn't bind; a plain Opacity set is enough.
    private void ShowCopyToast(string label)
    {
        ToastText.Text = string.IsNullOrEmpty(label) ? "Copied to clipboard" : $"Copied {label} to clipboard";
        CopyToast.Opacity = 1;
        _toastTimer ??= DispatcherQueue.CreateTimer();
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _toastTimer.IsRepeating = false;
        _toastTimer.Tick -= OnToastTick;
        _toastTimer.Tick += OnToastTick;
        _toastTimer.Start();
    }

    private void OnToastTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
    {
        CopyToast.Opacity = 0;
        sender.Stop();
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

    // Set the window's taskbar icon directly via the AppUserModel property store, pointing at the
    // exe's embedded icon - the packaged tile-logo path doesn't resolve for our Process.Start window.
    private void TrySetTaskbarIcon()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store == null) return;
            try
            {
                var exe = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "HoobiBitwardenCompanion.exe");
                SetStringProperty(store, PKEY_AppUserModel_RelaunchIconResource, $"{exe},0");
                store.Commit();
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch { /* best-effort cosmetic */ }
    }

    private static void SetStringProperty(IPropertyStore store, PROPERTYKEY key, string value)
    {
        if (InitPropVariantFromString(value, out var pv) != 0) return;
        try { store.SetValue(ref key, ref pv); }
        finally { _ = PropVariantClear(ref pv); }
    }

    private static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchIconResource = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 3,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPVARIANT { public ushort vt; public ushort w1, w2, w3; public IntPtr p; public IntPtr p2; }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, out IPropertyStore propertyStore);

    [DllImport("propsys.dll")]
    private static extern int InitPropVariantFromString([MarshalAs(UnmanagedType.LPWStr)] string psz, out PROPVARIANT ppropvar);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    private const uint WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

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
