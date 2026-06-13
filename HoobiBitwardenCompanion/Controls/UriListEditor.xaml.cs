using System.Collections.Generic;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HoobiBitwardenCompanion.Controls;

// Editable list of login autofill URIs (COMPANION_WINUI_PHASE1 §3.4): each row is a website with a
// per-URI match-detection mode; rows can be added, removed and reordered. Reads/writes the raw
// login["uris"] JsonArray so unmodelled fields round-trip untouched.
public sealed partial class UriListEditor : UserControl
{
    // Bitwarden URI match-detection values (login.uris[].match): null = use the global default.
    private sealed record MatchOption(string Label, int? Value);

    private static readonly MatchOption[] MatchOptions =
    [
        new("Default", null),
        new("Base domain", 0),
        new("Host", 1),
        new("Exact", 3),
        new("Never", 5),
        new("Starts with", 2),
        new("Regular expression", 4),
    ];

    private sealed record Row(Border Card, TextBox UriBox, ComboBox MatchBox);

    private readonly List<Row> _rows = [];

    public UriListEditor() => InitializeComponent();

    public void Load(JsonArray? uris)
    {
        RowsPanel.Children.Clear();
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
        foreach (var row in _rows)
        {
            var uri = row.UriBox.Text?.Trim();
            if (string.IsNullOrEmpty(uri)) continue;
            var match = (row.MatchBox.SelectedItem as MatchOption)?.Value;
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
            ItemsSource = MatchOptions,
            DisplayMemberPath = nameof(MatchOption.Label),
            SelectedIndex = IndexOfMatch(match),
        };

        var removeButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostIconButtonStyle"],
            Content = new FontIcon { Glyph = "", FontSize = 16 },
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        ToolTipService.SetToolTip(removeButton, "Remove");

        var upButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostIconButtonStyle"],
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        ToolTipService.SetToolTip(upButton, "Move up");

        var downButton = new Button
        {
            Style = (Style)Application.Current.Resources["GhostIconButtonStyle"],
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        ToolTipService.SetToolTip(downButton, "Move down");

        var topRow = new Grid { ColumnSpacing = 4 };
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(uriBox, 0);
        Grid.SetColumn(upButton, 1);
        Grid.SetColumn(downButton, 2);
        Grid.SetColumn(removeButton, 3);
        topRow.Children.Add(uriBox);
        topRow.Children.Add(upButton);
        topRow.Children.Add(downButton);
        topRow.Children.Add(removeButton);

        var stack = new StackPanel { Spacing = (double)Application.Current.Resources["SpacingSmall"] };
        stack.Children.Add(topRow);
        stack.Children.Add(matchBox);

        var card = new Border
        {
            Style = (Style)Application.Current.Resources["SectionCardStyle"],
            Child = stack,
        };

        var row = new Row(card, uriBox, matchBox);
        removeButton.Click += (_, _) => RemoveRow(row);
        upButton.Click += (_, _) => MoveRow(row, -1);
        downButton.Click += (_, _) => MoveRow(row, +1);

        _rows.Add(row);
        RowsPanel.Children.Add(card);
    }

    private void RemoveRow(Row row)
    {
        _rows.Remove(row);
        RowsPanel.Children.Remove(row.Card);
    }

    private void MoveRow(Row row, int delta)
    {
        var index = _rows.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _rows.Count) return;
        _rows.RemoveAt(index);
        _rows.Insert(target, row);
        RowsPanel.Children.RemoveAt(index);
        RowsPanel.Children.Insert(target, row.Card);
    }

    private static int IndexOfMatch(int? match)
    {
        for (var i = 0; i < MatchOptions.Length; i++)
            if (MatchOptions[i].Value == match) return i;
        return 0;
    }
}
