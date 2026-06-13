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
    private MainWindow? _host;
    private string _itemId = string.Empty;
    private JsonObject? _item;
    private string? _originalJson;
    private string? _iconBaseUrl;
    private string? _vaultUrl;
    private bool _editing;

    private readonly List<(FieldRow Row, Action<string> WriteBack)> _editable = [];
    private readonly List<FieldRow> _secrets = [];
    private UriListEditor? _uriEditor;
    private ToggleSwitch? _repromptToggle;

    private Dictionary<string, string> _folders = [];
    private Dictionary<string, string> _organizations = [];
    private TextBox? _nameBox;
    private bool _favoriteOn;
    private bool _hasFavoriteControl;
    private ComboBox? _ownerBox;
    private ComboBox? _folderBox;
    private StackPanel? _collectionsPanel;
    private readonly List<(CheckBox Box, string Id)> _collectionChecks = [];

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
        _host = ctx.Host;
        _itemId = ctx.Options.ItemId!;
        _iconBaseUrl = ctx.Options.IconBaseUrl;
        _vaultUrl = ctx.Options.VaultUrl;
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

            (_folders, _organizations) = await _client.GetMetadataAsync();
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
        _uriEditor = null;
        _repromptToggle = null;
        _nameBox = null;
        _hasFavoriteControl = false;
        _ownerBox = null;
        _folderBox = null;
        _collectionsPanel = null;
        _collectionChecks.Clear();

        var item = _item!;
        var type = item["type"]?.GetValue<int>() ?? 0;

        BuildItemDetailsSection(item);

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

    private string? FaviconUrl(JsonObject item)
    {
        var host = string.IsNullOrEmpty(_iconBaseUrl) ? null : FirstHost(item);
        return string.IsNullOrEmpty(host) ? null : $"{_iconBaseUrl}/{host}/icon.png";
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

    // Item details (COMPANION_WINUI_PHASE1 §3.4): in view mode, the item icon + name with its folder
    // (and organization, for org items); in edit mode, the name, owner, folder, collections and the
    // favourite star. Replaces the old title-bar header - the identity lives in this card now.
    private void BuildItemDetailsSection(JsonObject item)
    {
        if (_editing)
        {
            BuildItemDetailsEdit(item);
            return;
        }

        // View mode has no "Item details" title: the card sits flush under the title bar (matching
        // the mockup). Build a bare card.
        var panel = new StackPanel { Spacing = (double)Application.Current.Resources["SpacingSmall"] };
        FieldsPanel.Children.Add(new Border { Style = (Style)Application.Current.Resources["SectionCardStyle"], Child = panel });

        // Name row: favicon + name, no copy.
        var nameRow = new Grid { ColumnSpacing = (double)Application.Current.Resources["SpacingSmall"] };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var favicon = FaviconUrl(item);
        FrameworkElement icon = favicon != null
            ? new Image { Width = 28, Height = 28, Source = SafeBitmap(favicon) }
            : new FontIcon { Glyph = "", FontSize = 22 };
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(icon, 0);
        var nameText = new TextBlock
        {
            Text = item["name"]?.GetValue<string>() ?? "(no name)",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(nameText, 1);
        nameRow.Children.Add(icon);
        nameRow.Children.Add(nameText);
        panel.Children.Add(nameRow);

        // Organization (org items) then folder, each with a leading glyph.
        var orgId = item["organizationId"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(orgId) && _organizations.TryGetValue(orgId, out var orgName))
            panel.Children.Add(IconTextRow("", orgName)); // org/people glyph

        var folderId = item["folderId"]?.GetValue<string>();
        var folderName = !string.IsNullOrEmpty(folderId) && _folders.TryGetValue(folderId, out var fn) ? fn : "No folder";
        panel.Children.Add(IconTextRow("", folderName)); // folder glyph
    }

    private void BuildItemDetailsEdit(JsonObject item)
    {
        // Header row: "Item details" title + favourite star.
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Item details", Style = (Style)Application.Current.Resources["SectionTitleTextStyle"] };
        Grid.SetColumn(title, 0);
        // Favourite toggle: a plain Button whose star glyph flips filled/outline (a ToggleButton
        // can't take the Button-targeted GhostIconButtonStyle, which fail-fasts XAML).
        _favoriteOn = item["favorite"]?.GetValue<bool>() ?? false;
        _hasFavoriteControl = true;
        var star = new FontIcon { Glyph = _favoriteOn ? "" : "", FontSize = 16 };
        var favoriteButton = new Button { Style = (Style)Application.Current.Resources["GhostIconButtonStyle"], Content = star };
        ToolTipService.SetToolTip(favoriteButton, "Favourite");
        favoriteButton.Click += (_, _) => { _favoriteOn = !_favoriteOn; star.Glyph = _favoriteOn ? "" : ""; };
        Grid.SetColumn(favoriteButton, 1);
        header.Children.Add(title);
        header.Children.Add(favoriteButton);
        FieldsPanel.Children.Add(header);

        var stack = new StackPanel { Spacing = (double)Application.Current.Resources["SpacingSmall"] };
        FieldsPanel.Children.Add(new Border { Style = (Style)Application.Current.Resources["SectionCardStyle"], Child = stack });

        _nameBox = new TextBox { Header = "Item name", Text = item["name"]?.GetValue<string>() ?? string.Empty };
        stack.Children.Add(_nameBox);

        // Owner: "You" (no org) or one of the organizations.
        _ownerBox = new ComboBox { Header = "Owner", HorizontalAlignment = HorizontalAlignment.Stretch };
        _ownerBox.Items.Add(new ComboBoxItem { Content = "You", Tag = string.Empty });
        var currentOrg = item["organizationId"]?.GetValue<string>() ?? string.Empty;
        foreach (var (oid, oname) in _organizations)
            _ownerBox.Items.Add(new ComboBoxItem { Content = oname, Tag = oid });
        _ownerBox.SelectedIndex = IndexOfTag(_ownerBox, currentOrg);
        stack.Children.Add(_ownerBox);

        // Folder.
        _folderBox = new ComboBox { Header = "Folder", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (fid, fname) in _folders)
            _folderBox.Items.Add(new ComboBoxItem { Content = fname, Tag = fid });
        _folderBox.Items.Add(new ComboBoxItem { Content = "No folder", Tag = string.Empty });
        _folderBox.SelectedIndex = IndexOfTag(_folderBox, item["folderId"]?.GetValue<string>() ?? string.Empty);
        stack.Children.Add(_folderBox);

        // Collections (org items only). Rebuild when the owner changes.
        _collectionsPanel = new StackPanel { Spacing = (double)Application.Current.Resources["SpacingXSmall"] };
        stack.Children.Add(_collectionsPanel);
        _ownerBox.SelectionChanged += (_, _) => _ = RefreshCollectionsAsync(SelectedTag(_ownerBox), item);
        _ = RefreshCollectionsAsync(currentOrg, item);
    }

    private async Task RefreshCollectionsAsync(string orgId, JsonObject item)
    {
        if (_collectionsPanel == null) return;
        _collectionsPanel.Children.Clear();
        _collectionChecks.Clear();
        if (string.IsNullOrEmpty(orgId) || _client == null) return;

        var collections = await _client.GetCollectionsAsync(orgId);
        if (collections.Count == 0) return;

        var selected = new HashSet<string>();
        if (item["collectionIds"] is JsonArray cids)
            foreach (var c in cids)
                if (c?.GetValue<string>() is { } s) selected.Add(s);

        _collectionsPanel.Children.Add(new TextBlock { Text = "Collections", Style = (Style)Application.Current.Resources["MutedCaptionTextStyle"] });
        foreach (var (cid, cname) in collections)
        {
            var check = new CheckBox { Content = cname, IsChecked = selected.Contains(cid) };
            _collectionChecks.Add((check, cid));
            _collectionsPanel.Children.Add(check);
        }
    }

    private static int IndexOfTag(ComboBox box, string tag)
    {
        for (var i = 0; i < box.Items.Count; i++)
            if (box.Items[i] is ComboBoxItem ci && (ci.Tag as string) == tag) return i;
        return 0;
    }

    private static string SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    private static Microsoft.UI.Xaml.Media.Imaging.BitmapImage? SafeBitmap(string url)
    {
        try { return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(url)); }
        catch { return null; }
    }

    private static Grid IconTextRow(string glyph, string text)
    {
        var row = new Grid { ColumnSpacing = (double)Application.Current.Resources["SpacingSmall"] };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new FontIcon { Glyph = glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 0);
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        row.Children.Add(icon);
        row.Children.Add(label);
        return row;
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

        BuildAutofillSection(login);
    }

    // Autofill options: in edit mode a full URI editor (add/remove/reorder + per-URI match
    // detection); in view mode a read-only list with copy. Reads/writes login["uris"] directly.
    private void BuildAutofillSection(JsonObject login)
    {
        var uris = login["uris"] as JsonArray;
        if (_editing)
        {
            AddSectionHeader("Autofill options");
            _uriEditor = new UriListEditor();
            _uriEditor.Load(uris);
            FieldsPanel.Children.Add(_uriEditor);
        }
        else if (uris is { Count: > 0 })
        {
            var panel = AddSection("Autofill options");
            foreach (var u in uris)
            {
                if (u is not JsonObject uo) continue;
                AddRow(panel, "Website", uo["uri"]?.GetValue<string>(), editable: false);
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

    // Additional options: notes plus the master-password re-prompt toggle (item["reprompt"]:
    // 0 = none, 1 = require the master password to reveal/use the item).
    private void BuildNotesSection(JsonObject item)
    {
        var panel = AddSection("Additional options");

        var row = AddRow(panel, "Notes", item["notes"]?.GetValue<string>(), editable: true);
        _editable.Add((row, v => item["notes"] = string.IsNullOrEmpty(v) ? null : v));

        // Master password re-prompt: edit-only, on a single line (label left, toggle right).
        if (_editing)
        {
            _repromptToggle = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0 };
            _repromptToggle.IsOn = (item["reprompt"]?.GetValue<int>() ?? 0) != 0;

            var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = "Master password re-prompt", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(_repromptToggle, 1);
            grid.Children.Add(label);
            grid.Children.Add(_repromptToggle);
            panel.Children.Add(grid);
        }
    }

    private void BuildHistorySection(JsonObject item)
    {
        var created = item["creationDate"]?.GetValue<string>();
        var revised = item["revisionDate"]?.GetValue<string>();
        if (string.IsNullOrEmpty(created) && string.IsNullOrEmpty(revised)) return;

        var panel = AddSection("Item history");
        if (!string.IsNullOrEmpty(revised))
            AddRow(panel, "Last edited", FormatDate(revised), editable: false, showCopy: false);
        if (!string.IsNullOrEmpty(created))
            AddRow(panel, "Created", FormatDate(created), editable: false, showCopy: false);
    }

    private void AddSectionHeader(string title) =>
        FieldsPanel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["SectionTitleTextStyle"],
        });

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

    private FieldRow AddRow(StackPanel panel, string label, string? value, bool editable, bool secret = false, bool regenerate = false, bool showCopy = true)
    {
        var row = new FieldRow
        {
            Label = label,
            Value = value ?? string.Empty,
            IsSecret = secret,
            ShowRegenerate = regenerate,
            ShowCopy = showCopy,
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

        // Collection-shaped fields can't use the per-row write-backs: rebuild them from their editors.
        if (_uriEditor != null && _item["login"] is JsonObject loginObj)
            loginObj["uris"] = _uriEditor.ToJsonArray();
        if (_repromptToggle != null)
            _item["reprompt"] = _repromptToggle.IsOn ? 1 : 0;

        // Item details edit controls.
        if (_nameBox != null)
            _item["name"] = _nameBox.Text;
        if (_hasFavoriteControl)
            _item["favorite"] = _favoriteOn;
        if (_folderBox != null)
        {
            var folderId = SelectedTag(_folderBox);
            _item["folderId"] = string.IsNullOrEmpty(folderId) ? null : folderId;
        }
        if (_ownerBox != null)
        {
            var orgId = SelectedTag(_ownerBox);
            _item["organizationId"] = string.IsNullOrEmpty(orgId) ? null : orgId;
            if (string.IsNullOrEmpty(orgId))
            {
                _item["collectionIds"] = null;
            }
            else
            {
                var collections = new JsonArray();
                foreach (var (box, cid) in _collectionChecks)
                    if (box.IsChecked == true) collections.Add(cid);
                _item["collectionIds"] = collections;
            }
        }

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
        // Fixed width so the dialog doesn't resize when switching between password and passphrase.
        var generator = new GeneratorControl { Width = 360 };
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

    private void OnWebVaultClick(object sender, RoutedEventArgs e) => _ = OpenInWebVaultAsync();

    private async Task OpenInWebVaultAsync()
    {
        // Prefer the live server URL from the extension (the launch-time value can be empty if the
        // process hadn't resolved it yet); fall back to the launch arg, then the public cloud vault.
        var serverUrl = _client != null ? await _client.GetServerUrlAsync() : null;
        var baseUrl = (serverUrl ?? _vaultUrl) is { Length: > 0 } b ? b.TrimEnd('/') : "https://vault.bitwarden.com";
        var url = $"{baseUrl}/#/vault?itemId={Uri.EscapeDataString(_itemId)}";
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