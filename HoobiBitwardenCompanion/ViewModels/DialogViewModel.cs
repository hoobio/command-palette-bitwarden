using System.Text.Json;
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

  internal virtual void OnWindowClosed()
  {
  }

  protected async Task SendQuietlyAsync(string op, JsonElement? payload)
  {
    try
    {
      await Channel.RequestAsync(op, payload);
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException or ObjectDisposedException)
    {
      // The extension drops its pending state by itself when this cancel does not arrive.
    }
  }

  protected abstract Task<bool> RunStepAsync();
}
