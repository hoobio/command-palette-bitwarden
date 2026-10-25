using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HoobiBitwarden.Ipc;

namespace HoobiBitwardenCompanion.ViewModels;

internal abstract partial class DialogViewModel(PipeChannel channel) : ObservableObject
{
  protected PipeChannel Channel { get; } = channel;

  internal event Action? Completed;

  [ObservableProperty]
  public partial string? ErrorMessage { get; protected set; }

  [ObservableProperty]
  public partial bool IsBusy { get; private set; }

  [ObservableProperty]
  public partial int Step { get; protected set; }

  [RelayCommand]
  internal async Task SubmitAsync()
  {
    if (IsBusy)
      return;
    ErrorMessage = null;
    IsBusy = true;
    try
    {
      if (await RunStepAsync())
      {
        ClearSecrets();
        Completed?.Invoke();
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

  internal virtual void ClearSecrets()
  {
  }

  protected abstract Task<bool> RunStepAsync();
}
