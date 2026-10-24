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
  private MainWindow? _mainWindow;

  public WindowCoordinator(Stream input, Stream output) =>
    Channel = new PipeChannel(input, output, HandleRequestAsync);

  public PipeChannel Channel { get; }

  public IReadOnlyCollection<ItemWindow> ItemWindows => _itemWindows.Values;

  public async Task RunAsync(Action onClosed)
  {
    await Channel.RunAsync();
    Dispatcher.UIThread.Post(onClosed);
  }

  private async Task<JsonElement?> HandleRequestAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
  {
    switch (op)
    {
      case Ops.ShowItem:
        var request = IpcJson.Read(payload, IpcJson.Default.ItemRequest);
        if (!Guid.TryParse(request.Id, out _))
          throw new IpcException(IpcErrors.Invalid);
        await Dispatcher.UIThread.InvokeAsync(() => ShowItem(request.Id));
        return null;
      case Ops.ShowVault:
        await Dispatcher.UIThread.InvokeAsync(ShowVault);
        return null;
      default:
        throw new IpcException(IpcErrors.Invalid);
    }
  }

  public ItemWindow ShowItem(string id)
  {
    if (!_itemWindows.TryGetValue(id, out var window))
    {
      var viewModel = new ItemViewModel(id, Channel);
      window = new ItemWindow(viewModel);
      window.Closed += (_, _) =>
      {
        _itemWindows.Remove(id);
        viewModel.Dispose();
      };
      _itemWindows[id] = window;
      _ = viewModel.LoadAsync();
    }

    BringToFront(window);
    return window;
  }

  public MainWindow ShowVault()
  {
    if (_mainWindow is null)
    {
      _mainWindow = new MainWindow();
      _mainWindow.Closed += (_, _) => _mainWindow = null;
    }

    BringToFront(_mainWindow);
    return _mainWindow;
  }

  // The companion is started by a background COM server, so a plain Activate only flashes the taskbar button.
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
