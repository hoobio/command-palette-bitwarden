using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
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

        Closed += (_, _) => _ipc?.Dispose();

        ApplyBackdrop(_options.Backdrop);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Taskbar / window icon (the package logo). The window is launched by the extension, not its
        // tile, so set it explicitly or there's no taskbar icon. Resolve relative to the install root.
        try { AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); }
        catch { /* best-effort */ }

        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtonColors();
        UpdateCaptionButtonColors();

        ResizeAndCenter(720, 900);
        Activated += OnFirstActivated;
        _ = InitializeAsync();
    }

    // Bring the freshly launched window to the foreground on its current monitor (it's spawned by
    // the extension, so it won't always steal focus on its own).
    private void OnFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        Activated -= OnFirstActivated;
        try { SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)); }
        catch { /* best-effort */ }
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

        var context = new CompanionContext(client, _options, Close);
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
