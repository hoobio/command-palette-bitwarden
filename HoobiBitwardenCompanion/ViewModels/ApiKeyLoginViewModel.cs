using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class ApiKeyLoginViewModel(PipeChannel channel) : ObservableObject
{
  internal event Action? LoggedIn;

  [ObservableProperty]
  public partial string? ClientId { get; set; }

  [ObservableProperty]
  public partial string? ClientSecret { get; set; }

  [ObservableProperty]
  public partial string? ErrorMessage { get; private set; }

  [ObservableProperty]
  public partial bool IsBusy { get; private set; }

  [RelayCommand]
  internal async Task SubmitAsync()
  {
    if (IsBusy)
      return;
    ErrorMessage = null;
    IsBusy = true;
    try
    {
      var request = new ApiKeyLoginRequest(ClientId?.Trim() ?? string.Empty, ClientSecret?.Trim() ?? string.Empty);
      var reply = IpcJson.Read(await channel.RequestAsync(Ops.ApiKeyLogin, IpcJson.ToElement(request, IpcJson.Default.ApiKeyLoginRequest), Timeout.InfiniteTimeSpan), IpcJson.Default.ApiKeyLoginResponse);
      if (reply.Success)
      {
        ClientId = null;
        ClientSecret = null;
        LoggedIn?.Invoke();
      }
      else
      {
        ErrorMessage = reply.Error ?? "Login failed";
      }
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      ErrorMessage = ItemViewModel.Describe(ex);
    }
    finally
    {
      IsBusy = false;
    }
  }
}
