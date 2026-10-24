using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class VaultViewModel : ObservableObject, IDisposable
{
  private const string AllGlyph = "";
  private const string StarGlyph = "";
  private const string FolderGlyph = "";
  private const string CollectionGlyph = "";
  private const string OrganizationGlyph = "";
  private const string ArchiveGlyph = "";
  private const string TrashGlyph = "";

  private static readonly (int Type, string Label)[] Types = [(1, "Login"), (3, "Card"), (4, "Identity"), (2, "Secure note"), (5, "SSH key")];

  private readonly PipeChannel _channel;
  private readonly Action<string> _openItem;
  private readonly DispatcherTimer _searchTimer;
  private List<ItemRowViewModel> _all = [];
  private bool _reloading;
  private bool _syncing;

  public VaultViewModel(PipeChannel channel, Action<string> openItem)
  {
    _channel = channel;
    _openItem = openItem;
    _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
    _searchTimer.Tick += (_, _) => ApplySearch();
  }

  internal bool IsSearchPending => _searchTimer.IsEnabled;

  internal void ApplySearch()
  {
    _searchTimer.Stop();
    ApplyFilter(reset: true);
  }

  [ObservableProperty]
  public partial AvaloniaList<ItemRowViewModel> Rows { get; private set; } = [];

  [ObservableProperty]
  public partial IReadOnlyList<FilterViewModel> Filters { get; private set; } = [];

  [ObservableProperty]
  public partial FilterViewModel? SelectedFilter { get; set; }

  [ObservableProperty]
  public partial string SearchText { get; set; } = string.Empty;

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(OpenSelectedCommand))]
  public partial ItemRowViewModel? SelectedRow { get; set; }

  [ObservableProperty]
  public partial ItemViewModel? Detail { get; private set; }

  [ObservableProperty]
  public partial bool IsLocked { get; private set; }

  [ObservableProperty]
  public partial bool IsEmpty { get; private set; }

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  public async Task ReloadAsync()
  {
    try
    {
      var items = IpcJson.Read(await _channel.RequestAsync(Ops.ListItems, null), IpcJson.Default.ItemListResponse);
      var filters = IpcJson.Read(await _channel.RequestAsync(Ops.ListFilters, null), IpcJson.Default.FiltersResponse);
      Apply(items.Items, filters);
      if (Detail is { } detail)
        await detail.LoadAsync();
    }
    catch (IpcException ex) when (ex.Code == IpcErrors.Locked)
    {
      OnLocked();
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      ErrorMessage = ItemViewModel.Describe(ex);
    }
  }

  internal void Apply(IReadOnlyList<ItemSummary> items, FiltersResponse filters)
  {
    IsLocked = false;
    ErrorMessage = null;
    var previous = _all.DistinctBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
    _all = [.. items
      .Select(s => previous.TryGetValue(s.Id, out var row) && row.IsSameVersion(s) ? row : new ItemRowViewModel(s))
      .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
      .ThenBy(r => r.Id, StringComparer.Ordinal)];

    _reloading = true;
    try
    {
      var selected = SelectedFilter;
      Filters = BuildFilters(filters, items);
      SelectedFilter = Filters.FirstOrDefault(f => f == selected) ?? Filters[0];
    }
    finally
    {
      _reloading = false;
    }

    ApplyFilter(reset: false);
  }

  internal static List<FilterViewModel> BuildFilters(FiltersResponse filters, IReadOnlyList<ItemSummary> items)
  {
    var list = new List<FilterViewModel>
    {
      new(FilterKind.All, "All items", AllGlyph),
      new(FilterKind.Favorites, "Favourites", StarGlyph),
      new(FilterKind.Header, "Types"),
    };
    list.AddRange(Types.Select(t => new FilterViewModel(FilterKind.Type, t.Label, ItemViewModel.GlyphFor(t.Type), Type: t.Type)));
    list.Add(new(FilterKind.Header, "Folders"));
    list.AddRange(filters.Folders.Select(f => new FilterViewModel(FilterKind.Folder, f.Name, FolderGlyph, f.Id)));
    list.Add(new(FilterKind.Folder, "No folder", FolderGlyph));
    if (filters.Collections.Count > 0)
    {
      list.Add(new(FilterKind.Header, "Collections"));
      list.AddRange(filters.Collections.Select(c => new FilterViewModel(FilterKind.Collection, c.Name, CollectionGlyph, c.Id)));
    }

    if (filters.Organizations.Count > 0)
    {
      list.Add(new(FilterKind.Header, "Organisations"));
      list.AddRange(filters.Organizations.Select(o => new FilterViewModel(FilterKind.Organization, o.Name, OrganizationGlyph, o.Id)));
    }

    list.Add(new(FilterKind.Header, string.Empty));
    if (items.Any(i => i.Archived))
      list.Add(new(FilterKind.Archive, "Archive", ArchiveGlyph));
    list.Add(new(FilterKind.Trash, "Trash", TrashGlyph));
    return list;
  }

  internal static List<ItemRowViewModel> Filter(IReadOnlyList<ItemRowViewModel> rows, FilterViewModel? filter, string query)
  {
    var result = new List<ItemRowViewModel>();
    foreach (var row in rows)
    {
      if ((filter?.Matches(row.Summary) ?? true) && row.Matches(query))
        result.Add(row);
    }

    return result;
  }

  internal void ApplyFilter(bool reset)
  {
    var selectedId = SelectedRow?.Id ?? Detail?.Id;
    var result = Filter(_all, SelectedFilter, SearchText.Trim());
    _syncing = true;
    try
    {
      if (reset)
        Rows = new AvaloniaList<ItemRowViewModel>(result);
      else
      {
        Sync(Rows, result);
      }
    }
    finally
    {
      _syncing = false;
    }

    IsEmpty = result.Count == 0;
    var match = selectedId is null ? null : result.Find(r => string.Equals(r.Id, selectedId, StringComparison.OrdinalIgnoreCase));
    SelectedRow = match;
    ShowDetail(match);
  }

  internal static void Sync(AvaloniaList<ItemRowViewModel> target, List<ItemRowViewModel> source)
  {
    if (target.Count == 0)
    {
      target.AddRange(source);
      return;
    }

    var keep = new HashSet<ItemRowViewModel>(source);
    for (var i = target.Count - 1; i >= 0; i--)
    {
      if (!keep.Contains(target[i]))
        target.RemoveAt(i);
    }

    for (var i = 0; i < source.Count; i++)
    {
      if (i >= target.Count || !ReferenceEquals(target[i], source[i]))
        target.Insert(i, source[i]);
    }
  }

  partial void OnSelectedFilterChanged(FilterViewModel? value)
  {
    if (!_reloading)
      ApplyFilter(reset: true);
  }

  partial void OnSearchTextChanged(string value)
  {
    _searchTimer.Stop();
    _searchTimer.Start();
  }

  partial void OnSelectedRowChanged(ItemRowViewModel? value)
  {
    if (!_syncing)
      ShowDetail(value);
  }

  private void ShowDetail(ItemRowViewModel? value)
  {
    if (Detail is { IsEditing: true } || string.Equals(Detail?.Id, value?.Id, StringComparison.OrdinalIgnoreCase))
      return;
    SetDetail(value is null ? null : new ItemViewModel(value.Id, _channel));
    _ = Detail?.LoadAsync();
  }

  private void SetDetail(ItemViewModel? detail)
  {
    if (Detail is { } old)
    {
      old.Removed -= OnDetailRemoved;
      old.Discarded -= OnDetailRemoved;
      old.Saved -= OnDetailSaved;
      old.CloneRequested = null;
      old.Dispose();
    }

    Detail = detail;
    if (detail is null)
      return;
    detail.Removed += OnDetailRemoved;
    detail.Discarded += OnDetailRemoved;
    detail.Saved += OnDetailSaved;
    detail.CloneRequested = ShowNew;
  }

  private void OnDetailRemoved()
  {
    SetDetail(null);
    SelectedRow = null;
  }

  private void OnDetailSaved() => _ = ReloadAsync();

  private void ShowNew(EditableItem item, FiltersResponse filters)
  {
    SetDetail(ItemViewModel.ForNew(_channel, item, filters));
    SelectedRow = null;
    SearchText = string.Empty;
    _searchTimer.Stop();
    if (SelectedFilter is { Kind: not FilterKind.All } && Filters.Count > 0)
      SelectedFilter = Filters[0];
    else
      ApplyFilter(reset: true);
  }

  [RelayCommand]
  private async Task NewItemAsync(string type)
  {
    try
    {
      var filters = IpcJson.Read(await _channel.RequestAsync(Ops.ListFilters, null), IpcJson.Default.FiltersResponse);
      if (Detail is { IsEditing: true } editing && !await editing.TryEndEditAsync())
        return;
      ShowNew(ItemEditorViewModel.Blank(int.Parse(type, System.Globalization.CultureInfo.InvariantCulture)), filters);
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      ErrorMessage = ItemViewModel.Describe(ex);
    }
  }

  [RelayCommand]
  private Task CloneAsync() => Detail is { IsReadOnly: true } detail ? detail.CloneCommand.ExecuteAsync(null) : Task.CompletedTask;

  internal void MoveSelection(int delta)
  {
    if (Rows.Count == 0)
      return;
    var index = SelectedRow is null ? (delta > 0 ? -1 : Rows.Count) : Rows.IndexOf(SelectedRow);
    SelectedRow = Rows[Math.Clamp(index + delta, 0, Rows.Count - 1)];
  }

  [RelayCommand(CanExecute = nameof(CanOpenSelected))]
  private void OpenSelected()
  {
    if (SelectedRow is { } row)
      _openItem(row.Id);
  }

  private bool CanOpenSelected() => SelectedRow is not null;

  internal void ClearSearch()
  {
    SearchText = string.Empty;
    ApplySearch();
  }

  internal void OnLocked()
  {
    _searchTimer.Stop();
    Detail?.OnLocked();
    SetDetail(null);
    SelectedRow = null;
    Rows.Clear();
    _all = [];
    Filters = [];
    IsLocked = true;
    ErrorMessage = null;
  }

  public void Dispose()
  {
    _searchTimer.Stop();
    SetDetail(null);
  }
}
