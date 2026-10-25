using System.IO;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Pages;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class CompanionRepromptOpsTests : IDisposable
{
  private readonly string _id = Guid.NewGuid().ToString();
  private readonly BitwardenCliService _service;
  private readonly CompanionOps _ops;
  private bool _biometricEnabled;
  private bool _biometricVerifies;

  public CompanionRepromptOpsTests()
  {
    RepromptPage.ClearGracePeriod();
    _service = new BitwardenCliService { DataFileOverride = Path.Combine(Path.GetTempPath(), $"bw_missing_{Guid.NewGuid():N}.json") };
    _service.LoadTestData([new BitwardenItem { Id = _id, Name = "GitHub", Type = BitwardenItemType.Login, Reprompt = 1 }], [], []);
    _service.SetSession("session-key");
    _ops = new CompanionOps(_service, password => Task.FromResult(password == "correct"), verifyBiometric: () => Task.FromResult(_biometricVerifies), biometricEnabled: () => _biometricEnabled);
  }

  public void Dispose()
  {
    RepromptPage.ClearGracePeriod();
    _ops.Dispose();
    GC.SuppressFinalize(this);
  }

  private static async Task<RepromptConfirmResponse> ConfirmOpAsync(CompanionOps ops, RepromptConfirmRequest request) =>
    IpcJson.Read(await ops.HandleAsync(Ops.ConfirmReprompt, IpcJson.ToElement(request, IpcJson.Default.RepromptConfirmRequest), CancellationToken.None), IpcJson.Default.RepromptConfirmResponse);

  [Fact]
  public void Register_ReturnsTheDialogWithTheItemNameAndWindowsHelloSetting()
  {
    _biometricEnabled = true;

    var dialog = _ops.RegisterReprompt(_id, () => { }, 55);

    Assert.Equal(55, dialog.ReturnWindow);
    Assert.Equal("GitHub", dialog.ItemName);
    Assert.True(dialog.WindowsHello);
    Assert.Equal(32, dialog.Token.Length);
    Assert.Equal(1, _ops.PendingRepromptCount);
  }

  [Fact]
  public void Register_GivesEveryRequestItsOwnToken()
  {
    var first = _ops.RegisterReprompt(_id, () => { }, 0);
    var second = _ops.RegisterReprompt(_id, () => { }, 0);

    Assert.NotEqual(first.Token, second.Token);
    Assert.Equal(2, _ops.PendingRepromptCount);
  }

  [Fact]
  public async Task Confirm_CorrectPassword_RunsTheActionOnce_StartsTheGrace_AndConsumesTheToken()
  {
    var runs = 0;
    var token = _ops.RegisterReprompt(_id, () => runs++, 0).Token;

    var reply = await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, "correct"));
    var again = await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, "correct"));

    Assert.Equal(new RepromptConfirmResponse(true, null), reply);
    Assert.Equal(1, runs);
    Assert.True(RepromptPage.IsWithinGracePeriod(_id));
    Assert.False(again.Success);
    Assert.Equal(0, _ops.PendingRepromptCount);
  }

  [Fact]
  public async Task Confirm_WrongPassword_KeepsTheTokenAndRunsNothing()
  {
    var runs = 0;
    var token = _ops.RegisterReprompt(_id, () => runs++, 0).Token;

    var reply = await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, "wrong"));

    Assert.Equal(new RepromptConfirmResponse(false, "Incorrect master password. Please try again."), reply);
    Assert.Equal(0, runs);
    Assert.Equal(1, _ops.PendingRepromptCount);
    Assert.False(RepromptPage.IsWithinGracePeriod(_id));

    Assert.True((await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, "correct"))).Success);
    Assert.Equal(1, runs);
  }

  [Fact]
  public async Task Confirm_EmptyPassword_AsksForIt_WithoutCountingAFailure()
  {
    var token = _ops.RegisterReprompt(_id, () => { }, 0).Token;

    var reply = await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, ""));

    Assert.Equal(new RepromptConfirmResponse(false, "Enter your master password"), reply);
    Assert.Equal(0, RepromptPage.GetCooldownSecondsRemaining());
  }

  [Fact]
  public async Task Confirm_RepeatedFailures_StartTheCooldown_AndRefuseEvenTheRightPassword()
  {
    var runs = 0;
    var token = _ops.RegisterReprompt(_id, () => runs++, 0).Token;
    for (var i = 0; i < 5; i++)
      await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, "wrong"));

    var reply = await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, "correct"));

    Assert.False(reply.Success);
    Assert.StartsWith("Too many failed attempts. Try again in", reply.Error, StringComparison.Ordinal);
    Assert.Equal(0, runs);
  }

  [Fact]
  public async Task Confirm_Biometric_UsesWindowsHello()
  {
    _biometricEnabled = true;
    _biometricVerifies = true;
    var runs = 0;
    var token = _ops.RegisterReprompt(_id, () => runs++, 0).Token;

    var reply = await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, null, true));

    Assert.True(reply.Success);
    Assert.Equal(1, runs);
  }

  [Fact]
  public async Task Confirm_BiometricFailure_ReportsIt_AndKeepsTheToken()
  {
    _biometricEnabled = true;
    var token = _ops.RegisterReprompt(_id, () => { }, 0).Token;

    var reply = await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, null, true));

    Assert.Equal(new RepromptConfirmResponse(false, "Windows Hello verification failed"), reply);
    Assert.Equal(1, _ops.PendingRepromptCount);
  }

  [Fact]
  public async Task Confirm_ActionFailure_ReportsIt_AndKeepsTheTokenForARetry()
  {
    var token = _ops.RegisterReprompt(_id, () => throw new ObjectDisposedException("secret"), 0).Token;

    var reply = await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, "correct"));

    Assert.Equal(new RepromptConfirmResponse(false, "Action failed after verification."), reply);
    Assert.Equal(1, _ops.PendingRepromptCount);
  }

  [Fact]
  public async Task Confirm_UnknownToken_ReportsExpiry()
  {
    var reply = await ConfirmOpAsync(_ops, new RepromptConfirmRequest("nope", "correct"));

    Assert.False(reply.Success);
    Assert.Contains("expired", reply.Error, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Confirm_ExpiredToken_DropsItAndRunsNothing()
  {
    _ops.RepromptLifetime = TimeSpan.FromSeconds(-1);
    var runs = 0;
    var token = _ops.RegisterReprompt(_id, () => runs++, 0).Token;

    var reply = await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, "correct"));

    Assert.False(reply.Success);
    Assert.Equal(0, runs);
    Assert.Equal(0, _ops.PendingRepromptCount);
  }

  [Fact]
  public void Register_PrunesExpiredTokens()
  {
    _ops.RepromptLifetime = TimeSpan.FromSeconds(-1);
    _ops.RegisterReprompt(_id, () => { }, 0);
    _ops.RepromptLifetime = TimeSpan.FromMinutes(5);

    _ops.RegisterReprompt(_id, () => { }, 0);

    Assert.Equal(1, _ops.PendingRepromptCount);
  }

  [Fact]
  public async Task CancelOp_DropsTheToken_SoAConfirmCannotRunTheAction()
  {
    var runs = 0;
    var token = _ops.RegisterReprompt(_id, () => runs++, 0).Token;

    Assert.Null(await _ops.HandleAsync(Ops.CancelReprompt, IpcJson.ToElement(new RepromptCancelRequest(token), IpcJson.Default.RepromptCancelRequest), CancellationToken.None));
    var reply = await ConfirmOpAsync(_ops, new RepromptConfirmRequest(token, "correct"));

    Assert.False(reply.Success);
    Assert.Equal(0, runs);
  }

  [Fact]
  public void LockingTheVault_DropsEveryPendingToken()
  {
    _ops.RegisterReprompt(_id, () => { }, 0);
    _service.ClearSession();

    _ops.RaiseIfLocked();

    Assert.Equal(0, _ops.PendingRepromptCount);
  }
}
