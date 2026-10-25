using System.Text.Json;
using Avalonia.Controls;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCompanion.Services;
using HoobiBitwardenCompanion.ViewModels;

namespace HoobiBitwardenCompanion.Tests;

public class LoginRepromptWindowTests(AvaloniaSession session)
{
  private sealed class FakeExtension
  {
    public List<(string Op, JsonElement? Payload)> Calls { get; } = [];
    public Queue<LoginResponse> Logins { get; } = new([new LoginResponse(true, null)]);
    public LoginResponse Device { get; set; } = new(true, null);
    public RepromptConfirmResponse Confirm { get; set; } = new(true, null);

    public List<string> Ops_ => [.. Calls.Select(c => c.Op)];

    public Task<JsonElement?> HandleAsync(string op, JsonElement? payload, CancellationToken cancellationToken)
    {
      lock (Calls)
        Calls.Add((op, payload));
      return Task.FromResult<JsonElement?>(op switch
      {
        Ops.Login => IpcJson.ToElement(Logins.Count > 1 ? Logins.Dequeue() : Logins.Peek(), IpcJson.Default.LoginResponse),
        Ops.SubmitDeviceVerification => IpcJson.ToElement(Device, IpcJson.Default.LoginResponse),
        Ops.ConfirmReprompt => IpcJson.ToElement(Confirm, IpcJson.Default.RepromptConfirmResponse),
        Ops.CancelLogin or Ops.CancelReprompt => null,
        _ => throw new IpcException(IpcErrors.Invalid),
      });
    }
  }

  private static WindowCoordinator NewCoordinator(ChannelPair pair, List<nint>? returned = null)
  {
    var coordinator = new WindowCoordinator(pair.CompanionInput, pair.CompanionOutput) { ReturnForeground = returned is null ? _ => { } : returned.Add, TakeForeground = _ => { } };
    _ = coordinator.RunAsync(() => { });
    return coordinator;
  }

  private static LoginRequest SentLogin(FakeExtension extension, int index) =>
    IpcJson.Read(extension.Calls.Where(c => c.Op == Ops.Login).ElementAt(index).Payload, IpcJson.Default.LoginRequest);

  [Fact]
  public Task ShowLogin_IsOneWindow_TakesTheDialogValues_AndClosingReturnsTheForeground() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    List<nint> returned = [];
    using var coordinator = NewCoordinator(pair, returned);

    var reply = pair.Extension.RequestAsync(Ops.ShowLogin, IpcJson.ToElement(new LoginDialogRequest(1234, true, "C:\\bw"), IpcJson.Default.LoginDialogRequest));
    AvaloniaSession.WaitFor(() => reply.IsCompleted);
    var window = coordinator.ShowLogin();
    Assert.Same(window, coordinator.ShowLogin());
    Assert.Equal(1234, window.ReturnWindow);
    Assert.True(window.ViewModel!.RememberSession);
    Assert.Contains("C:\\bw", window.ViewModel.CustomDataDirWarning, StringComparison.Ordinal);

    window.Close();
    Assert.Equal([(nint)1234], returned);
    Assert.NotSame(window, coordinator.ShowLogin());
    coordinator.ShowLogin().Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Login_Submit_SendsTheCredentialsAndDefaultMethod_ThenClearsThem_AndClosesTheWindow() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin(0, rememberSession: false);
    var viewModel = window.ViewModel!;
    viewModel.Email = " user@test.com ";
    viewModel.MasterPassword = "hunter2";
    viewModel.RememberSession = true;
    await viewModel.SubmitAsync();

    Assert.Equal(new LoginRequest("user@test.com", "hunter2", 0, null, true), SentLogin(extension, 0));
    Assert.Null(viewModel.Email);
    Assert.Null(viewModel.MasterPassword);
    Assert.NotSame(window, coordinator.ShowLogin());
    coordinator.ShowLogin().Close();
  });

  [Fact]
  public Task Login_AutoDetect_SendsNoMethod() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    window.ViewModel!.Email = "user@test.com";
    window.ViewModel.MasterPassword = "pw";
    window.ViewModel.MethodIndex = 3;
    await window.ViewModel.SubmitAsync();

    Assert.Null(SentLogin(extension, 0).Method);
    coordinator.ShowLogin().Close();
  });

  [Fact]
  public Task Login_EmptyCredentials_SendNothing() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    await window.ViewModel!.SubmitAsync();

    Assert.Empty(extension.Calls);
    Assert.Equal("Enter your email and master password", window.ViewModel.ErrorMessage);
    window.Close();
  });

  [Fact]
  public Task Login_Failure_ShowsTheError_AndKeepsTheWindowAndValues() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Logins.Clear();
    extension.Logins.Enqueue(new LoginResponse(false, "Username or password is incorrect."));
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    var viewModel = window.ViewModel!;
    viewModel.Email = "user@test.com";
    viewModel.MasterPassword = "wrong";
    await viewModel.SubmitAsync();

    Assert.Equal("Username or password is incorrect.", viewModel.ErrorMessage);
    Assert.False(viewModel.IsBusy);
    Assert.Equal("user@test.com", viewModel.Email);
    Assert.Equal("wrong", viewModel.MasterPassword);
    Assert.Same(window, coordinator.ShowLogin());
    window.Close();
  });

  [Fact]
  public Task Login_TwoFactorRequired_SwitchesToTheCodeStep_ThenSendsTheCode_AndBackReturnsToTheCredentials() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Logins.Clear();
    extension.Logins.Enqueue(new LoginResponse(false, null, TwoFactorRequired: true));
    extension.Logins.Enqueue(new LoginResponse(false, null, TwoFactorRequired: true));
    extension.Logins.Enqueue(new LoginResponse(true, null));
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    var viewModel = window.ViewModel!;
    viewModel.Email = "user@test.com";
    viewModel.MasterPassword = "pw";
    viewModel.MethodIndex = 2;
    await viewModel.SubmitAsync();

    Assert.Equal(LoginViewModel.CodeStep, viewModel.Step);
    Assert.True(window.FindControl<Control>("CodePanel")!.IsVisible);
    Assert.False(window.FindControl<Control>("CredentialsPanel")!.IsVisible);
    Assert.False(window.FindControl<Control>("DevicePanel")!.IsVisible);
    Assert.True(window.FindControl<Control>("BackButton")!.IsVisible);
    Assert.Equal("Touch your YubiKey...", viewModel.CodePlaceholder);
    Assert.Equal("Verify", viewModel.SubmitLabel);
    Assert.Null(viewModel.ErrorMessage);

    viewModel.Back();
    Assert.Equal(LoginViewModel.CredentialsStep, viewModel.Step);
    Assert.True(window.FindControl<Control>("CredentialsPanel")!.IsVisible);
    Assert.False(window.FindControl<Control>("CodePanel")!.IsVisible);
    Assert.Equal("user@test.com", viewModel.Email);
    Assert.Equal("pw", viewModel.MasterPassword);

    viewModel.MethodIndex = 0;
    await viewModel.SubmitAsync();
    Assert.Equal(LoginViewModel.CodeStep, viewModel.Step);
    viewModel.Code = " 123456 ";
    await viewModel.SubmitAsync();

    Assert.Equal(new LoginRequest("user@test.com", "pw", 0, "123456", false), SentLogin(extension, 2));
    Assert.NotSame(window, coordinator.ShowLogin());
    coordinator.ShowLogin().Close();
  });

  [Fact]
  public Task Login_CodeStep_InvalidCode_SendsNothing_AndKeepsTheStep() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Logins.Clear();
    extension.Logins.Enqueue(new LoginResponse(false, null, TwoFactorRequired: true));
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    var viewModel = window.ViewModel!;
    viewModel.Email = "user@test.com";
    viewModel.MasterPassword = "pw";
    await viewModel.SubmitAsync();
    viewModel.Code = "12";
    await viewModel.SubmitAsync();

    Assert.Single(extension.Calls);
    Assert.Equal("Enter a valid authenticator code", viewModel.ErrorMessage);
    Assert.Equal(LoginViewModel.CodeStep, viewModel.Step);
    window.Close();
  });

  [Fact]
  public Task Login_CodeStep_Failure_ShowsTheErrorAndKeepsTheCode() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Logins.Clear();
    extension.Logins.Enqueue(new LoginResponse(false, null, TwoFactorRequired: true));
    extension.Logins.Enqueue(new LoginResponse(false, "Invalid 2FA code - try again"));
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    var viewModel = window.ViewModel!;
    viewModel.Email = "user@test.com";
    viewModel.MasterPassword = "pw";
    await viewModel.SubmitAsync();
    viewModel.Code = "123456";
    await viewModel.SubmitAsync();

    Assert.Equal("Invalid 2FA code - try again", viewModel.ErrorMessage);
    Assert.Equal("123456", viewModel.Code);
    Assert.Equal(LoginViewModel.CodeStep, viewModel.Step);
    window.Close();
  });

  [Fact]
  public Task Login_DeviceVerification_SwitchesToTheDeviceStep_AndSendsTheOtp() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Logins.Clear();
    extension.Logins.Enqueue(new LoginResponse(false, null, DeviceVerificationRequired: true));
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    var viewModel = window.ViewModel!;
    viewModel.Email = "user@test.com";
    viewModel.MasterPassword = "pw";
    await viewModel.SubmitAsync();

    Assert.Equal(LoginViewModel.DeviceStep, viewModel.Step);
    Assert.True(window.FindControl<Control>("DevicePanel")!.IsVisible);
    Assert.False(window.FindControl<Control>("CredentialsPanel")!.IsVisible);
    Assert.Equal("Verify device", viewModel.SubmitLabel);

    viewModel.Code = "12";
    await viewModel.SubmitAsync();
    Assert.DoesNotContain(Ops.SubmitDeviceVerification, extension.Ops_);

    viewModel.Code = "123456";
    await viewModel.SubmitAsync();

    Assert.Equal("123456", IpcJson.Read(extension.Calls.Single(c => c.Op == Ops.SubmitDeviceVerification).Payload, IpcJson.Default.DeviceVerificationRequest).Code);
    Assert.Null(viewModel.Email);
    Assert.NotSame(window, coordinator.ShowLogin());
    coordinator.ShowLogin().Close();
    Assert.DoesNotContain(Ops.CancelLogin, extension.Ops_);
  });

  [Fact]
  public Task Login_DeviceVerification_Failure_KeepsTheStepAndCode() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Device = new LoginResponse(false, "Verification failed") };
    extension.Logins.Clear();
    extension.Logins.Enqueue(new LoginResponse(false, null, DeviceVerificationRequired: true));
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    var viewModel = window.ViewModel!;
    viewModel.Email = "user@test.com";
    viewModel.MasterPassword = "pw";
    await viewModel.SubmitAsync();
    viewModel.Code = "123456";
    await viewModel.SubmitAsync();

    Assert.Equal("Verification failed", viewModel.ErrorMessage);
    Assert.Equal("123456", viewModel.Code);
    Assert.Equal(LoginViewModel.DeviceStep, viewModel.Step);
    window.Close();
  });

  [Fact]
  public Task Login_ClosingDuringDeviceVerification_SendsCancelLogin() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Logins.Clear();
    extension.Logins.Enqueue(new LoginResponse(false, null, DeviceVerificationRequired: true));
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    window.ViewModel!.Email = "user@test.com";
    window.ViewModel.MasterPassword = "pw";
    await window.ViewModel.SubmitAsync();
    window.Close();

    AvaloniaSession.WaitFor(() => extension.Ops_.Contains(Ops.CancelLogin));
  });

  [Fact]
  public Task Login_BackFromDeviceVerification_SendsCancelLogin() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    extension.Logins.Clear();
    extension.Logins.Enqueue(new LoginResponse(false, null, DeviceVerificationRequired: true));
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    window.ViewModel!.Email = "user@test.com";
    window.ViewModel.MasterPassword = "pw";
    await window.ViewModel.SubmitAsync();
    window.ViewModel.Back();

    AvaloniaSession.WaitFor(() => extension.Ops_.Contains(Ops.CancelLogin));
    Assert.Equal(LoginViewModel.CredentialsStep, window.ViewModel.Step);
    window.Close();
  });

  [Fact]
  public Task Login_ClosingWithoutADeviceVerification_SendsNoCancel_AndClearsTheSecrets() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowLogin();
    var viewModel = window.ViewModel!;
    viewModel.Email = "user@test.com";
    viewModel.MasterPassword = "pw";
    window.Close();

    Assert.Null(viewModel.MasterPassword);
    Assert.Null(viewModel.Email);
    Assert.Empty(extension.Calls);
    await Task.CompletedTask;
  });

  [Fact]
  public Task Dialogs_AreTopmost_AndTakeTheForeground() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);
    List<Window> taken = [];
    coordinator.TakeForeground = taken.Add;

    var login = coordinator.ShowLogin();
    var unlock = coordinator.ShowUnlock();
    var reprompt = coordinator.ShowReprompt(0, "t", "Item", false);

    Assert.All(new Window[] { login, unlock, reprompt }, window => Assert.True(window.Topmost));
    Assert.Equal(new Window[] { login, unlock, reprompt }, taken);
    login.Close();
    unlock.Close();
    reprompt.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task ShowReprompt_IsOneWindowPerToken_AndANewTokenReplacesTheOldOne() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    List<nint> returned = [];
    using var coordinator = NewCoordinator(pair, returned);

    var reply = pair.Extension.RequestAsync(Ops.ShowReprompt, IpcJson.ToElement(new RepromptDialogRequest(4321, "tok1", "GitHub", true), IpcJson.Default.RepromptDialogRequest));
    AvaloniaSession.WaitFor(() => reply.IsCompleted);
    var window = coordinator.ShowReprompt(0, "tok1", "GitHub", true);
    Assert.Same(window, coordinator.ShowReprompt(0, "tok1", "GitHub", true));
    Assert.Equal(4321, window.ReturnWindow);
    Assert.Equal("GitHub", window.ViewModel!.ItemName);
    Assert.True(window.ViewModel.WindowsHello);

    var next = coordinator.ShowReprompt(9, "tok2", "Other", false);
    Assert.NotSame(window, next);
    Assert.Equal("tok2", next.ViewModel!.Token);
    AvaloniaSession.WaitFor(() => extension.Ops_.Contains(Ops.CancelReprompt));
    Assert.Equal("tok1", IpcJson.Read(extension.Calls.Single(c => c.Op == Ops.CancelReprompt).Payload, IpcJson.Default.RepromptCancelRequest).Token);
    Assert.Equal((nint)4321, returned.Single());
    next.Close();
    await Task.CompletedTask;
  });

  [Fact]
  public Task Reprompt_Submit_SendsTheTokenAndPassword_ThenClearsIt_AndClosesWithoutCancel() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    List<nint> returned = [];
    using var coordinator = NewCoordinator(pair, returned);

    var window = coordinator.ShowReprompt(99, "tok", "GitHub", false);
    var viewModel = window.ViewModel!;
    viewModel.Password = "hunter2";
    await viewModel.SubmitAsync();

    Assert.Equal(new RepromptConfirmRequest("tok", "hunter2"), IpcJson.Read(extension.Calls.Single().Payload, IpcJson.Default.RepromptConfirmRequest));
    Assert.Null(viewModel.Password);
    Assert.Equal([(nint)99], returned);
    Assert.Equal([Ops.ConfirmReprompt], extension.Ops_);
  });

  [Fact]
  public Task Reprompt_Failure_ShowsTheError_AndKeepsTheWindowAndPassword() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension { Confirm = new RepromptConfirmResponse(false, "Incorrect master password. Please try again.") };
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowReprompt(0, "tok", "GitHub", false);
    window.ViewModel!.Password = "wrong";
    await window.ViewModel.SubmitAsync();

    Assert.Equal("Incorrect master password. Please try again.", window.ViewModel.ErrorMessage);
    Assert.Equal("wrong", window.ViewModel.Password);
    Assert.Same(window, coordinator.ShowReprompt(0, "tok", "GitHub", false));
    window.Close();
  });

  [Fact]
  public Task Reprompt_EmptyPassword_SendsNothing() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowReprompt(0, "tok", "GitHub", false);
    await window.ViewModel!.SubmitAsync();

    Assert.Empty(extension.Calls);
    Assert.Equal("Enter your master password", window.ViewModel.ErrorMessage);
    window.Close();
    AvaloniaSession.WaitFor(() => extension.Ops_.Contains(Ops.CancelReprompt));
  });

  [Fact]
  public Task Reprompt_WindowsHello_SendsABiometricRequest_WithoutAPassword() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowReprompt(0, "tok", "GitHub", true);
    window.ViewModel!.Password = "ignored";
    await window.ViewModel.SubmitBiometricAsync();

    Assert.Equal(new RepromptConfirmRequest("tok", null, true), IpcJson.Read(extension.Calls.Single().Payload, IpcJson.Default.RepromptConfirmRequest));
    coordinator.ShowReprompt(0, "tok", "GitHub", true).Close();
  });

  [Fact]
  public Task Reprompt_ClosingWithoutConfirming_SendsCancelReprompt_AndClearsThePassword() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowReprompt(0, "tok", "GitHub", false);
    var viewModel = window.ViewModel!;
    viewModel.Password = "pw";
    window.Close();

    Assert.Null(viewModel.Password);
    AvaloniaSession.WaitFor(() => extension.Ops_.Contains(Ops.CancelReprompt));
    Assert.Equal("tok", IpcJson.Read(extension.Calls.Single().Payload, IpcJson.Default.RepromptCancelRequest).Token);
    await Task.CompletedTask;
  });

  [Fact]
  public Task Reprompt_LockedEvent_KeepsTheWindowOpen() => session.RunAsync(async () =>
  {
    var extension = new FakeExtension();
    using var pair = new ChannelPair(extension.HandleAsync);
    using var coordinator = NewCoordinator(pair);

    var window = coordinator.ShowReprompt(0, "tok", "GitHub", false);
    coordinator.OnLocked();
    Assert.Same(window, coordinator.ShowReprompt(0, "tok", "GitHub", false));
    window.Close();
    await Task.CompletedTask;
  });
}
