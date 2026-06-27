using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HoobiBitwardenCompanion.Controls;

// Editable list of login autofill URIs (COMPANION_WINUI_PHASE1 §3.4): each row is a website with a
// per-URI match-detection mode; rows can be added, removed and drag-reordered. Reads/writes the raw
// login["uris"] JsonArray so unmodelled fields round-trip untouched.
public sealed partial class UriListEditor : UserControl
{
    private sealed record MatchOption(string Label, int? Value);

    // Bitwarden URI match-detection values (login.uris[].match): null = use the global default. The
    // last two sit under a non-selectable "Advanced options" category in the dropdown.
    private static readonly MatchOption[] StandardOptions =
    [
        new("Default", null),
        new("Base domain", 0),
        new("Host", 1),
        new("Exact", 3),
        new("Never", 5),
    ];

    private static readonly MatchOption[] AdvancedOptions =
    [
        new("Starts with", 2),
        new("Regular expression", 4),
    ];

    private sealed record Row(TextBox UriBox, ComboBox MatchBox);

    private readonly ObservableCollection<Border> _cards = [];
    private readonly Dictionary<Border, Row> _rows = [];

    public UriListEditor()
    {
        InitializeComponent();
        RowsList.ItemsSource = _cards;
    }

    public void Load(JsonArray? uris)
    {
        _cards.Clear();
        _rows.Clear();
        if (uris != null)
        {
            foreach (var node in uris)
            {
                if (node is not JsonObject o) continue;
                AddRow(o["uri"]?.GetValue<string>() ?? string.Empty, o["match"]?.GetValue<int>());
            }
        }
    }

    public JsonArray ToJsonArray()
    {
        var array = new JsonArray();
        foreach (var card in _cards) // ObservableCollection order reflects any drag-reordering
        {
            if (!_rows.TryGetValue(card, out var row)) continue;
            var uri = row.UriBox.Text?.Trim();
            if (string.IsNullOrEmpty(uri)) continue;
            var match = (row.MatchBox.SelectedItem as ComboBoxItem)?.Tag as int?;
            array.Add(new JsonObject
            {
                ["uri"] = uri,
                ["match"] = match.HasValue ? JsonValue.Create(match.Value) : null,
            });
        }
        return array;
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => AddRow(string.Empty, null);

    private void AddRow(string uri, int? match)
    {
        var uriBox = new TextBox
        {
            Text = uri,
            PlaceholderText = "https://example.com",
            Header = "Website (URI)",
        };

        var matchBox = new ComboBox
        {
            Header = "Match detection",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        PopulateMatchOptions(matchBox, match);

        var removeButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostIconButtonStyle"],
            Content = new FontIcon { Glyph = "", FontSize = 16 }, // Remove
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        ToolTipService.SetToolTip(removeButton, "Remove");

        var dragHandle = new FontIcon
        {
            Glyph = "", // GripBarDots
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2, 0, 0, 8),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        };
        ToolTipService.SetToolTip(dragHandle, "Drag to reorder");

        var gearButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostIconButtonStyle"],
            Content = new FontIcon { Glyph = "", FontSize = 16 }, // Settings
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        ToolTipService.SetToolTip(gearButton, "Match detection options");

        var topRow = new Grid { ColumnSpacing = 4 };
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(uriBox, 0);
        Grid.SetColumn(gearButton, 1);
        Grid.SetColumn(removeButton, 2);
        Grid.SetColumn(dragHandle, 3);
        topRow.Children.Add(uriBox);
        topRow.Children.Add(gearButton);
        topRow.Children.Add(removeButton);
        topRow.Children.Add(dragHandle);

        var helper = new TextBlock
        {
            Text = "URI match detection is how Bitwarden identifies autofill suggestions.",
            Style = (Style)Application.Current.Resources["MutedCaptionTextStyle"],
            TextWrapping = TextWrapping.Wrap,
        };

        // Match-detection options live behind the gear: collapsed by default, shown when the gear is
        // toggled or when the URI already has a non-default match mode.
        var matchSection = new StackPanel
        {
            Spacing = (double)Application.Current.Resources["SpacingXSmall"],
            Visibility = match.HasValue ? Visibility.Visible : Visibility.Collapsed,
        };
        matchSection.Children.Add(matchBox);
        matchSection.Children.Add(helper);
        gearButton.Click += (_, _) =>
            matchSection.Visibility = matchSection.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

        var stack = new StackPanel { Spacing = (double)Application.Current.Resources["SpacingSmall"] };
        stack.Children.Add(topRow);
        stack.Children.Add(matchSection);

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["SectionCardStyle"],
            Child = stack,
        };

        removeButton.Click += (_, _) => { _cards.Remove(card); _rows.Remove(card); };

        _rows[card] = new Row(uriBox, matchBox);
        _cards.Add(card);
    }

    private static void PopulateMatchOptions(ComboBox box, int? selected)
    {
        ComboBoxItem? toSelect = null;

        foreach (var option in StandardOptions)
        {
            var item = new ComboBoxItem { Content = option.Label, Tag = option.Value };
            box.Items.Add(item);
            if (option.Value == selected) toSelect = item;
        }

        // Non-selectable greyed category header.
        box.Items.Add(new ComboBoxItem { Content = "Advanced options", IsEnabled = false });

        foreach (var option in AdvancedOptions)
        {
            var item = new ComboBoxItem { Content = option.Label, Tag = option.Value };
            box.Items.Add(item);
            if (option.Value == selected) toSelect = item;
        }

        box.SelectedItem = toSelect ?? box.Items[0];
    }
}
