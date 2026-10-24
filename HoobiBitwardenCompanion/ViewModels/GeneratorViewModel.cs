using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class GeneratorViewModel : ObservableObject
{
  public static readonly Choice[] UsernameKinds = [new(GeneratorKinds.PlusAddress, "Plus-addressed email"), new(GeneratorKinds.CatchAll, "Catch-all email")];

  private readonly PipeChannel _channel;
  private long? _entryId;

  public GeneratorViewModel(PipeChannel channel, bool canUse = false)
  {
    _channel = channel;
    CanUse = canUse;
    var options = CompanionSettings.LoadGenerator();
    SelectedTab = options.Kind switch
    {
      GeneratorKinds.Passphrase => 1,
      GeneratorKinds.PlusAddress or GeneratorKinds.CatchAll when !canUse => 2,
      _ => 0,
    };
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
    Email = options.Email;
    Domain = options.Domain;
    SelectedUsernameKind = UsernameKinds.FirstOrDefault(k => k.Value == options.Kind) ?? UsernameKinds[0];
  }

  public bool CanUse { get; }

  public bool ShowUsernameTab => !CanUse;

  internal Action<string>? UseRequested { get; set; }

  public ObservableCollection<GeneratedRowViewModel> History { get; } = [];

  public bool HasHistory => History.Count > 0;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsPassword), nameof(IsPassphrase), nameof(IsUsername))]
  public partial int SelectedTab { get; set; }

  public bool IsPassword => SelectedTab == 0;

  public bool IsPassphrase => SelectedTab == 1;

  public bool IsUsername => SelectedTab == 2;

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
  [NotifyPropertyChangedFor(nameof(IsPlusAddress))]
  public partial Choice SelectedUsernameKind { get; set; }

  public bool IsPlusAddress => SelectedUsernameKind.Value == GeneratorKinds.PlusAddress;

  [ObservableProperty]
  public partial string? Email { get; set; }

  [ObservableProperty]
  public partial string? Domain { get; set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(HasValue))]
  [NotifyCanExecuteChangedFor(nameof(CopyCommand), nameof(UseCommand))]
  public partial string? Value { get; private set; }

  public bool HasValue => !string.IsNullOrEmpty(Value);

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  [ObservableProperty]
  public partial string? StatusMessage { get; private set; }

  public string Kind => SelectedTab switch
  {
    1 => GeneratorKinds.Passphrase,
    2 => SelectedUsernameKind.Value!,
    _ => GeneratorKinds.Password,
  };

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
    IncludeNumber,
    Email,
    Domain);

  partial void OnSelectedTabChanged(int value)
  {
    Value = null;
    _entryId = null;
  }

  public async Task InitializeAsync()
  {
    if (!IsUsername)
      await RegenerateAsync();
    await LoadHistoryAsync();
  }

  [RelayCommand]
  private async Task RegenerateAsync()
  {
    var options = ToOptions();
    try
    {
      var generated = IpcJson.Read(await _channel.RequestAsync(Ops.Generate, IpcJson.ToElement(options, IpcJson.Default.GeneratorOptions)), IpcJson.Default.GeneratedResponse);
      Value = generated.Value;
      _entryId = generated.EntryId;
      ErrorMessage = null;
      StatusMessage = null;
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
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

    await LoadHistoryAsync();
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
      StatusMessage = "Copied";
      ErrorMessage = null;
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      ErrorMessage = Describe(ex);
    }
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
    IpcException { Code: IpcErrors.Invalid } => "Check the generator options. A password needs 5 to 128 characters and at least one character type, a passphrase 3 to 20 words, a username a valid email or domain.",
    IpcException { Code: IpcErrors.Failed } => "Bitwarden did not generate a value.",
    _ => ItemViewModel.Describe(ex),
  };
}

internal sealed partial class GeneratedRowViewModel(GeneratorViewModel owner, GeneratedEntry entry)
{
  public string Label => entry.Kind switch
  {
    GeneratorKinds.Passphrase => "Passphrase",
    GeneratorKinds.PlusAddress => "Plus-addressed email",
    GeneratorKinds.CatchAll => "Catch-all email",
    _ => "Password",
  };

  public string Created => entry.Created.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

  [RelayCommand]
  private Task CopyAsync() => owner.CopyEntryAsync(entry.Id);
}
