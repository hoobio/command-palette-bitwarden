using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class ServerPickerViewModel(PipeChannel channel, SetServerRequest? initial = null) : ObservableObject
{
  private SetServerRequest _server = initial ?? new SetServerRequest(ServerRegions.Cloud, null);

  internal event Action<SelfHostedServerViewModel>? SelfHostedRequested;

  internal SetServerRequest Current => _server;

  public string Host =>_server.Region switch
  {
    ServerRegions.Cloud => "bitwarden.com",
    ServerRegions.Eu => "bitwarden.eu",
    _ => Uri.TryCreate(_server.BaseUrl, UriKind.Absolute, out var uri) ? uri.Authority : _server.BaseUrl ?? string.Empty,
  };

  public bool IsCloud => _server.Region == ServerRegions.Cloud;

  public bool IsEu => _server.Region == ServerRegions.Eu;

  public bool IsSelfHosted => _server.Region == ServerRegions.SelfHosted;

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  [RelayCommand]
  internal async Task SelectAsync(string region)
  {
    ErrorMessage = null;
    if (region == ServerRegions.SelfHosted)
      SelfHostedRequested?.Invoke(new SelfHostedServerViewModel(_server, ApplyAsync));
    else
      ErrorMessage = await ApplyAsync(new SetServerRequest(region, null));
  }

  private async Task<string?> ApplyAsync(SetServerRequest request)
  {
    SetServerResponse reply;
    try
    {
      reply = IpcJson.Read(await channel.RequestAsync(Ops.SetServer, IpcJson.ToElement(request, IpcJson.Default.SetServerRequest), Timeout.InfiniteTimeSpan), IpcJson.Default.SetServerResponse);
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      return ItemViewModel.Describe(ex);
    }

    if (!reply.Success)
      return reply.Error ?? "Failed to set server URL";
    _server = request;
    OnPropertyChanged(nameof(Host));
    OnPropertyChanged(nameof(IsCloud));
    OnPropertyChanged(nameof(IsEu));
    OnPropertyChanged(nameof(IsSelfHosted));
    return null;
  }
}

internal sealed partial class ServerUrlFieldViewModel(string label, string path, string? value) : ObservableObject
{
  public string Label { get; } = label;

  public string Placeholder { get; } = "{base URL}" + path;

  [ObservableProperty]
  public partial string? Value { get; set; } = value;
}

internal sealed partial class SelfHostedServerViewModel : ObservableObject
{
  private readonly Func<SetServerRequest, Task<string?>> _apply;

  public SelfHostedServerViewModel(SetServerRequest current, Func<SetServerRequest, Task<string?>> apply)
  {
    _apply = apply;
    BaseUrl = current.Region == ServerRegions.SelfHosted ? current.BaseUrl : null;
    Fields =
    [
      new("Web Vault URL", "/", current.WebVaultUrl),
      new("API URL", "/api", current.ApiUrl),
      new("Identity URL", "/identity", current.IdentityUrl),
      new("Icons URL", "/icons", current.IconsUrl),
      new("Notifications URL", "/notifications", current.NotificationsUrl),
      new("Events URL", "/events", current.EventsUrl),
      new("Key Connector URL", "/key-connector", current.KeyConnectorUrl),
    ];
    Expanded = Fields.Any(f => !string.IsNullOrWhiteSpace(f.Value));
  }

  public IReadOnlyList<ServerUrlFieldViewModel> Fields { get; }

  [ObservableProperty]
  public partial string? BaseUrl { get; set; }

  [ObservableProperty]
  public partial bool Expanded { get; set; }

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  internal async Task<bool> SaveAsync()
  {
    ErrorMessage = null;
    string? Override(int index) => string.IsNullOrWhiteSpace(Fields[index].Value) ? null : Fields[index].Value;
    ErrorMessage = await _apply(new SetServerRequest(ServerRegions.SelfHosted, BaseUrl, Override(0), Override(1), Override(2), Override(3), Override(4), Override(5), Override(6)));
    return ErrorMessage is null;
  }
}
