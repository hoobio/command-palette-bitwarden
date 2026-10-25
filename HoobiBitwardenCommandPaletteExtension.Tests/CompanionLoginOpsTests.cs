using System.IO;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class CompanionLoginOpsTests
{
  private static (BitwardenCliService Service, FakeProcessFactory Factory, string SettingsPath) CreateService()
  {
    var settingsPath = Path.Combine(Path.GetTempPath(), $"bw_test_{Guid.NewGuid():N}.json");
    var factory = new FakeProcessFactory();
    var svc = new BitwardenCliService(new BitwardenSettingsManager(settingsPath), factory.Create) { DataFileOverride = Path.Combine(Path.GetTempPath(), $"bw_missing_{Guid.NewGuid():N}.json") };
    return (svc, factory, settingsPath);
  }

  private static async Task<LoginResponse> LoginOpAsync(CompanionOps ops, LoginRequest request) =>
    IpcJson.Read(await ops.HandleAsync(Ops.Login, IpcJson.ToElement(request, IpcJson.Default.LoginRequest), CancellationToken.None), IpcJson.Default.LoginResponse);

  private static async Task<LoginResponse> DeviceOpAsync(CompanionOps ops, string code) =>
    IpcJson.Read(await ops.HandleAsync(Ops.SubmitDeviceVerification, IpcJson.ToElement(new DeviceVerificationRequest(code), IpcJson.Default.DeviceVerificationRequest), CancellationToken.None), IpcJson.Default.LoginResponse);

  private static FakeCliProcess DevicePrompt(int id)
  {
    var process = new FakeCliProcess(exitCode: 0, id: id);
    process.StderrStream.Enqueue("? A login request was made from a device verification...\n");
    process.StderrStream.Complete();
    return process;
  }

  [Fact]
  public async Task LoginOp_Success_UnlocksSyncsAndRaisesStatusChanged()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session_key_abc\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.", exitCode: 0));
    var statusChanged = 0;
    svc.StatusChanged += () => statusChanged++;
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await LoginOpAsync(ops, new LoginRequest(" user@test.com ", "pass", 0, null, false));
      await ops.PostLoginSync;

      Assert.Equal(new LoginResponse(true, null), reply);
      Assert.True(svc.IsUnlocked);
      Assert.True(statusChanged >= 1);
      Assert.Contains("sync", factory.LastArgs ?? string.Empty, StringComparison.Ordinal);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task LoginOp_AppliesRememberSession_WhenItDiffersFromTheSetting()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stderr: "Username or password is incorrect.\n", exitCode: 1));
    using var ops = new CompanionOps(svc);
    try
    {
      Assert.False(svc.Settings!.RememberSession.Value);

      await LoginOpAsync(ops, new LoginRequest("user@test.com", "pass", 0, null, true));

      Assert.True(svc.Settings.RememberSession.Value);
      Assert.True(File.Exists(settingsPath));
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task LoginOp_TwoFactorRequired_ReportsItWithoutAnError()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stderr: "Two-step login is required.\n", exitCode: 1));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await LoginOpAsync(ops, new LoginRequest("user@test.com", "pass", null, null, false));

      Assert.Equal(new LoginResponse(false, null, TwoFactorRequired: true), reply);
      Assert.DoesNotContain("--method", factory.LastArgs!, StringComparison.Ordinal);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task LoginOp_EmailMethod_AsksTheCliToSendTheCode()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stderr: "Two-step login is required.\n", exitCode: 1));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await LoginOpAsync(ops, new LoginRequest("user@test.com", "pass", TwoFactorRules.Email, null, false));

      Assert.True(reply.TwoFactorRequired);
      Assert.Contains("--method 1", factory.LastArgs!, StringComparison.Ordinal);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task LoginOp_WithCode_PassesMethodAndCode()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session_key_abc\n", exitCode: 0));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await LoginOpAsync(ops, new LoginRequest("user@test.com", "pass", TwoFactorRules.Authenticator, " 123456 ", false));
      await ops.PostLoginSync;

      Assert.True(reply.Success);
      Assert.Contains("--method 0 --code \"123456\"", factory.LastArgs ?? string.Empty, StringComparison.Ordinal);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Theory]
  [InlineData("Invalid Code supplied", "Invalid 2FA code - try again")]
  [InlineData("boom", "boom")]
  [InlineData("", "Login failed")]
  public async Task LoginOp_WithCode_Failure_MapsTheError(string stderr, string expected)
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stderr: stderr, exitCode: 1));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await LoginOpAsync(ops, new LoginRequest("user@test.com", "pass", 0, "123456", false));

      Assert.Equal(new LoginResponse(false, expected), reply);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task LoginOp_Failure_ReturnsTheCliError()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stderr: "Username or password is incorrect.\n", exitCode: 1));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await LoginOpAsync(ops, new LoginRequest("user@test.com", "wrong", 0, null, false));

      Assert.Equal(new LoginResponse(false, "Username or password is incorrect."), reply);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Theory]
  [InlineData("", "pass")]
  [InlineData("user@test.com", "")]
  public async Task LoginOp_MissingCredentials_FailsWithoutCallingTheCli(string email, string password)
  {
    var (svc, factory, settingsPath) = CreateService();
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await LoginOpAsync(ops, new LoginRequest(email, password, 0, null, false));

      Assert.Equal(new LoginResponse(false, "Enter your email and master password"), reply);
      Assert.Null(factory.LastPsi);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task LoginOp_DeviceVerification_IsReportedAndTheOtpCompletesTheLogin()
  {
    var (svc, factory, settingsPath) = CreateService();
    var login = DevicePrompt(30);
    factory.Enqueue(login);
    for (var i = 0; i < 3; i++)
      factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.", exitCode: 0));
    using var ops = new CompanionOps(svc);
    try
    {
      var first = await LoginOpAsync(ops, new LoginRequest("user@test.com", "pass", 0, null, false));
      Assert.Equal(new LoginResponse(false, null, DeviceVerificationRequired: true), first);

      login.StdoutStream.Enqueue("session_key_otp\n");
      login.StdoutStream.Complete();
      var second = await DeviceOpAsync(ops, "123456");
      await ops.PostLoginSync;

      Assert.Equal(new LoginResponse(true, null), second);
      Assert.True(svc.IsUnlocked);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task DeviceVerificationOp_InvalidCode_FailsWithoutTouchingThePendingProcess()
  {
    var (svc, factory, settingsPath) = CreateService();
    var login = DevicePrompt(31);
    factory.Enqueue(login);
    using var ops = new CompanionOps(svc);
    try
    {
      await LoginOpAsync(ops, new LoginRequest("user@test.com", "pass", 0, null, false));

      var reply = await DeviceOpAsync(ops, "12");

      Assert.False(reply.Success);
      Assert.False(login.Killed);
      ops.CancelLogin();
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task DeviceVerificationOp_WithoutAPendingLogin_ReportsIt()
  {
    var (svc, _, settingsPath) = CreateService();
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await DeviceOpAsync(ops, "123456");

      Assert.False(reply.Success);
      Assert.Contains("No pending device verification", reply.Error, StringComparison.Ordinal);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task CancelLoginOp_DisposesThePendingDeviceVerificationProcess()
  {
    var (svc, factory, settingsPath) = CreateService();
    var login = DevicePrompt(32);
    factory.Enqueue(login);
    using var ops = new CompanionOps(svc);
    try
    {
      await LoginOpAsync(ops, new LoginRequest("user@test.com", "pass", 0, null, false));
      Assert.False(login.Disposed);

      Assert.Null(await ops.HandleAsync(Ops.CancelLogin, null, CancellationToken.None));

      Assert.True(login.Killed);
      Assert.True(login.Disposed);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public void BuildLoginDialog_CarriesTheRememberSettingAndNoDataDirByDefault()
  {
    var (svc, _, settingsPath) = CreateService();
    using var ops = new CompanionOps(svc);
    try
    {
      svc.Settings!.RememberSession.Value = true;

      var dialog = ops.BuildLoginDialog(77);

      Assert.Equal(new LoginDialogRequest(77, true, null), dialog);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }
}
