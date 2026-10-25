using CommunityToolkit.Mvvm.ComponentModel;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class SetServerViewModel : DialogViewModel
{
  private static readonly string[] Regions = [ServerRegions.Cloud, ServerRegions.Eu, ServerRegions.SelfHosted];

  public SetServerViewModel(PipeChannel channel, string region, string? url) : base(channel)
  {
    RegionIndex = Math.Max(0, Array.IndexOf(Regions, region));
    if (IsSelfHosted)
      BaseUrl = url;
  }

  public static IReadOnlyList<string> RegionChoices { get; } = ["Bitwarden Cloud (bitwarden.com)", "Bitwarden EU (vault.bitwarden.eu)", "Self-hosted"];

  public bool IsSelfHosted => Regions[RegionIndex] == ServerRegions.SelfHosted;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsSelfHosted))]
  public partial int RegionIndex { get; set; }

  [ObservableProperty]
  public partial string? BaseUrl { get; set; }

  [ObservableProperty]
  public partial string? WebVaultUrl { get; set; }

  [ObservableProperty]
  public partial string? ApiUrl { get; set; }

  [ObservableProperty]
  public partial string? IdentityUrl { get; set; }

  [ObservableProperty]
  public partial string? IconsUrl { get; set; }

  [ObservableProperty]
  public partial string? NotificationsUrl { get; set; }

  [ObservableProperty]
  public partial string? EventsUrl { get; set; }

  [ObservableProperty]
  public partial string? KeyConnectorUrl { get; set; }

  protected override string BusyMessage => "Saving the server URL...";

  protected override async Task<bool> RunStepAsync()
  {
    var request = IsSelfHosted
      ? new SetServerRequest(ServerRegions.SelfHosted, BaseUrl, WebVaultUrl, ApiUrl, IdentityUrl, IconsUrl, NotificationsUrl, EventsUrl, KeyConnectorUrl)
      : new SetServerRequest(Regions[RegionIndex], null);
    var reply = IpcJson.Read(await Channel.RequestAsync(Ops.SetServer, IpcJson.ToElement(request, IpcJson.Default.SetServerRequest), Timeout.InfiniteTimeSpan), IpcJson.Default.SetServerResponse);
    if (reply.Success)
      return true;
    ErrorMessage = reply.Error ?? "Failed to set server URL";
    return false;
  }
}
