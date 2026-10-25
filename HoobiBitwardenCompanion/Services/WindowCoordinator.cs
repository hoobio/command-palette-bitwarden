using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.ViewModels;
using HoobiBitwardenCompanion.Views;

namespace HoobiBitwardenCompanion.Services;

internal sealed class WindowCoordinator : IDisposable
{
  private readonly Dictionary<string, ItemWindow> _itemWindows = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<ItemWindow> _newItemWindows = [];
  private GeneratorWindow? _generatorWindow;
  private SettingsWindow? _settingsWindow;
  private readonly Dictionary<Type, DialogWindow> _dialogs = [];
  private FiltersResponse? _filters;

  public WindowCoordinator(Stream input, Stream output) =>
    Channel = new PipeChannel(input, output, HandleRequestAsync, OnEvent);

  public PipeChannel Channel { get; }

  internal Action<nint> ReturnForeground { get; set; } = WindowAlertInterop.ReturnForeground;

  internal Action<Window> TakeForeground { get; set; } = WindowAlertInterop.TakeForeground;

  internal Func<nint, PixelPoint?> AnchorOf { get; set; } = WindowAlertInterop.WindowCentre;

  public IReadOnlyCollection<ItemWindow> ItemWindows => _itemWindows.Values;

  public IReadOnlyCollection<ItemWindow> NewItemWindows => _newItemWindows;

  public async Task RunAsync(Action onClosed)
  {
    Dispatcher.UIThread.Post(() => _ = LoadFiltersAsync(), DispatcherPriority.Background);
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
      Dispatcher.UIThread.Post(() =>
      {
        if (_filters is null)
          _ = LoadFiltersAsync();
        _ = OnVaultChangedAsync();
      });
    else if (op == Ops.SettingsChanged)
      Dispatcher.UIThread.Post(() => _ = OnSettingsChangedAsync());
  }

  internal void OnLocked()
  {
    _filters = null;
    foreach (var window in _itemWindows.Values)
      window.ViewModel?.OnLocked();
    foreach (var window in _newItemWindows.ToList())
    {
      window.ViewModel?.OnLocked();
      window.Close();
    }

    _generatorWindow?.ViewModel?.OnLocked();
    GeneratorView.OnLocked();
  }

  internal Task OnVaultChangedAsync() => Task.WhenAll(_itemWindows.Values.Select(w => w.ViewModel?.LoadAsync() ?? Task.CompletedTask));

  internal Task OnSettingsChangedAsync() => _settingsWindow?.ViewModel?.LoadAsync() ?? Task.CompletedTask;

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
      case Ops.ShowGenerator:
        await Dispatcher.UIThread.InvokeAsync(ShowGenerator);
        return null;
      case Ops.ShowSettings:
        await Dispatcher.UIThread.InvokeAsync(ShowSettings);
        return null;
      case Ops.ShowApiKeyLogin:
        var dialog = payload is null ? null : IpcJson.Read(payload, IpcJson.Default.DialogRequest);
        await Dispatcher.UIThread.InvokeAsync(() => ShowApiKeyLogin((nint)(dialog?.ReturnWindow ?? 0)));
        return null;
      case Ops.ShowUnlock:
        var unlock = IpcJson.Read(payload, IpcJson.Default.UnlockDialogRequest);
        await Dispatcher.UIThread.InvokeAsync(() => ShowUnlock((nint)unlock.ReturnWindow, unlock.RememberSession, unlock.WindowsHello));
        return null;
      case Ops.ShowSetServer:
        var server = IpcJson.Read(payload, IpcJson.Default.SetServerDialogRequest);
        await Dispatcher.UIThread.InvokeAsync(() => ShowSetServer((nint)server.ReturnWindow, server.Region, server.Url));
        return null;
      case Ops.ShowLogin:
        var login = IpcJson.Read(payload, IpcJson.Default.LoginDialogRequest);
        await Dispatcher.UIThread.InvokeAsync(() => ShowLogin((nint)login.ReturnWindow, login.RememberSession, login.CustomDataDir));
        return null;
      case Ops.ShowReprompt:
        var reprompt = IpcJson.Read(payload, IpcJson.Default.RepromptDialogRequest);
        await Dispatcher.UIThread.InvokeAsync(() => ShowReprompt((nint)reprompt.ReturnWindow, reprompt.Token, reprompt.ItemName, reprompt.WindowsHello));
        return null;
      case Ops.NewItem:
        Dispatcher.UIThread.Post(() => _ = ShowNewItemAsync());
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

  public GeneratorWindow ShowGenerator()
  {
    if (_generatorWindow is null)
    {
      var viewModel = new GeneratorViewModel(Channel) { Toast = message => ToastWindow.Show(_generatorWindow, message) };
      var window = new GeneratorWindow(viewModel)
      {
        SaveAsNewItem = value => ShowNewItemAsync(ItemEditorViewModel.Blank(1) with { Login = new LoginEdit(null, value, null, []) }),
      };
      window.Closed += (_, _) => _generatorWindow = null;
      _generatorWindow = window;
      _ = viewModel.InitializeAsync();
    }

    BringToFront(_generatorWindow);
    return _generatorWindow;
  }

  public SettingsWindow ShowSettings()
  {
    if (_settingsWindow is null)
    {
      var viewModel = new SettingsViewModel(Channel);
      var window = new SettingsWindow(viewModel);
      window.Closed += (_, _) => _settingsWindow = null;
      _settingsWindow = window;
      _ = viewModel.LoadAsync();
    }

    BringToFront(_settingsWindow);
    return _settingsWindow;
  }

  public ApiKeyLoginWindow ShowApiKeyLogin(nint returnWindow = 0) =>
    ShowDialog(() => new ApiKeyLoginWindow(new ApiKeyLoginViewModel(Channel)), returnWindow);

  public UnlockWindow ShowUnlock(nint returnWindow = 0, bool rememberSession = false, bool windowsHello = false) =>
    ShowDialog(() => new UnlockWindow(new UnlockViewModel(Channel, rememberSession, windowsHello)), returnWindow);

  public SetServerWindow ShowSetServer(nint returnWindow = 0, string region = ServerRegions.Cloud, string? url = null) =>
    ShowDialog(() => new SetServerWindow(new SetServerViewModel(Channel, region, url)), returnWindow);

  public LoginWindow ShowLogin(nint returnWindow = 0, bool rememberSession = false, string? customDataDir = null) =>
    ShowDialog(() => new LoginWindow(new LoginViewModel(Channel, rememberSession, customDataDir)), returnWindow);

  public RepromptWindow ShowReprompt(nint returnWindow, string token, string itemName, bool windowsHello)
  {
    if (_dialogs.TryGetValue(typeof(RepromptWindow), out var existing) && ((RepromptWindow)existing).ViewModel?.Token != token)
      existing.Close();
    return ShowDialog(() => new RepromptWindow(new RepromptViewModel(Channel, token, itemName, windowsHello)), returnWindow);
  }

  private T ShowDialog<T>(Func<T> create, nint returnWindow) where T : DialogWindow
  {
    var anchor = AnchorOf(returnWindow);
    var created = false;
    if (!_dialogs.TryGetValue(typeof(T), out var existing))
    {
      var window = create();
      window.Closed += (_, _) =>
      {
        _dialogs.Remove(typeof(T));
        ReturnForeground(window.ReturnWindow);
      };
      if (anchor is { } point)
      {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = point;
      }
      _dialogs[typeof(T)] = existing = window;
      created = true;
    }

    if (returnWindow != 0)
      existing.ReturnWindow = returnWindow;
    existing.Show();
    if (created && anchor is { } centre)
      existing.CentreOnScreenOf(centre);
    if (existing.WindowState == WindowState.Minimized)
      existing.WindowState = WindowState.Normal;
    existing.Activate();
    TakeForeground(existing);
    existing.FocusFirstInput();
    return (T)existing;
  }

  // The extension lists collections with bw, so a new item window opens from the last list and refreshes it afterwards.
  private async Task<FiltersResponse?> LoadFiltersAsync()
  {
    try
    {
      return _filters = IpcJson.Read(await Channel.RequestAsync(Ops.ListFilters, null), IpcJson.Default.FiltersResponse);
    }
    catch (Exception ex) when (ex is IpcException or IOException or TimeoutException)
    {
      return null;
    }
  }

  public async Task<ItemWindow?> ShowNewItemAsync(EditableItem? item = null)
  {
    var cached = _filters;
    if ((cached ?? await LoadFiltersAsync()) is not { } filters)
      return null;
    if (cached is not null)
      _ = LoadFiltersAsync();

    var viewModel = ItemViewModel.ForNew(Channel, item ?? ItemEditorViewModel.Blank(1), filters);
    var window = new ItemWindow(viewModel);
    Action close = window.Close;
    Action saved = () =>
    {
      _newItemWindows.Remove(window);
      _itemWindows[viewModel.Id] = window;
    };
    viewModel.Discarded += close;
    viewModel.Removed += close;
    viewModel.Saved += saved;
    window.Closed += (_, _) =>
    {
      _newItemWindows.Remove(window);
      if (_itemWindows.TryGetValue(viewModel.Id, out var keyed) && keyed == window)
        _itemWindows.Remove(viewModel.Id);
      viewModel.Discarded -= close;
      viewModel.Removed -= close;
      viewModel.Saved -= saved;
      viewModel.Dispose();
    };
    _newItemWindows.Add(window);
    BringToFront(window);
    return window;
  }

  // Activate takes focus only when the extension passed on its foreground right (CompanionHost.GrantForeground); the Topmost toggle still raises the window when that grant is refused.
  private void BringToFront(Window window)
  {
    window.Show();
    if (window.WindowState == WindowState.Minimized)
      window.WindowState = WindowState.Normal;
    var topmost = window.Topmost;
    window.Topmost = true;
    window.Topmost = topmost;
    window.Activate();
    TakeForeground(window);
    (window as SettingsWindow)?.FocusSearch();
  }

  public void Dispose() => Channel.Dispose();
}
