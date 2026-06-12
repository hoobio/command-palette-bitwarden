using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanionIpc;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HoobiBitwardenCompanion.Views;

// Quick Rotate window (COMPANION_WINUI_PHASE1 §3.8). Generates a new secret for an item's single
// hidden field, then persists+verifies (§3.6) and copies it for pasting downstream. The clipboard
// copy happens only after the save is confirmed on the server, so it can't imply an unsaved value.
public sealed partial class QuickRotatePage : Page
{
    private VaultClient? _client;
    private string _itemId = string.Empty;

    public QuickRotatePage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = LoadAsync(e.Parameter);
    }

    private async Task LoadAsync(object? parameter)
    {
        if (parameter is not CompanionContext { Client: { } client } ctx || string.IsNullOrEmpty(ctx.Options.ItemId))
        {
            ShowStatus("No item to rotate, or not connected to the Bitwarden extension.", isError: true);
            RotateButton.IsEnabled = false;
            return;
        }

        _client = client;
        _itemId = ctx.Options.ItemId!;

        var item = await _client.GetItemAsync(_itemId);
        if (item == null)
        {
            ShowStatus("Could not load the item. Is the vault unlocked?", isError: true);
            RotateButton.IsEnabled = false;
            return;
        }

        SubheaderText.Text = $"Generate a new secret for “{item["name"]?.GetValue<string>() ?? "this item"}”.";

        // Validate up front that there's exactly one secret to rotate.
        if (!VaultSecretMutations.TrySetSingleHiddenSecret(item, "probe", out var error))
        {
            ShowStatus(error ?? "This item cannot be quick-rotated.", isError: true);
            RotateButton.IsEnabled = false;
            return;
        }

        Generator.Generator = _client.GenerateAsync;
        await Generator.InitializeAsync();
    }

    private void OnRotateClick(object sender, RoutedEventArgs e) => _ = RotateAsync();

    private async Task RotateAsync()
    {
        if (_client == null) return;

        var value = Generator.Value;
        if (string.IsNullOrEmpty(value))
        {
            ShowStatus("Generate a value first.", isError: true);
            return;
        }

        // Re-fetch fresh so we don't write a stale copy back.
        var item = await _client.GetItemAsync(_itemId);
        if (item == null)
        {
            ShowStatus("Could not re-load the item. Nothing was changed.", isError: true);
            return;
        }

        if (!VaultSecretMutations.TrySetSingleHiddenSecret(item, value, out var error))
        {
            ShowStatus(error ?? "Cannot rotate this item.", isError: true);
            return;
        }

        RotateButton.IsEnabled = false;
        ShowStatus("Saving and verifying…", isError: false);
        var (ok, saveError, _) = await _client.SaveItemAsync(_itemId, item, [value]);
        RotateButton.IsEnabled = true;

        if (!ok)
        {
            ShowStatus(saveError ?? "Rotation failed. The new value was NOT saved - it has not been copied.", isError: true);
            return;
        }

        ClipboardHelper.Copy(value);
        ShowStatus("Rotated, verified on the server, and copied to the clipboard.", isError: false);
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        StatusText.Visibility = Visibility.Visible;
    }
}
