using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.UI;
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

        // Theme-aware Mica, matching the Earmark house style. The Window-level SystemBackdrop
        // tracks the app theme on its own, so no manual controller wiring is needed.
        SystemBackdrop = new MicaBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtonColors();
        UpdateCaptionButtonColors();

        Resize(720, 900);
        _ = InitializeAsync();
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

    private void Resize(int width, int height)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        width = Math.Min(width, area.WorkArea.Width - 80);
        height = Math.Min(height, area.WorkArea.Height - 80);
        AppWindow.Resize(new SizeInt32(width, height));
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
