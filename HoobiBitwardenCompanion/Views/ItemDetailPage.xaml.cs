using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using HoobiBitwardenCompanion.Controls;
using HoobiBitwardenCompanion.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace HoobiBitwardenCompanion.Views;

// Item detail / edit window (COMPANION_WINUI_PHASE1 §3.4). Displays all fields of a vault item via
// reusable FieldRows, supports inline edit, per-field copy, and regenerate on secret fields. Save
// runs the extension's persist+verify+refresh path (§3.6) - success is only reported once the value
// is confirmed on the server.
public sealed partial class ItemDetailPage : Page
{
    private VaultClient? _client;
    private string _itemId = string.Empty;
    private JsonObject? _item;
    private string? _originalJson;
    private string? _iconBaseUrl;
    private bool _editing;

    private readonly List<(FieldRow Row, Action<string> WriteBack)> _editable = [];
    private readonly List<FieldRow> _secrets = [];

    public ItemDetailPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = LoadAsync(e.Parameter);
    }

    private async Task LoadAsync(object? parameter)
    {
        if (parameter is not CompanionContext { Client: { } client } ctx || string.IsNullOrEmpty(ctx.Options.ItemId))
        {
            ShowStatus("No item to show, or not connected to the Bitwarden extension.", isError: true);
            EditButton.IsEnabled = false;
            return;
        }

        _client = client;
        _itemId = ctx.Options.ItemId!;
        _iconBaseUrl = ctx.Options.IconBaseUrl;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        LoadingText.Text = "Loading item details…";
        LoadingPanel.Visibility = Visibility.Visible;
        try
        {
            var item = await _client!.GetItemAsync(_itemId);
            if (item == null)
            {
                ShowStatus("Could not load the item. Is the vault unlocked?", isError: true);
                EditButton.IsEnabled = false;
                return;
            }

            _item = item;
            _originalJson = item.ToJsonString();
            BuildFields();
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildFields()
    {
        FieldsPanel.Children.Clear();
        _editable.Clear();
        _secrets.Clear();

        var item = _item!;
        ItemName.Text = item["name"]?.GetValue<string>() ?? "(no name)";
        var type = item["type"]?.GetValue<int>() ?? 0;
        TrySetFavicon(item);

        AddNameSection(item);

        if (type == 1) // Login
        {
            BuildLoginSection(item);
        }

        BuildCustomFieldsSection(item);
        BuildNotesSection(item);
        BuildHistorySection(item);

        // Only Login + custom fields/notes are editable in Phase 1.
        EditButton.Visibility = _editable.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Show the website favicon from the Bitwarden icon server (base resolved by the extension and
    // passed at launch; empty when the privacy setting is off). The glyph stays until the image loads.
    private void TrySetFavicon(JsonObject item)
    {
        FaviconImage.Visibility = Visibility.Collapsed;
        ItemIcon.Visibility = Visibility.Visible;
        if (string.IsNullOrEmpty(_iconBaseUrl)) return;

        var host = FirstHost(item);
        if (string.IsNullOrEmpty(host)) return;

        try { FaviconImage.Source = new BitmapImage(new Uri($"{_iconBaseUrl}/{host}/icon.png")); }
        catch { /* leave the glyph */ }
    }

    private void OnFaviconOpened(object sender, RoutedEventArgs e)
    {
        FaviconImage.Visibility = Visibility.Visible;
        ItemIcon.Visibility = Visibility.Collapsed;
    }

    private static string? FirstHost(JsonObject item)
    {
        if (item["login"] is not JsonObject login || login["uris"] is not JsonArray uris) return null;
        foreach (var u in uris)
        {
            var uri = (u as JsonObject)?["uri"]?.GetValue<string>();
            if (string.IsNullOrEmpty(uri)) continue;
            if (!uri.Contains("://", StringComparison.Ordinal)) uri = "https://" + uri;
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host))
                return parsed.Host;
        }
        return null;
    }

    private void AddNameSection(JsonObject item)
    {
        var panel = AddSection("Item");
        var name = AddRow(panel, "Name", item["name"]?.GetValue<string>(), editable: true);
        _editable.Add((name, v => item["name"] = v));
    }

    private void BuildLoginSection(JsonObject item)
    {
        var login = item["login"] as JsonObject ?? [];
        if (item["login"] is not JsonObject) item["login"] = login;

        var panel = AddSection("Login credentials");

        var username = AddRow(panel, "Username", login["username"]?.GetValue<string>(), editable: true);
        _editable.Add((username, v => login["username"] = v));

        var password = AddRow(panel, "Password", login["password"]?.GetValue<string>(), editable: true, secret: true, regenerate: true);
        _editable.Add((password, v => login["password"] = v));
        _secrets.Add(password);

        // TOTP: show the live code with a countdown ring when viewing; show the raw seed (editable,
        // masked) only when editing. The user never needs the seed except to change it.
        var totpSeed = login["totp"]?.GetValue<string>();
        if (_editing)
        {
            var seedRow = AddRow(panel, "Authenticator key (TOTP)", totpSeed, editable: true, secret: true);
            _editable.Add((seedRow, v => login["totp"] = string.IsNullOrEmpty(v) ? null : v));
        }
        else if (!string.IsNullOrEmpty(totpSeed))
        {
            panel.Children.Add(new TotpRow(totpSeed));
        }

        if (login["uris"] is JsonArray uris && uris.Count > 0)
        {
            var urlPanel = AddSection("Autofill URLs");
            for (var i = 0; i < uris.Count; i++)
            {
                if (uris[i] is not JsonObject uo) continue;
                var row = AddRow(urlPanel, "Website", uo["uri"]?.GetValue<string>(), editable: true);
                _editable.Add((row, v => uo["uri"] = v));
            }
        }
    }

    private void BuildCustomFieldsSection(JsonObject item)
    {
        if (item["fields"] is not JsonArray fields || fields.Count == 0) return;

        var panel = AddSection("Custom fields");
        foreach (var f in fields)
        {
            if (f is not JsonObject fo) continue;
            var name = fo["name"]?.GetValue<string>() ?? "Field";
            var isHidden = (fo["type"]?.GetValue<int>() ?? 0) == 1;
            var row = AddRow(panel, name, fo["value"]?.GetValue<string>(), editable: true, secret: isHidden, regenerate: isHidden);
            _editable.Add((row, v => fo["value"] = v));
            if (isHidden) _secrets.Add(row);
        }
    }

    private void BuildNotesSection(JsonObject item)
    {
        if (item["type"]?.GetValue<int>() is 1 or 2 || item["notes"]?.GetValue<string>() is { Length: > 0 })
        {
            var panel = AddSection("Notes");
            var row = AddRow(panel, "Notes", item["notes"]?.GetValue<string>(), editable: true);
            _editable.Add((row, v => item["notes"] = string.IsNullOrEmpty(v) ? null : v));
        }
    }

    private void BuildHistorySection(JsonObject item)
    {
        var revised = item["revisionDate"]?.GetValue<string>();
        if (string.IsNullOrEmpty(revised)) return;
        var panel = AddSection("Item history");
        AddRow(panel, "Last edited", FormatDate(revised), editable: false);
    }

    private StackPanel AddSection(string title)
    {
        var stack = new StackPanel { Spacing = (double)Application.Current.Resources["SpacingSmall"] };
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["SectionTitleTextStyle"],
        });
        FieldsPanel.Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["SectionCardStyle"],
            Child = stack,
        });
        return stack;
    }

    private FieldRow AddRow(StackPanel panel, string label, string? value, bool editable, bool secret = false, bool regenerate = false)
    {
        var row = new FieldRow
        {
            Label = label,
            Value = value ?? string.Empty,
            IsSecret = secret,
            ShowRegenerate = regenerate,
            IsEditing = _editing && editable,
        };
        if (regenerate)
            row.RegenerateRequested += (_, _) => _ = RegenerateAsync(row);
        panel.Children.Add(row);
        return row;
    }

    private void OnEditClick(object sender, RoutedEventArgs e) => SetEditing(true);

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        // Restore the pristine copy and rebuild.
        if (_originalJson != null && JsonNode.Parse(_originalJson) is JsonObject pristine)
            _item = pristine;
        SetEditing(false);
    }

    private void SetEditing(bool editing)
    {
        _editing = editing;
        // Rebuild so mode-specific rows swap (e.g. live TOTP code <-> editable seed). BuildFields
        // sets EditButton visibility from the editable count, so fix the button row up afterwards.
        BuildFields();
        EditButton.Visibility = editing || _editable.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => _ = SaveAsync();

    private async Task SaveAsync()
    {
        if (_client == null || _item == null) return;

        foreach (var (row, writeBack) in _editable)
            writeBack(row.CurrentValue);

        var mustContain = _secrets
            .Select(r => r.CurrentValue)
            .Where(v => !string.IsNullOrEmpty(v))
            .ToArray();

        SaveButton.IsEnabled = false;
        var (ok, error, refreshed) = await _client.SaveSteppedAsync(
            _itemId, _item, mustContain, msg => ShowStatus(msg, isError: false));
        SaveButton.IsEnabled = true;

        if (!ok)
        {
            ShowStatus(error ?? "Save failed. The change may not be on the server - do not assume it was saved.", isError: true);
            return;
        }

        if (refreshed != null)
        {
            _item = refreshed;
            _originalJson = refreshed.ToJsonString();
        }
        SetEditing(false);
        ShowStatus("Saved and verified on the server ✓", isError: false);
    }

    private async Task RegenerateAsync(FieldRow row)
    {
        var generator = new GeneratorControl();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Generate",
            Content = generator,
            PrimaryButtonText = "Use this value",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        generator.Initialize();

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrEmpty(generator.Value))
            row.Value = generator.Value;
    }

    private void OnWebVaultClick(object sender, RoutedEventArgs e)
    {
        // Keep the existing web-vault deep-link behaviour (Phase 1 §3.4); self-hosted server URLs
        // are a follow-up once the companion knows the configured server.
        var url = $"https://vault.bitwarden.com/#/vault?itemId={Uri.EscapeDataString(_itemId)}";
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        StatusText.Visibility = Visibility.Visible;
    }

    private static string FormatDate(string iso) =>
        DateTime.TryParse(iso, out var dt) ? dt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture) : iso;
}
