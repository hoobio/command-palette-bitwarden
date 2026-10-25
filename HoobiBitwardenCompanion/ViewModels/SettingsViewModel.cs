using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class SettingsViewModel(PipeChannel channel) : ObservableObject
{
  private string _signature = string.Empty;

  public ObservableCollection<SettingsSectionViewModel> Sections { get; } = [];

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  internal async Task LoadAsync()
  {
    IReadOnlyList<SettingDto> settings;
    try
    {
      settings = IpcJson.Read(await channel.RequestAsync(Ops.GetSettings, null), IpcJson.Default.SettingsResponse).Settings;
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      ErrorMessage = ItemViewModel.Describe(ex);
      return;
    }

    ErrorMessage = null;
    var signature = string.Join('|', settings.Select(s => $"{s.Key}:{s.Kind}"));
    if (signature != _signature)
    {
      _signature = signature;
      Rebuild(settings);
      return;
    }

    var rows = Sections.SelectMany(s => s.Rows).ToDictionary(r => r.Key);
    foreach (var setting in settings.Where(s => s.Kind != SettingKinds.Section))
      rows[setting.Key].Apply(setting);
  }

  private void Rebuild(IReadOnlyList<SettingDto> settings)
  {
    Sections.Clear();
    SettingsSectionViewModel? current = null;
    foreach (var setting in settings)
    {
      if (setting.Kind == SettingKinds.Section)
      {
        Sections.Add(current = new SettingsSectionViewModel(setting.Label));
        continue;
      }

      if (current is null)
        Sections.Add(current = new SettingsSectionViewModel(string.Empty));
      current.Rows.Add(new SettingRowViewModel(channel, setting));
    }
  }
}

internal sealed class SettingsSectionViewModel(string title)
{
  public string Title { get; } = title;

  public bool HasTitle => Title.Length > 0;

  public List<SettingRowViewModel> Rows { get; } = [];
}

internal sealed partial class SettingRowViewModel : ObservableObject
{
  private readonly PipeChannel _channel;
  private string _committed = string.Empty;
  private int _pending;
  private bool _applying;
  private bool _boolValue;
  private ChoiceDto? _selectedChoice;
  private string _textValue = string.Empty;

  public SettingRowViewModel(PipeChannel channel, SettingDto setting)
  {
    _channel = channel;
    Key = setting.Key;
    Kind = setting.Kind;
    Label = setting.Label;
    Description = setting.Description;
    Choices = setting.Choices ?? [];
    Apply(setting);
  }

  public string Key { get; }

  public string Kind { get; }

  public string Label { get; }

  public string? Description { get; }

  public IReadOnlyList<ChoiceDto> Choices { get; }

  public bool HasDescription => !string.IsNullOrEmpty(Description);

  public bool IsToggle => Kind == SettingKinds.Toggle;

  public bool IsChoice => Kind == SettingKinds.Choice;

  public bool IsText => Kind == SettingKinds.Text;

  internal bool IsEditing { get; set; }

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  public bool BoolValue
  {
    get => _boolValue;
    set
    {
      if (SetProperty(ref _boolValue, value) && !_applying)
        _ = SubmitAsync(value ? "true" : "false");
    }
  }

  public ChoiceDto? SelectedChoice
  {
    get => _selectedChoice;
    set
    {
      if (SetProperty(ref _selectedChoice, value) && !_applying && value is not null)
        _ = SubmitAsync(value.Value);
    }
  }

  public string TextValue
  {
    get => _textValue;
    private set => SetProperty(ref _textValue, value);
  }

  internal void Apply(SettingDto setting)
  {
    if (_pending > 0)
      return;
    _committed = setting.Value ?? string.Empty;
    Show(force: false);
  }

  internal Task CommitTextAsync(string text) => text == _committed ? Task.CompletedTask : SubmitAsync(text);

  internal async Task SubmitAsync(string value)
  {
    ErrorMessage = null;
    _pending++;
    string? error;
    try
    {
      var reply = IpcJson.Read(await _channel.RequestAsync(Ops.SetSetting, IpcJson.ToElement(new SetSettingRequest(Key, value), IpcJson.Default.SetSettingRequest)), IpcJson.Default.SetSettingResponse);
      error = reply.Success ? null : reply.Error ?? "The setting was not changed.";
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      error = ItemViewModel.Describe(ex);
    }
    finally
    {
      _pending--;
    }

    if (error is null)
    {
      _committed = value;
      if (IsText)
        TextValue = value;
      return;
    }

    ErrorMessage = error;
    Show(force: true);
    OnPropertyChanged(nameof(TextValue));
  }

  private void Show(bool force)
  {
    _applying = true;
    try
    {
      BoolValue = IsToggle && _committed == "true";
      SelectedChoice = IsChoice ? Choices.FirstOrDefault(c => c.Value == _committed) : null;
      if (IsText && (force || !IsEditing))
        TextValue = _committed;
    }
    finally
    {
      _applying = false;
    }
  }
}
