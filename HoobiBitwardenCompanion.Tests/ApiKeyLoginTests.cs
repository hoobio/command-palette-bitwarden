using System.Text.Json;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;

namespace HoobiBitwardenCompanion.Tests;

public class ApiKeyLoginTests(AvaloniaSession session)
{
  private sealed class FakeExtension
  {
    public List<JsonElement?> Logins { get; } = [];
    public ApiKeyLoginResponse Response { get; set; } = new(true, null);

    public Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      if (op != Ops.ApiKeyLogin)
        throw new IpcException(IpcErrors.Invalid);
      lock (Logins)
        Logins.Add(payload);
      return Task.FromResult<JsonElement?>(IpcJson.ToElement(Response, IpcJson.Default.ApiKeyLoginResponse));
    }
  }

  [Fact]
  public Task ShowApiKeyLogin_IsOneWindow_AndClosingReturnsTheForeground() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    List<nint> returned = [];
    coordinator.ReturnForeground = returned.Add;
    _ = coordinator.RunAsync(() => { });

    var reply = pair.Extension.RequestAsync(Ops.ShowApiKeyLogin, IpcJson.ToElement(new DialogRequest(1234), IpcJson.Default.DialogRequest));
    AvaloniaSession.WaitFor(() => reply.IsCompleted);
    var window = coordinator.ShowApiKeyLogin();
    Assert.Same(window, coordinator.ShowApiKeyLogin());
    Assert.Equal(1234, window.ReturnWindow);

    window.Close();
    Assert.Equal([(nint)1234], returned);
    Assert.NotSame(window, coordinator.ShowApiKeyLogin());
    coordinator.ShowApiKeyLogin().Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task ApiKeyLogin_Failure_ShowsTheError_AndKeepsTheWindowAndValues() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Response = new ApiKeyLoginResponse(false, "client_secret is incorrect") };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowApiKeyLogin();
    var viewModel = window.ViewModel!;
    viewModel.ClientId = " user.abc ";
    viewModel.ClientSecret = " s3cret ";
    await viewModel.SubmitAsync();

    var sent = IpcJson.Read(extension.Logins.Single(), IpcJson.Default.ApiKeyLoginRequest);
    Assert.Equal(new ApiKeyLoginRequest("user.abc", "s3cret"), sent);
    Assert.Equal("client_secret is incorrect", viewModel.ErrorMessage);
    Assert.False(viewModel.IsBusy);
    Assert.Equal(" user.abc ", viewModel.ClientId);
    Assert.Equal(" s3cret ", viewModel.ClientSecret);
    Assert.Same(window, coordinator.ShowApiKeyLogin());
    window.Close();
  });

  [Fact]
  public Task ApiKeyLogin_Success_ClearsTheValues_AndClosesTheWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    List<nint> returned = [];
    coordinator.ReturnForeground = returned.Add;
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowApiKeyLogin(99);
    var viewModel = window.ViewModel!;
    viewModel.ClientId = "user.abc";
    viewModel.ClientSecret = "s3cret";
    await viewModel.SubmitAsync();

    Assert.Null(viewModel.ClientId);
    Assert.Null(viewModel.ClientSecret);
    Assert.Null(viewModel.ErrorMessage);
    Assert.Equal([(nint)99], returned);
    Assert.NotSame(window, coordinator.ShowApiKeyLogin());
    coordinator.ShowApiKeyLogin().Close();
  });

  [Fact]
  public Task ApiKeyLogin_LockedEvent_KeepsTheWindowOpen() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput);
    coordinator.ReturnForeground = _ => { };
    _ = coordinator.RunAsync(() => { });

    var window = coordinator.ShowApiKeyLogin();
    coordinator.OnLocked();
    Assert.Same(window, coordinator.ShowApiKeyLogin());
    window.Close();
    await Task.CompletedTask;
  });
}
