using CommunityToolkit.Mvvm.ComponentModel;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class ApiKeyLoginViewModel(PipeChannel channel) : DialogViewModel(channel)
{
  [ObservableProperty]
  public partial string? ClientId { get; set; }

  [ObservableProperty]
  public partial string? ClientSecret { get; set; }

  internal override void ClearSecrets()
  {
    ClientId = null;
    ClientSecret = null;
  }

  protected override string BusyMessage => "Logging in with your API key...";

  protected override async Task<bool> RunStepAsync()
  {
    var request = new ApiKeyLoginRequest(ClientId?.Trim() ?? string.Empty, ClientSecret?.Trim() ?? string.Empty);
    var reply = IpcJson.Read(await Channel.RequestAsync(Ops.ApiKeyLogin, IpcJson.ToElement(request, IpcJson.Default.ApiKeyLoginRequest), Timeout.InfiniteTimeSpan), IpcJson.Default.ApiKeyLoginResponse);
    if (reply.Success)
      return true;
    ErrorMessage = reply.Error ?? "Login failed";
    return false;
  }
}
