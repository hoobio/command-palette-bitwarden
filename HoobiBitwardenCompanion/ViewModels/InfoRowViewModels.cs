using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal abstract partial class InfoRowViewModel : ObservableObject
{
  protected InfoRowViewModel(string label, string? description = null)
  {
    Label = label;
    Description = description;
  }

  public string Label { get; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(HasDescription))]
  public partial string? Description { get; protected set; }

  [ObservableProperty]
  public partial bool IsShown { get; set; } = true;

  public bool HasDescription => !string.IsNullOrEmpty(Description);
}

internal sealed partial class InfoSectionViewModel(string title, IReadOnlyList<InfoRowViewModel> rows) : ObservableObject
{
  public string Title { get; } = title;

  public IReadOnlyList<InfoRowViewModel> Rows { get; } = rows;

  [ObservableProperty]
  public partial bool IsShown { get; set; } = true;
}

internal sealed partial class LinkRowViewModel(string label, string description, Action open) : InfoRowViewModel(label, description)
{
  [RelayCommand]
  private void Open() => open();
}

internal sealed class AboutRowViewModel() : InfoRowViewModel("Command Palette Extension for Bitwarden")
{
  private const string Copyright = "Copyright (c) Hoobi";

  internal static string Describe(AboutResponse about)
  {
    var channel = about.Channel switch
    {
      AppChannels.Dev => " (Dev)",
      AppChannels.Prerelease => " (Pre-release)",
      _ => string.Empty,
    };
    var commit = about.Commit is { Length: > 0 } hash ? $" - {hash}" : string.Empty;
    return $"Version {about.Version}{channel}{commit}\n{Copyright}";
  }

  internal void Load(AboutResponse about) => Description = Describe(about);
}

internal sealed partial class UpdateRowViewModel(PipeChannel channel, Action<string> open) : InfoRowViewModel("Check for updates")
{
  internal const string DevText = "Update checks are off for local (Dev) builds.";
  internal const string CheckingText = "Checking for updates...";
  internal const string UpToDateText = "You are on the latest version.";
  internal const string FailedText = "Could not check for updates. Try again later.";
  internal const string InstallFailedText = "Could not install the update. Try again later.";
  internal const string InstalledText = "Update installed.";
  internal const string StoreOpenedText = "Opened the Microsoft Store to install the update.";
  internal const string InstallNotCompletedText = "Update did not install. Try again from the Microsoft Store.";
  internal const string StoreUpdatesUri = "ms-windows-store://downloadsandupdates";
  internal const string StoreAvailableText = "An update is available in the Microsoft Store.";
  internal const string DefaultReleaseUrl = "https://github.com/hoobio/command-palette-bitwarden/releases/latest";
  private static readonly TimeSpan StoreTimeout = TimeSpan.FromMinutes(3);

  private bool _isStore;
  private string? _releaseUrl;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CanCheck))]
  public partial bool IsBusy { get; private set; }

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CanCheck))]
  public partial bool IsOff { get; private set; }

  [ObservableProperty]
  public partial bool UpdateAvailable { get; private set; }

  [ObservableProperty]
  public partial string ActionText { get; private set; } = "View release";

  public bool CanCheck => !IsBusy && !IsOff;

  internal void Load(AboutResponse about)
  {
    _isStore = about.IsStore;
    if (about.Channel != AppChannels.Dev)
      return;
    IsOff = true;
    Description = DevText;
  }

  [RelayCommand]
  private async Task CheckAsync()
  {
    if (!CanCheck)
      return;
    IsBusy = true;
    UpdateAvailable = false;
    Description = CheckingText;
    try
    {
      Apply(IpcJson.Read(await channel.RequestAsync(Ops.CheckUpdates, null, StoreTimeout), IpcJson.Default.UpdateResponse));
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      Description = FailedText;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task ActionAsync()
  {
    if (!_isStore)
    {
      open(_releaseUrl ?? DefaultReleaseUrl);
      return;
    }

    try
    {
      var install = IpcJson.Read(await channel.RequestAsync(Ops.InstallUpdate, null, StoreTimeout), IpcJson.Default.InstallResponse);
      if (install.State == InstallStates.OpenStore)
        open(StoreUpdatesUri);
      UpdateAvailable = install.State != InstallStates.Installed;
      Description = install.State switch
      {
        InstallStates.Installed => InstalledText,
        InstallStates.OpenStore => StoreOpenedText,
        _ => InstallNotCompletedText,
      };
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      Description = InstallFailedText;
    }
  }

  private void Apply(UpdateResponse response)
  {
    _isStore = response.IsStore;
    _releaseUrl = response.ReleaseUrl;
    ActionText = response.IsStore ? "Install update" : "View release";
    UpdateAvailable = response.State == UpdateStates.Available;
    Description = response.State switch
    {
      UpdateStates.Off => DevText,
      UpdateStates.UpToDate => UpToDateText,
      UpdateStates.Available => response.IsStore ? StoreAvailableText : $"Update available: {response.LatestVersion}",
      _ => FailedText,
    };
  }
}
