using System.Collections.ObjectModel;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class HotkeysViewModel(PipeChannel channel) : ObservableObject
{
  private string _signature = string.Empty;

  public ObservableCollection<HotkeyGroupViewModel> Groups { get; } = [];

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  internal event Action<HotkeyRowViewModel>? EditRequested;

  internal void RequestEdit(HotkeyRowViewModel row) => EditRequested?.Invoke(row);

  internal async Task LoadAsync()
  {
    IReadOnlyList<HotkeyGroupDto> groups;
    try
    {
      groups = IpcJson.Read(await channel.RequestAsync(Ops.GetHotkeys, null), IpcJson.Default.HotkeysResponse).Groups;
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      ErrorMessage = ItemViewModel.Describe(ex);
      return;
    }

    ErrorMessage = null;
    var signature = string.Join('|', groups.Select(g => $"{g.ItemType}:{string.Join(',', g.Actions.Select(a => a.Id))}:{string.Join(',', g.EnterChoices.Select(c => c.Value))}"));
    if (signature != _signature)
    {
      _signature = signature;
      Groups.Clear();
      foreach (var group in groups)
        Groups.Add(new HotkeyGroupViewModel(this, group));
      return;
    }

    for (var i = 0; i < groups.Count; i++)
      Groups[i].Apply(groups[i]);
  }

  internal async Task<string?> SetChordAsync(string actionId, ChordDto? chord, bool surfaceError = false)
  {
    var error = await SendAsync(Ops.SetHotkey, IpcJson.ToElement(new SetHotkeyRequest(actionId, chord), IpcJson.Default.SetHotkeyRequest));
    await LoadAsync();
    if (surfaceError)
      ErrorMessage ??= error;
    return error;
  }

  internal async Task<string?> SetEnterAsync(string itemType, string? actionId)
  {
    var error = await SendAsync(Ops.SetEnterAction, IpcJson.ToElement(new SetEnterActionRequest(itemType, actionId), IpcJson.Default.SetEnterActionRequest));
    await LoadAsync();
    ErrorMessage ??= error;
    return error;
  }

  [RelayCommand]
  private async Task ResetAllAsync()
  {
    var error = await SendAsync(Ops.ResetHotkeys, null);
    await LoadAsync();
    ErrorMessage ??= error;
  }

  private async Task<string?> SendAsync(string op, System.Text.Json.JsonElement? payload)
  {
    try
    {
      var reply = IpcJson.Read(await channel.RequestAsync(op, payload), IpcJson.Default.HotkeyResponse);
      return reply.Success ? null : reply.Error ?? "The shortcut was not changed.";
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      return ItemViewModel.Describe(ex);
    }
  }
}

internal sealed partial class HotkeyGroupViewModel : ObservableObject
{
  private readonly HotkeysViewModel _owner;
  private string _inheritedEnter;
  private bool _applying;
  private ChoiceDto? _selectedEnter;

  public HotkeyGroupViewModel(HotkeysViewModel owner, HotkeyGroupDto dto)
  {
    _owner = owner;
    ItemType = dto.ItemType;
    Title = dto.Title;
    _inheritedEnter = dto.InheritedEnter;
    EnterChoices = dto.EnterChoices;
    foreach (var action in dto.Actions)
      Rows.Add(new HotkeyRowViewModel(owner, action));
    Apply(dto);
  }

  public string ItemType { get; }

  public string Title { get; }

  public IReadOnlyList<ChoiceDto> EnterChoices { get; }

  public List<HotkeyRowViewModel> Rows { get; } = [];

  public bool HasRows => Rows.Count > 0;

  public ChoiceDto? SelectedEnter
  {
    get => _selectedEnter;
    set
    {
      if (!SetProperty(ref _selectedEnter, value))
        return;
      OnPropertyChanged(nameof(IsEnterModified));
      if (!_applying && value is not null)
        _ = _owner.SetEnterAsync(ItemType, value.Value);
    }
  }

  public bool IsEnterModified => SelectedEnter is { } selected && selected.Value != _inheritedEnter;

  internal void Apply(HotkeyGroupDto dto)
  {
    _inheritedEnter = dto.InheritedEnter;
    _applying = true;
    try
    {
      SelectedEnter = EnterChoices.FirstOrDefault(c => c.Value == dto.CurrentEnter);
    }
    finally
    {
      _applying = false;
    }

    OnPropertyChanged(nameof(IsEnterModified));

    for (var i = 0; i < Rows.Count; i++)
      Rows[i].Apply(dto.Actions[i]);
  }

  [RelayCommand]
  private async Task ResetEnterAsync() => await _owner.SetEnterAsync(ItemType, null);
}

internal sealed partial class HotkeyRowViewModel : ObservableObject
{
  private readonly HotkeysViewModel _owner;

  public HotkeyRowViewModel(HotkeysViewModel owner, HotkeyActionDto dto)
  {
    _owner = owner;
    ActionId = dto.Id;
    Title = dto.Title;
    Default = dto.Default;
    Apply(dto);
  }

  public string ActionId { get; }

  public string Title { get; }

  public ChordDto? Default { get; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(Caps), nameof(HasChord), nameof(IsModified))]
  public partial ChordDto? Current { get; private set; }

  public IReadOnlyList<string> Caps => Current is { } chord ? HotkeyKeys.Caps(chord.Key, chord.Modifiers) : [];

  public bool HasChord => Current is not null;

  public bool IsModified => Current != Default;

  internal void Apply(HotkeyActionDto dto) => Current = dto.Current;

  [RelayCommand]
  private void Edit() => _owner.RequestEdit(this);

  [RelayCommand]
  private async Task ResetAsync() => await _owner.SetChordAsync(ActionId, Default, surfaceError: true);
}

internal sealed partial class ChordCaptureViewModel : ObservableObject
{
  private readonly HotkeysViewModel _owner;
  private readonly HotkeyRowViewModel _row;
  private int _key;
  private int _modifiers;
  private bool _meta;
  private bool _unsupported;

  public ChordCaptureViewModel(HotkeysViewModel owner, HotkeyRowViewModel row)
  {
    _owner = owner;
    _row = row;
    Set(row.Current);
  }

  public string Title => _row.Title;

  public IReadOnlyList<string> Caps => HotkeyKeys.Caps(_key, _modifiers);

  public bool IsNone => _key == 0 && _modifiers == 0 && !_meta && !_unsupported;

  public bool IsValid => IsNone || (!_meta && !_unsupported && HotkeyKeys.IsKey(_key) && (_modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)) != 0);

  internal ChordDto? Chord => IsNone ? null : new(_key, _modifiers, HotkeyKeys.Display(_key, _modifiers));

  [ObservableProperty]
  public partial string? ErrorMessage { get; set; }

  internal void Press(Key key, KeyModifiers modifiers)
  {
    _key = HotkeyKeyMap.VirtualKeyOf(key);
    _modifiers = HotkeyKeyMap.ModifiersOf(modifiers);
    _meta = modifiers.HasFlag(KeyModifiers.Meta);
    _unsupported = _key == 0 && !HotkeyKeyMap.IsModifierKey(key);
    ErrorMessage = _meta ? "The Windows key cannot be used." : _unsupported ? "This key cannot be used." : null;
    Refresh();
  }

  [RelayCommand]
  private void ResetToDefault() => Set(_row.Default);

  [RelayCommand]
  private void Clear() => Set(null);

  internal async Task<bool> SaveAsync()
  {
    if (!IsValid)
      return false;
    ErrorMessage = await _owner.SetChordAsync(_row.ActionId, Chord);
    return ErrorMessage is null;
  }

  private void Set(ChordDto? chord)
  {
    _key = chord?.Key ?? 0;
    _modifiers = chord?.Modifiers ?? 0;
    _meta = false;
    _unsupported = false;
    ErrorMessage = null;
    Refresh();
  }

  private void Refresh()
  {
    OnPropertyChanged(nameof(Caps));
    OnPropertyChanged(nameof(IsNone));
    OnPropertyChanged(nameof(IsValid));
  }
}
