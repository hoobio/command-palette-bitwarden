using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class SettingsViewModel(PipeChannel channel, Action<string>? open = null) : ObservableObject
{
  private const string RepoUrl = "https://github.com/hoobio/command-palette-bitwarden";
  private const string DonateUrl = "https://www.paypal.com/donate/?hosted_button_id=E2UBLTYAT4K8Y";

  private string _signature = string.Empty;
  private bool _aboutLoaded;
  private IReadOnlyList<InfoSectionViewModel>? _infoSections;

  public ObservableCollection<SettingsSectionViewModel> Sections { get; } = [];

  public IReadOnlyList<InfoSectionViewModel> InfoSections => _infoSections ??= CreateInfoSections();

  private List<InfoSectionViewModel> CreateInfoSections()
  {
    var sections = BuildInfoSections(channel, OpenGuarded);
    foreach (var row in sections.SelectMany(s => s.Rows))
      row.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(InfoRowViewModel.Description))
          RefreshVisibility();
      };
    return sections;
  }

  internal static string LogsDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoobiBitwardenCommandPalette", "logs");

  private static void OpenTarget(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();

  private static List<InfoSectionViewModel> BuildInfoSections(PipeChannel channel, Action<string> open) =>
  [
    new("Help and feedback",
    [
      new LinkRowViewModel("Report a bug", "Opens a pre-filled bug report on GitHub", () => open($"{RepoUrl}/issues/new?template=bug_report.yml")),
      new LinkRowViewModel("Request a feature", "Suggests an improvement", () => open($"{RepoUrl}/issues/new?template=feature_request.yml")),
      new LinkRowViewModel("Project on GitHub", "Source, releases and existing issues", () => open(RepoUrl)),
    ]),
    new("About",
    [
      new AboutRowViewModel(),
      new LinkRowViewModel("Support", "The extension is free and open source. A donation helps keep it maintained", () => open(DonateUrl)),
      new UpdateRowViewModel(channel, open),
    ]),
  ];

  private void OpenGuarded(string target)
  {
    try
    {
      (open ?? OpenTarget)(target);
    }
    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
    {
      ErrorMessage = $"Could not open {target}: {ex.Message}";
    }
  }

  private void OpenLogs()
  {
    Directory.CreateDirectory(LogsDirectory);
    OpenGuarded(LogsDirectory);
  }

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  [ObservableProperty]
  public partial string SearchText { get; set; } = string.Empty;

  partial void OnSearchTextChanged(string value) => RefreshVisibility();

  internal void RefreshVisibility()
  {
    var query = SearchText.Trim();
    var rows = Sections.SelectMany(s => s.Rows).ToList();
    var byKey = rows.ToDictionary(r => r.Key);
    bool ParentOn(SettingRowViewModel row) => row.Parent is null || !byKey.TryGetValue(row.Parent, out var owner)
      || (row.ParentValue == ParentValues.NonEmpty ? !string.IsNullOrWhiteSpace(owner.CurrentValue) : owner.CurrentValue == row.ParentValue);
    bool Matches(SettingRowViewModel row) => TextMatches(query, row.Label, row.Description);

    foreach (var row in rows)
      row.IsShown = ParentOn(row) && Matches(row);

    if (query.Length > 0)
      foreach (var child in rows.Where(r => r.Parent is not null && !ParentOn(r) && Matches(r)))
        if (byKey.TryGetValue(child.Parent!, out var parent))
          parent.IsShown = true;

    foreach (var section in Sections)
      section.IsShown = section.Rows.Any(r => r.IsShown);

    foreach (var section in InfoSections)
    {
      foreach (var row in section.Rows)
        row.IsShown = TextMatches(query, row.Label, row.Description);
      section.IsShown = section.Rows.Any(r => r.IsShown);
    }
  }

  private static bool TextMatches(string query, string label, string? description) => query.Length == 0
    || label.Contains(query, StringComparison.OrdinalIgnoreCase)
    || (description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

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
    await LoadAboutAsync();
    var signature =string.Join('|', settings.Select(s => $"{s.Key}:{s.Kind}:{s.Parent}:{s.ParentValue}"));
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

  private async Task LoadAboutAsync()
  {
    if (_aboutLoaded)
      return;
    AboutResponse about;
    try
    {
      about = IpcJson.Read(await channel.RequestAsync(Ops.GetAbout, null), IpcJson.Default.AboutResponse);
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      return;
    }

    _aboutLoaded = true;
    foreach (var row in InfoSections.SelectMany(s => s.Rows))
    {
      (row as AboutRowViewModel)?.Load(about);
      (row as UpdateRowViewModel)?.Load(about);
    }
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
      var row = new SettingRowViewModel(channel, setting, setting.Key == SettingKeys.OpenLogs ? OpenLogs : null);
      row.ValueChanged += RefreshVisibility;
      current.Rows.Add(row);
    }

    RefreshVisibility();
  }
}

internal sealed partial class SettingsSectionViewModel(string title) : ObservableObject
{
  public string Title { get; } = title;

  [ObservableProperty]
  public partial bool IsShown { get; set; } = true;

  public bool HasTitle => Title.Length > 0;

  public List<SettingRowViewModel> Rows { get; } = [];
}

internal sealed partial class SettingRowViewModel : ObservableObject
{
  private readonly PipeChannel _channel;
  private readonly Action? _activate;
  private string _committed = string.Empty;
  private int _pending;
  private bool _applying;
  private bool _sending;
  private string? _queued;
  private bool _boolValue;
  private ChoiceDto? _selectedChoice;
  private string _textValue = string.Empty;

  public SettingRowViewModel(PipeChannel channel, SettingDto setting, Action? activate = null)
  {
    _channel = channel;
    _activate = activate;
    Key = setting.Key;
    Kind = setting.Kind;
    Label = setting.Label;
    Description = setting.Description;
    Parent = setting.Parent;
    ParentValue = setting.ParentValue;
    Choices = setting.Choices ?? [];
    Apply(setting);
  }

  internal event Action? ValueChanged;

  public string Key { get; }

  public string? Parent { get; }

  public string? ParentValue { get; }

  public bool IsChild => Parent is not null;

  [ObservableProperty]
  public partial bool IsShown { get; set; } = true;

  internal string CurrentValue => Kind switch
  {
    SettingKinds.Toggle => _boolValue ? "true" : "false",
    SettingKinds.Choice => _selectedChoice?.Value ?? _committed,
    _ => _committed,
  };

  public string Kind { get; }

  public string Label { get; }

  public string? Description { get; }

  public IReadOnlyList<ChoiceDto> Choices { get; }

  public bool HasDescription => !string.IsNullOrEmpty(Description);

  public bool IsToggle => Kind == SettingKinds.Toggle;

  public bool IsChoice => Kind == SettingKinds.Choice;

  public bool IsText => Kind == SettingKinds.Text;

  public bool IsLink => Kind == SettingKinds.Link;

  [ObservableProperty]
  public partial string? Placeholder { get; private set; }

  [ObservableProperty]
  public partial string? Tooltip { get; private set; }

  [ObservableProperty]
  public partial bool IsStatusOk { get; private set; }

  [ObservableProperty]
  public partial bool IsStatusError { get; private set; }

  [RelayCommand]
  private void Open() => _activate?.Invoke();

  internal bool IsEditing { get; set; }

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  public bool BoolValue
  {
    get => _boolValue;
    set
    {
      if (!SetProperty(ref _boolValue, value))
        return;
      ValueChanged?.Invoke();
      if (!_applying)
        _ = SubmitAsync(value ? "true" : "false");
    }
  }

  public ChoiceDto? SelectedChoice
  {
    get => _selectedChoice;
    set
    {
      if (!SetProperty(ref _selectedChoice, value))
        return;
      ValueChanged?.Invoke();
      if (!_applying && value is not null)
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
    Placeholder = setting.Placeholder;
    Tooltip = setting.Tooltip;
    IsStatusOk = setting.Status == SettingStatuses.Ok;
    IsStatusError = setting.Status == SettingStatuses.Error;
    var value = setting.Value ?? string.Empty;
    var changed = value != _committed;
    _committed = value;
    Show(force: false);
    if (changed && IsText)
      ValueChanged?.Invoke();
  }

  internal Task CommitTextAsync(string text) => text == _committed ? Task.CompletedTask : SubmitAsync(text);

  // Requests to the extension are handled concurrently, so a quick on-off toggle sent as two requests could be applied in the wrong order.
  internal async Task SubmitAsync(string value)
  {
    if (_sending)
    {
      _queued = value;
      return;
    }

    _sending = true;
    try
    {
      for (var next = value; next is not null; next = TakeQueued())
        await SendAsync(next);
    }
    finally
    {
      _sending = false;
    }
  }

  private string? TakeQueued()
  {
    var queued = _queued;
    _queued = null;
    return queued;
  }

  private async Task SendAsync(string value)
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
      {
        TextValue = value;
        ValueChanged?.Invoke();
      }

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
