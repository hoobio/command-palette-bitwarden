using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class RepromptViewModel(PipeChannel channel, string token, string itemName, bool windowsHello) : DialogViewModel(channel)
{
  private bool _biometric;
  private bool _confirmed;

  public string Token { get; } = token;

  public string ItemName { get; } = itemName;

  public bool WindowsHello { get; } = windowsHello;

  [ObservableProperty]
  public partial string? Password { get; set; }

  internal override void ClearSecrets() => Password = null;

  internal override void OnWindowClosed()
  {
    if (!_confirmed)
      _ = SendQuietlyAsync(Ops.CancelReprompt, IpcJson.ToElement(new RepromptCancelRequest(Token), IpcJson.Default.RepromptCancelRequest));
  }

  [RelayCommand]
  internal async Task SubmitBiometricAsync()
  {
    _biometric = true;
    try
    {
      await SubmitAsync();
    }
    finally
    {
      _biometric = false;
    }
  }

  protected override async Task<bool> RunStepAsync()
  {
    var password = Password ?? string.Empty;
    if (!_biometric && password.Length == 0)
    {
      ErrorMessage = "Enter your master password";
      return false;
    }

    var request = new RepromptConfirmRequest(Token, _biometric ? null : password, _biometric);
    var reply = IpcJson.Read(await Channel.RequestAsync(Ops.ConfirmReprompt, IpcJson.ToElement(request, IpcJson.Default.RepromptConfirmRequest), Timeout.InfiniteTimeSpan), IpcJson.Default.RepromptConfirmResponse);
    if (reply.Success)
    {
      _confirmed = true;
      return true;
    }

    ErrorMessage = reply.Error ?? "Verification failed";
    return false;
  }
}
