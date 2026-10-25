using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal sealed partial class UnlockViewModel(PipeChannel channel, bool rememberSession, bool windowsHello) : DialogViewModel(channel)
{
  private bool _biometric;

  [ObservableProperty]
  public partial string? Password { get; set; }

  [ObservableProperty]
  public partial bool RememberSession { get; set; } = rememberSession;

  public bool WindowsHello { get; } = windowsHello;

  internal override void ClearSecrets() => Password = null;

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

  protected override string BusyMessage => _biometric ? "Waiting for Windows Hello..." : "Unlocking your vault...";

  protected override async Task<bool> RunStepAsync()
  {
    var password = Password ?? string.Empty;
    if (!_biometric && password.Length == 0)
    {
      ErrorMessage = "Enter your master password";
      return false;
    }

    var request = new UnlockRequest(_biometric ? string.Empty : password, RememberSession, _biometric);
    var reply = IpcJson.Read(await Channel.RequestAsync(Ops.Unlock, IpcJson.ToElement(request, IpcJson.Default.UnlockRequest), Timeout.InfiniteTimeSpan), IpcJson.Default.UnlockResponse);
    if (reply.Success || reply.LoggedOut)
      return true;
    ErrorMessage = reply.Error ?? "Unlock failed";
    return false;
  }
}
