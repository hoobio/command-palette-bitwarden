using System;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using HoobiBitwardenCompanion.Views;
using Windows.Graphics;

namespace HoobiBitwardenCompanion;

public sealed partial class MainWindow : Window
{
    private readonly LaunchOptions _options;

    internal MainWindow(LaunchOptions options)
    {
        _options = options;
        InitializeComponent();

        // Theme-aware Mica, matching the Earmark house style. The Window-level SystemBackdrop
        // tracks the app theme on its own, so no manual controller wiring is needed.
        SystemBackdrop = new MicaBackdrop();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        RootGrid.ActualThemeChanged += (_, _) => UpdateCaptionButtonColors();
        UpdateCaptionButtonColors();

        Resize(720, 900);
        Navigate();
    }

    private void Navigate()
    {
        // Phase 1 skeleton: a single placeholder until the real pages land. The Frame and
        // mode switch are the seams later phases plug login/detail/generate/rotate pages into.
        var description = _options.Mode switch
        {
            CompanionMode.ItemDetail => $"Item detail / edit\nItem id: {_options.ItemId ?? "(none)"}",
            CompanionMode.Generate => "Standalone password generator",
            CompanionMode.QuickRotate => $"Quick Rotate\nItem id: {_options.ItemId ?? "(none)"}",
            _ => "Login / unlock",
        };

        ContentFrame.Navigate(typeof(PlaceholderPage), description);
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
