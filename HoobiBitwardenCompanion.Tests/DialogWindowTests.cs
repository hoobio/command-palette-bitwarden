using System.Text.Json;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Tests;

public class DialogWindowTests(AvaloniaSession session)
{
  private sealed class FakeExtension
  {
    public List<(string Op, JsonElement? Payload)> Calls { get; } = [];
    public UnlockResponse Unlock { get; set; } = new(true, null);
    public SetServerResponse SetServer { get; set; } = new(true, null);
    public SemaphoreSlim? Gate { get; set; }

    public async Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      if (op == Ops.ListFilters)
        throw new IpcException(IpcErrors.Invalid);
      lock (Calls)
        Calls.Add((op, payload));
      if (Gate is { } gate)
        await gate.WaitAsync(cancellationToken);
      return op switch
      {
        Ops.Unlock => IpcJson.ToElement(Unlock, IpcJson.Default.UnlockResponse),
        Ops.SetServer => IpcJson.ToElement(SetServer, IpcJson.Default.SetServerResponse),
        _ => throw new IpcException(IpcErrors.Invalid),
      };
    }
  }

  [Fact]
  public Task Unlock_WindowsHelloCancelled_ReturnsToThePasswordWithoutAnError() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Unlock = new(false, "Biometric unlock was cancelled or unavailable") };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowUnlock(0, rememberSession: false, windowsHello: true);
    var focusRequests = 0;
    window.ViewModel!.InputFocusRequested += () => focusRequests++;
    await window.ViewModel.SubmitBiometricAsync();

    Assert.Null(window.ViewModel.ErrorMessage);
    Assert.Equal(1, focusRequests);
    Assert.Same(window, coordinator.ShowUnlock());
    window.Close();
  });

  [Fact]
  public Task ShowDialog_OpensOnTheCommandPaletteScreen() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    List<nint> anchored = [];
    coordinator.ReturnForeground = _ => { };
    coordinator.AnchorOf = hwnd =>
    {
      anchored.Add(hwnd);
      return new Avalonia.PixelPoint(300, 200);
    };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowUnlock(4321, rememberSession: false, windowsHello: false);

    Assert.Equal([(nint)4321], anchored);
    Assert.Equal(Avalonia.Controls.WindowStartupLocation.Manual, window.WindowStartupLocation);
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Unlock_WhileTheRequestRuns_ShowsProgressWithText() => session.RunAsync(async () =>
  {
    using var gate = new SemaphoreSlim(0);
    var extension = new FakeExtension { Gate = gate };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowUnlock(0, rememberSession: false, windowsHello: true);
    var viewModel = window.ViewModel!;
    viewModel.Password = "hunter2";
    var submit = viewModel.SubmitAsync();
    AvaloniaSession.WaitFor(() => viewModel.IsBusy);
    Assert.Equal("Unlocking your vault...", viewModel.BusyText);

    gate.Release();
    await submit;
    Assert.False(viewModel.IsBusy);

    window = coordinator.ShowUnlock(0, rememberSession: false, windowsHello: true);
    var hello = window.ViewModel!.SubmitBiometricAsync();
    AvaloniaSession.WaitFor(() => window.ViewModel.IsBusy);
    Assert.Equal("Waiting for Windows Hello...", window.ViewModel.BusyText);
    gate.Release();
    await hello;
  });

  [Fact]
  public Task ShowUnlock_IsOneWindow_TakesTheDialogValues_AndClosingReturnsTheForeground() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    List<nint> returned = [];
    coordinator.ReturnForeground = returned.Add;
    _ = coordinator.RunAsync(() => { });

    var reply = pair.Extension.RequestAsync(Ops.ShowUnlock, IpcJson.ToElement(new UnlockDialogRequest(1234, true, true), IpcJson.Default.UnlockDialogRequest));
    AvaloniaSession.WaitFor(() => reply.IsCompleted);
    var window = coordinator.ShowUnlock();
    Assert.Same(window, coordinator.ShowUnlock());
    Assert.Equal(1234, window.ReturnWindow);
    Assert.True(window.ViewModel!.RememberSession);
    Assert.True(window.ViewModel.WindowsHello);

    window.Close();
    Assert.Equal([(nint)1234], returned);
    Assert.NotSame(window, coordinator.ShowUnlock());
    coordinator.ShowUnlock().Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Unlock_Submit_SendsThePassword_ThenClearsIt_AndClosesTheWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    List<nint> returned = [];
    coordinator.ReturnForeground = returned.Add;
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowUnlock(99, rememberSession: false, windowsHello: false);
    var viewModel = window.ViewModel!;
    viewModel.Password = "hunter2";
    viewModel.RememberSession = true;
    await viewModel.SubmitAsync();

    var sent = IpcJson.Read(extension.Calls.Single().Payload, IpcJson.Default.UnlockRequest);
    Assert.Equal(new UnlockRequest("hunter2", true), sent);
    Assert.Null(viewModel.Password);
    Assert.Equal([(nint)99], returned);
    Assert.NotSame(window, coordinator.ShowUnlock());
    coordinator.ShowUnlock().Close();
  });

  [Fact]
  public Task Unlock_Failure_ShowsTheError_AndKeepsTheWindowAndPassword() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Unlock = new UnlockResponse(false, "Invalid password entered") };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowUnlock();
    var viewModel = window.ViewModel!;
    viewModel.Password = "wrong";
    await viewModel.SubmitAsync();

    Assert.Equal("Invalid password entered", viewModel.ErrorMessage);
    Assert.False(viewModel.IsBusy);
    Assert.Equal("wrong", viewModel.Password);
    Assert.Same(window, coordinator.ShowUnlock());
    window.Close();
  });

  [Fact]
  public Task Unlock_LoggedOut_ClosesTheWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Unlock = new UnlockResponse(false, "You are not logged in", LoggedOut: true) };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowUnlock();
    window.ViewModel!.Password = "pw";
    await window.ViewModel.SubmitAsync();

    Assert.NotSame(window, coordinator.ShowUnlock());
    coordinator.ShowUnlock().Close();
  });

  [Fact]
  public Task Unlock_EmptyPassword_SendsNothing() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowUnlock();
    await window.ViewModel!.SubmitAsync();

    Assert.Empty(extension.Calls);
    Assert.Equal("Enter your master password", window.ViewModel.ErrorMessage);
    window.Close();
  });

  [Fact]
  public Task Unlock_WindowsHello_SendsABiometricRequest_WithoutAPassword() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowUnlock(0, rememberSession: true, windowsHello: true);
    window.ViewModel!.Password = "ignored";
    await window.ViewModel.SubmitBiometricAsync();

    Assert.Equal(new UnlockRequest(string.Empty, true, true), IpcJson.Read(Assert.Single(extension.Calls).Payload, IpcJson.Default.UnlockRequest));
    Assert.NotSame(window, coordinator.ShowUnlock());
    coordinator.ShowUnlock().Close();
  });

  [Fact]
  public Task Unlock_LockedEvent_KeepsTheWindowOpen() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowUnlock();
    coordinator.OnLocked();
    Assert.Same(window, coordinator.ShowUnlock());
    window.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task ShowSetServer_IsOneWindow_PreselectsTheRegion_AndClosingReturnsTheForeground() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    List<nint> returned = [];
    coordinator.ReturnForeground = returned.Add;
    _ = coordinator.RunAsync(() => { });

    var reply = pair.Extension.RequestAsync(Ops.ShowSetServer, IpcJson.ToElement(new SetServerDialogRequest(4321, ServerRegions.SelfHosted, "https://vault.example.com"), IpcJson.Default.SetServerDialogRequest));
    AvaloniaSession.WaitFor(() => reply.IsCompleted);
    var window = coordinator.ShowSetServer();
    Assert.Same(window, coordinator.ShowSetServer());
    Assert.Equal(4321, window.ReturnWindow);
    Assert.True(window.ViewModel!.IsSelfHosted);
    Assert.Equal("https://vault.example.com", window.ViewModel.BaseUrl);

    window.Close();
    Assert.Equal([(nint)4321], returned);
    Assert.NotSame(window, coordinator.ShowSetServer());
    coordinator.ShowSetServer().Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task SetServer_Cloud_SendsOnlyTheRegion_AndClosesTheWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    List<nint> returned = [];
    coordinator.ReturnForeground = returned.Add;
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowSetServer(77, ServerRegions.Cloud, null);
    var viewModel = window.ViewModel!;
    Assert.False(viewModel.IsSelfHosted);
    viewModel.BaseUrl = "https://ignored.example.com";
    await viewModel.SubmitAsync();

    Assert.Equal(new SetServerRequest(ServerRegions.Cloud, null), IpcJson.Read(extension.Calls.Single().Payload, IpcJson.Default.SetServerRequest));
    Assert.Equal([(nint)77], returned);
    Assert.NotSame(window, coordinator.ShowSetServer());
    coordinator.ShowSetServer().Close();
  });

  [Fact]
  public Task SetServer_SelfHosted_SendsTheUrlsTyped() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowSetServer(0, ServerRegions.Eu, null);
    var viewModel = window.ViewModel!;
    viewModel.RegionIndex = 2;
    Assert.True(viewModel.IsSelfHosted);
    viewModel.BaseUrl = "https://vault.example.com";
    viewModel.ApiUrl = "https://api.example.com";
    await viewModel.SubmitAsync();

    var sent = IpcJson.Read(extension.Calls.Single().Payload, IpcJson.Default.SetServerRequest);
    Assert.Equal(new SetServerRequest(ServerRegions.SelfHosted, "https://vault.example.com", ApiUrl: "https://api.example.com"), sent);
    coordinator.ShowSetServer().Close();
  });

  [Fact]
  public Task SetServer_Failure_ShowsTheError_AndKeepsTheWindowAndValues() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { SetServer = new SetServerResponse(false, "Invalid ApiUrl: must start with https://") };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowSetServer(0, ServerRegions.SelfHosted, "https://vault.example.com");
    var viewModel = window.ViewModel!;
    viewModel.ApiUrl = "http://api.example.com";
    await viewModel.SubmitAsync();

    Assert.Equal("Invalid ApiUrl: must start with https://", viewModel.ErrorMessage);
    Assert.False(viewModel.IsBusy);
    Assert.Equal("http://api.example.com", viewModel.ApiUrl);
    Assert.Same(window, coordinator.ShowSetServer());
    window.Close();
  });

  [Fact]
  public Task SetServer_LockedEvent_KeepsTheWindowOpen() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowSetServer();
    coordinator.OnLocked();
    Assert.Same(window, coordinator.ShowSetServer());
    window.Close();
    await Task.CompletedTask;
  });
}
