using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Services;

internal sealed class WindowCoordinator : IDisposable
{
  private readonly Dictionary<string, ItemWindow> _itemWindows = new(StringComparer.OrdinalIgnoreCase);

  public WindowCoordinator(Stream input, Stream output) =>
    Channel = new PipeChannel(input, output, HandleRequestAsync, OnEvent);

  public PipeChannel Channel { get; }

  public IReadOnlyCollection<ItemWindow> ItemWindows => _itemWindows.Values;

  public async Task RunAsync(Action onClosed)
  {
    try
    {
      await Channel.RunAsync();
    }
    finally
    {
      Dispatcher.UIThread.Post(onClosed);
    }
  }

  private void OnEvent(string op, JsonElement? payload)
  {
    if (op == Ops.Locked)
      Dispatcher.UIThread.Post(OnLocked);
    else if (op == Ops.VaultChanged)
      Dispatcher.UIThread.Post(() => _ = OnVaultChangedAsync());
  }

  internal void OnLocked()
  {
    foreach (var window in _itemWindows.Values)
      window.ViewModel?.OnLocked();
    GeneratorView.OnLocked();
  }

  internal Task OnVaultChangedAsync() => Task.WhenAll(_itemWindows.Values.Select(w => w.ViewModel?.LoadAsync() ?? Task.CompletedTask));

  private async Task<JsonElement?> HandleRequestAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
  {
    switch (op)
    {
      case Ops.ShowItem or Ops.RotateItem:
        var request = IpcJson.Read(payload, IpcJson.Default.ItemRequest);
        if (!Guid.TryParse(request.Id, out _))
          throw new IpcException(IpcErrors.Invalid);
        if (op == Ops.ShowItem)
          await Dispatcher.UIThread.InvokeAsync(() => ShowItem(request.Id));
        else
          Dispatcher.UIThread.Post(() => RotateItem(request.Id));
        return null;
      default:
        throw new IpcException(IpcErrors.Invalid);
    }
  }

  // The palette request returns before the confirm, because the extension restarts a companion that does not answer in time.
  public ItemWindow RotateItem(string id)
  {
    var window = ShowItem(id, load: false);
    _ = window.ViewModel?.RotateAfterLoadAsync();
    return window;
  }

  public ItemWindow ShowItem(string id) => ShowItem(id, load: true);

  private ItemWindow ShowItem(string id, bool load)
  {
    if (!_itemWindows.TryGetValue(id, out var window))
    {
      var viewModel = new ItemViewModel(id, Channel);
      window = new ItemWindow(viewModel);
      Action removed = window.Close;
      viewModel.Removed += removed;
      window.Closed += (_, _) =>
      {
        _itemWindows.Remove(id);
        viewModel.Removed -= removed;
        viewModel.Dispose();
      };
      _itemWindows[id] = window;
    }

    if (load)
      _ = window.ViewModel?.LoadAsync();
    BringToFront(window);
    return window;
  }

  // Activate takes focus only when the extension passed on its foreground right (CompanionHost.GrantForeground); the Topmost toggle still raises the window when that grant is refused.
  private static void BringToFront(Window window)
  {
    window.Show();
    if (window.WindowState == WindowState.Minimized)
      window.WindowState = WindowState.Normal;
    var topmost = window.Topmost;
    window.Topmost = true;
    window.Topmost = topmost;
    window.Activate();
  }

  public void Dispose() => Channel.Dispose();
}
