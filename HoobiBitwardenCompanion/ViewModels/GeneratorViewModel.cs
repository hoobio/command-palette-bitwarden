using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class GeneratorViewModel : ObservableObject
{
  private readonly PipeChannel _channel;
  private long? _entryId;

  public GeneratorViewModel(PipeChannel channel)
  {
    _channel = channel;
    var options = CompanionSettings.LoadGenerator();
    SelectedTab = options.Kind == GeneratorKinds.Passphrase ? 1 : 0;
    Length = options.Length;
    Uppercase = options.Uppercase;
    Lowercase = options.Lowercase;
    Numbers = options.Numbers;
    Special = options.Special;
    MinNumber = options.MinNumber;
    MinSpecial = options.MinSpecial;
    AvoidAmbiguous = options.AvoidAmbiguous;
    Words = options.Words;
    Separator = options.Separator;
    Capitalize = options.Capitalize;
    IncludeNumber = options.IncludeNumber;
  }

  internal Action<string>? UseRequested { get; set; }

  internal long? EntryId => _entryId;

  public FieldViewModel? CurrentPassword { get; init; }

  public bool HasCurrentPassword => CurrentPassword is not null;

  public FieldViewModel? Website { get; init; }

  public bool HasWebsite => Website is not null;

  public FieldViewModel? Username { get; init; }

  public bool HasUsername => Username is not null;

  internal Action<string>? Toast { get; set; }

  public ObservableCollection<GeneratedRowViewModel> History { get; } = [];

  public bool HasHistory => History.Count > 0;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsPassword), nameof(IsPassphrase))]
  public partial int SelectedTab { get; set; }

  public bool IsPassword => SelectedTab == 0;

  public bool IsPassphrase => SelectedTab == 1;

  [ObservableProperty]
  public partial decimal? Length { get; set; }

  [ObservableProperty]
  public partial bool Uppercase { get; set; }

  [ObservableProperty]
  public partial bool Lowercase { get; set; }

  [ObservableProperty]
  public partial bool Numbers { get; set; }

  [ObservableProperty]
  public partial bool Special { get; set; }

  [ObservableProperty]
  public partial decimal? MinNumber { get; set; }

  [ObservableProperty]
  public partial decimal? MinSpecial { get; set; }

  [ObservableProperty]
  public partial bool AvoidAmbiguous { get; set; }

  [ObservableProperty]
  public partial decimal? Words { get; set; }

  [ObservableProperty]
  public partial string? Separator { get; set; }

  [ObservableProperty]
  public partial bool Capitalize { get; set; }

  [ObservableProperty]
  public partial bool IncludeNumber { get; set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(HasValue))]
  [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(UseCommand))]
  public partial string? Value { get; private set; }

  public bool HasValue => !string.IsNullOrEmpty(Value);

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  [ObservableProperty]
  public partial string? StatusMessage { get; private set; }

  public string Kind => SelectedTab == 1 ? GeneratorKinds.Passphrase : GeneratorKinds.Password;

  public GeneratorOptions ToOptions() => new(
    Kind,
    (int)(Length ?? 0),
    Uppercase,
    Lowercase,
    Numbers,
    Special,
    (int)(MinNumber ?? 0),
    (int)(MinSpecial ?? 0),
    AvoidAmbiguous,
    (int)(Words ?? 0),
    Separator ?? string.Empty,
    Capitalize,
    IncludeNumber);

  private static readonly HashSet<string> OptionNames =
  [
    nameof(SelectedTab), nameof(Length), nameof(Uppercase), nameof(Lowercase), nameof(Numbers), nameof(Special), nameof(MinNumber),
    nameof(MinSpecial), nameof(AvoidAmbiguous), nameof(Words), nameof(Separator), nameof(Capitalize), nameof(IncludeNumber),
  ];

  private bool _ready;
  private int _request;

  protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
  {
    base.OnPropertyChanged(e);
    if (_ready && e.PropertyName is { } name && OptionNames.Contains(name))
      _ = RegenerateAsync();
  }

  public async Task InitializeAsync()
  {
    await RegenerateAsync();
    _ready = true;
    await LoadHistoryAsync();
  }

  [RelayCommand]
  private async Task RegenerateAsync()
  {
    var options = ToOptions();
    var request = ++_request;
    try
    {
      var generated = IpcJson.Read(await _channel.RequestAsync(Ops.Generate, IpcJson.ToElement(options, IpcJson.Default.GeneratorOptions)), IpcJson.Default.GeneratedResponse);
      if (request != _request)
        return;
      Value = generated.Value;
      _entryId = generated.EntryId;
      ErrorMessage = null;
      StatusMessage = null;
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      if (request == _request)
        ErrorMessage = Describe(ex);
      return;
    }

    try
    {
      CompanionSettings.SaveGenerator(options);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      StatusMessage = "The generator options were not saved.";
    }
  }

  [RelayCommand(CanExecute = nameof(HasValue))]
  private Task CopyAsync() => _entryId is { } id ? CopyEntryAsync(id) : Task.CompletedTask;

  [RelayCommand(CanExecute = nameof(HasValue))]
  private void Use()
  {
    if (Value is { Length: > 0 } value)
      UseRequested?.Invoke(value);
  }

  internal async Task CopyEntryAsync(long id)
  {
    try
    {
      await _channel.RequestAsync(Ops.CopyGenerated, IpcJson.ToElement(new HistoryEntryRequest(id), IpcJson.Default.HistoryEntryRequest));
      ErrorMessage = null;
      Toast?.Invoke("Copied");
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      ErrorMessage = Describe(ex);
      return;
    }

    await LoadHistoryAsync();
  }

  internal async Task LoadHistoryAsync()
  {
    try
    {
      var history = IpcJson.Read(await _channel.RequestAsync(Ops.ListGeneratorHistory, null), IpcJson.Default.GeneratorHistoryResponse);
      History.Clear();
      foreach (var entry in history.Entries)
        History.Add(new GeneratedRowViewModel(this, entry));
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      History.Clear();
      ErrorMessage = Describe(ex);
    }

    OnPropertyChanged(nameof(HasHistory));
  }

  internal void OnLocked()
  {
    Value = null;
    _entryId = null;
    History.Clear();
    OnPropertyChanged(nameof(HasHistory));
    ErrorMessage = ItemViewModel.Describe(new IpcException(IpcErrors.Locked));
  }

  private static string Describe(Exception ex) => ex switch
  {
    IpcException { Code: IpcErrors.Invalid } => "Check the generator options. A password needs 5 to 128 characters and at least one character type, a passphrase 3 to 20 words.",
    IpcException { Code: IpcErrors.Failed } => "These options cannot make a value. Lower the minimums or raise the length.",
    _ => ItemViewModel.Describe(ex),
  };
}

internal sealed partial class GeneratedRowViewModel(GeneratorViewModel owner, GeneratedEntry entry)
{
  public string Label => entry.Kind == GeneratorKinds.Passphrase ? "Passphrase" : "Password";

  public string Created => entry.Created.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

  [RelayCommand]
  private Task CopyAsync() => owner.CopyEntryAsync(entry.Id);
}
