using System.IO;
using HoobiBitwarden.Ipc;
using HoobiBitwardenCommandPaletteExtension.Companion;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class CompanionDialogOpsTests
{
  private static (BitwardenCliService Service, FakeProcessFactory Factory, string SettingsPath) CreateService()
  {
    var settingsPath = Path.Combine(Path.GetTempPath(), $"bw_test_{Guid.NewGuid():N}.json");
    var factory = new FakeProcessFactory();
    var svc = new BitwardenCliService(new BitwardenSettingsManager(settingsPath), factory.Create) { DataFileOverride = Path.Combine(Path.GetTempPath(), $"bw_missing_{Guid.NewGuid():N}.json") };
    return (svc, factory, settingsPath);
  }

  private static async Task<UnlockResponse> UnlockOpAsync(CompanionOps ops, UnlockRequest request) =>
    IpcJson.Read(await ops.HandleAsync(Ops.Unlock, IpcJson.ToElement(request, IpcJson.Default.UnlockRequest), CancellationToken.None), IpcJson.Default.UnlockResponse);

  private static async Task<SetServerResponse> SetServerOpAsync(CompanionOps ops, SetServerRequest request) =>
    IpcJson.Read(await ops.HandleAsync(Ops.SetServer, IpcJson.ToElement(request, IpcJson.Default.SetServerRequest), CancellationToken.None), IpcJson.Default.SetServerResponse);

  [Fact]
  public async Task UnlockOp_Success_UnlocksAndRaisesStatusChangedWhileTheCacheRefreshes()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session_key_abc\n", exitCode: 0));
    for (var i = 0; i < 3; i++)
      factory.Enqueue(new FakeCliProcess(stdout: "[]", exitCode: 0));
    var statusChanged = 0;
    svc.StatusChanged += () => statusChanged++;
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await UnlockOpAsync(ops, new UnlockRequest("masterpass", false));
      await ops.PostUnlockRefresh;

      Assert.Equal(new UnlockResponse(true, null), reply);
      Assert.True(svc.IsUnlocked);
      Assert.True(statusChanged >= 1);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task UnlockOp_AppliesRememberSession_WhenItDiffersFromTheSetting()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stderr: "Invalid master password.\n", exitCode: 1));
    using var ops = new CompanionOps(svc);
    try
    {
      Assert.False(svc.Settings!.RememberSession.Value);

      await UnlockOpAsync(ops, new UnlockRequest("pw", true));

      Assert.True(svc.Settings.RememberSession.Value);
      Assert.True(File.Exists(settingsPath));
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Theory]
  [InlineData("Invalid master password.", "Invalid master password.")]
  [InlineData("Cannot decrypt key", "Invalid password entered")]
  [InlineData("", "Unlock failed")]
  public async Task UnlockOp_Failure_MapsTheError(string stderr, string expected)
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stderr: stderr, exitCode: 1));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await UnlockOpAsync(ops, new UnlockRequest("pw", false));

      Assert.Equal(new UnlockResponse(false, expected), reply);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task UnlockOp_NotLoggedIn_ReportsLoggedOut()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stderr: "You are not logged in.\n", exitCode: 1));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await UnlockOpAsync(ops, new UnlockRequest("pw", false));

      Assert.Equal(new UnlockResponse(false, "You are not logged in", true), reply);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task UnlockOp_EmptyPassword_FailsWithoutCallingTheCli()
  {
    var (svc, factory, settingsPath) = CreateService();
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await UnlockOpAsync(ops, new UnlockRequest("", false));

      Assert.Equal(new UnlockResponse(false, "Enter your master password"), reply);
      Assert.Null(factory.LastPsi);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task UnlockOp_Biometric_UnavailableDesktopApp_ReportsTheFailure()
  {
    var (svc, factory, settingsPath) = CreateService();
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await UnlockOpAsync(ops, new UnlockRequest("", false, true));

      Assert.False(reply.Success);
      Assert.False(string.IsNullOrEmpty(reply.Error));
      Assert.Null(factory.LastPsi);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task SetServerOp_Cloud_SavesTheCloudHost()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "Saved setting `config`.\n", exitCode: 0));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await SetServerOpAsync(ops, new SetServerRequest(ServerRegions.Cloud, null));

      Assert.Equal(new SetServerResponse(true, null), reply);
      Assert.Equal("config server \"bitwarden.com\"", factory.LastArgs);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task SetServerOp_Eu_SavesTheFullEuUrl()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "Saved setting `config`.\n", exitCode: 0));
    using var ops = new CompanionOps(svc);
    try
    {
      await SetServerOpAsync(ops, new SetServerRequest(ServerRegions.Eu, null));

      Assert.Equal("config server \"https://vault.bitwarden.eu\"", factory.LastArgs);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task SetServerOp_SelfHosted_TrimsTheUrl_AndPassesTheOverrides()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "Saved setting `config`.\n", exitCode: 0));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await SetServerOpAsync(ops, new SetServerRequest(ServerRegions.SelfHosted, "  https://vault.example.com ", ApiUrl: " https://api.example.com ", KeyConnectorUrl: "  "));

      Assert.True(reply.Success);
      Assert.Equal("config server \"https://vault.example.com\" --api \"https://api.example.com\"", factory.LastArgs);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Theory]
  [InlineData("", "Enter a valid server URL starting with http:// or https://")]
  [InlineData("vault.example.com", "Enter a valid server URL starting with http:// or https://")]
  [InlineData("http://", "Enter a valid server URL starting with http:// or https://")]
  public async Task SetServerOp_SelfHosted_InvalidUrl_FailsWithoutCallingTheCli(string baseUrl, string expected)
  {
    var (svc, factory, settingsPath) = CreateService();
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await SetServerOpAsync(ops, new SetServerRequest(ServerRegions.SelfHosted, baseUrl));

      Assert.Equal(new SetServerResponse(false, expected), reply);
      Assert.Null(factory.LastPsi);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task SetServerOp_SelfHosted_NonHttpsOverride_NamesTheField()
  {
    var (svc, factory, settingsPath) = CreateService();
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await SetServerOpAsync(ops, new SetServerRequest(ServerRegions.SelfHosted, "https://vault.example.com", IdentityUrl: "http://id.example.com"));

      Assert.Equal(new SetServerResponse(false, "Invalid IdentityUrl: must start with https://"), reply);
      Assert.Null(factory.LastPsi);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task SetServerOp_CliFailure_PassesTheErrorThrough()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stderr: "Something went wrong\n", exitCode: 1));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await SetServerOpAsync(ops, new SetServerRequest(ServerRegions.Cloud, null));

      Assert.Equal(new SetServerResponse(false, "Something went wrong"), reply);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task SetServerOp_LockedVault_LogsOutBeforeSavingTheServer()
  {
    var (svc, factory, settingsPath) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"locked\"}\n", exitCode: 0));
    Assert.Equal(VaultStatus.Locked, await svc.GetVaultStatusAsync());
    factory.Enqueue(new FakeCliProcess(stdout: "You have logged out.\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "Saved setting `config`.\n", exitCode: 0));
    using var ops = new CompanionOps(svc);
    try
    {
      var reply = await SetServerOpAsync(ops, new SetServerRequest(ServerRegions.Cloud, null));

      Assert.True(reply.Success);
      Assert.Equal(VaultStatus.Unauthenticated, svc.LastStatus);
      Assert.Equal("config server \"bitwarden.com\"", factory.LastArgs);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }

  [Fact]
  public async Task SetServerOp_UnknownRegion_IsRejectedAsInvalid()
  {
    var (svc, _, settingsPath) = CreateService();
    using var ops = new CompanionOps(svc);
    try
    {
      var ex = await Assert.ThrowsAsync<IpcException>(() => SetServerOpAsync(ops, new SetServerRequest("mars", null)));

      Assert.Equal(IpcErrors.Invalid, ex.Code);
    }
    finally
    {
      File.Delete(settingsPath);
    }
  }
}
