using System.IO;
using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Pages;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class BitwardenCliServiceMockedTests
{
  private static (BitwardenCliService Service, FakeProcessFactory Factory) CreateService()
  {
    var factory = new FakeProcessFactory();
    var svc = new BitwardenCliService(processFactory: factory.Create) { DataFileOverride = Path.Combine(Path.GetTempPath(), $"bw_missing_{Guid.NewGuid():N}.json") };
    return (svc, factory);
  }

  [Fact]
  public async Task GetVaultStatus_CliNotAvailable_ReturnsCliNotFound()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "", exitCode: 1));
    var result = await svc.GetVaultStatusAsync();
    Assert.Equal(VaultStatus.CliNotFound, result);
  }

  [Fact]
  public async Task GetVaultStatus_CliAvailable_NoSession_FetchesStatus_Unauthenticated()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"unauthenticated\",\"serverUrl\":\"https://vault.bitwarden.com\"}\n", exitCode: 0));
    var result = await svc.GetVaultStatusAsync();
    Assert.Equal(VaultStatus.Unauthenticated, result);
  }

  [Fact]
  public async Task GetVaultStatus_CliAvailable_NoSession_FetchesStatus_Locked()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"locked\",\"serverUrl\":\"https://vault.bitwarden.com\"}\n", exitCode: 0));
    var result = await svc.GetVaultStatusAsync();
    Assert.Equal(VaultStatus.Locked, result);
  }

  [Fact]
  public async Task GetVaultStatus_WithSession_VerifySucceeds_ReturnsUnlocked()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("test-session-key");
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"unlocked\"}\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));
    var result = await svc.GetVaultStatusAsync();
    Assert.Equal(VaultStatus.Unlocked, result);
  }

  [Fact]
  public async Task GetVaultStatus_WithSession_StatusGateLocked_FallsToFetchStatus()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("bad-session");
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"locked\"}\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"locked\"}\n", exitCode: 0));
    var result = await svc.GetVaultStatusAsync();
    Assert.Equal(VaultStatus.Locked, result);
  }

  [Fact]
  public async Task GetVaultStatus_WithSession_StatusUnlockedButDecryptFails_FallsToFetchStatus()
  {
    // Status reports unlocked but the key cannot decrypt (e.g. after a server-side key rotation), so only the list-folders step fails.
    var (svc, factory) = CreateService();
    svc.SetSession("stale-session");
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"unlocked\"}\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Vault is locked.\n", exitCode: 1));
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"locked\"}\n", exitCode: 0));
    var result = await svc.GetVaultStatusAsync();
    Assert.Equal(VaultStatus.Locked, result);
  }

  [Fact]
  public async Task Unlock_Success_SetsSessionAndUnlocked()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session_key_abc\n", stderr: "", exitCode: 0));
    var (success, error) = await svc.UnlockAsync("masterpass");
    Assert.True(success);
    Assert.Null(error);
    Assert.True(svc.IsUnlocked);
    Assert.Equal(VaultStatus.Unlocked, svc.LastStatus);
  }

  [Fact]
  public async Task Unlock_WrongPassword_ReturnsError()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Invalid master password.\n", exitCode: 1));
    var (success, error) = await svc.UnlockAsync("wrongpass");
    Assert.False(success);
    Assert.Equal("Invalid master password.", error);
  }

  [Fact]
  public async Task Unlock_ReadFails_KillsProcess()
  {
    var (svc, factory) = CreateService();
    var process = new FakeCliProcess(exitCode: 0, id: 301);
    process.StdoutStream.Fail(new IOException("pipe broken"));
    process.StderrStream.Complete();
    factory.Enqueue(process);

    var (success, _) = await svc.UnlockAsync("masterpass");

    Assert.False(success);
    Assert.True(process.Killed);
  }

  [Fact]
  public async Task Login_ReadFails_KillsAndDisposesProcess()
  {
    var (svc, factory) = CreateService();
    var process = new FakeCliProcess(exitCode: 0, id: 301);
    process.StdoutStream.Fail(new IOException("pipe broken"));
    process.StderrStream.Complete();
    factory.Enqueue(process);

    var (success, _, _, _) = await svc.LoginAsync("user@test.com", "pass");

    Assert.False(success);
    Assert.True(process.Killed);
    Assert.True(process.Disposed);
  }

  [Fact]
  public async Task Unlock_NotLoggedIn_ResetsToLoggedOut()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("old-key");
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "You are not logged in.\n", exitCode: 1));
    var statusChangedFired = false;
    svc.StatusChanged += () => statusChangedFired = true;
    var (success, error) = await svc.UnlockAsync("pass");
    Assert.False(success);
    Assert.False(svc.IsUnlocked);
    Assert.True(statusChangedFired);
  }

  [Fact]
  public async Task Unlock_PassesBwMpEnvironmentVariable()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "key123\n", stderr: "", exitCode: 0));
    await svc.UnlockAsync("my_secret_pass");
    Assert.Equal("my_secret_pass", factory.LastPsi?.Environment["BW_MP"]);
  }

  [Fact]
  public async Task Login_Success_SetsSessionKey()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session_key_xyz\n", stderr: "", exitCode: 0));
    var (success, error, twoFa, deviceVerification) = await svc.LoginAsync("user@test.com", "pass");
    Assert.True(success);
    Assert.Null(error);
    Assert.False(twoFa);
    Assert.False(deviceVerification);
    Assert.True(svc.IsUnlocked);
  }

  [Fact]
  public async Task Login_TwoFactorRequired_DetectedViaStderr()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Two-step login is required.\n", exitCode: 1));
    var (success, error, twoFa, deviceVerification) = await svc.LoginAsync("user@test.com", "pass");
    Assert.False(success);
    Assert.True(twoFa);
    Assert.False(deviceVerification);
  }

  [Fact]
  public async Task Login_DeviceVerification_DetectedViaStderr()
  {
    var (svc, factory) = CreateService();
    var process = new FakeCliProcess(exitCode: 0, id: 10);
    process.StderrStream.Enqueue("? A login request was made from a device verification...\n");
    process.StderrStream.Complete();
    factory.Enqueue(process);

    var (success, error, twoFa, deviceVerification) = await svc.LoginAsync("user@test.com", "pass");
    Assert.False(success);
    Assert.False(twoFa);
    Assert.True(deviceVerification);
    Assert.Contains("device verification", error!, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Login_Failure_ReturnsErrorMessage()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Username or password is incorrect.\n", exitCode: 1));
    var (success, error, twoFa, deviceVerification) = await svc.LoginAsync("user@test.com", "wrong");
    Assert.False(success);
    Assert.False(twoFa);
    Assert.False(deviceVerification);
    Assert.Equal("Username or password is incorrect.", error);
  }

  [Fact]
  public async Task Login_WithTwoFactorCode_IncludesCodeInArgs()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session123\n", stderr: "", exitCode: 0));
    var (success, _, _, _) = await svc.LoginAsync("user@test.com", "pass", "123456");
    Assert.True(success);
    Assert.Contains("--code", factory.LastPsi!.Arguments, StringComparison.Ordinal);
    Assert.Contains("123456", factory.LastPsi!.Arguments, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Login_WithTwoFactorCode_DefaultsToTotpMethod()
  {
    // Regression: self-hosted servers returned email as the default 2FA method when --method was omitted, rejecting valid TOTP codes.
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session123\n", stderr: "", exitCode: 0));
    await svc.LoginAsync("user@test.com", "pass", "123456");
    Assert.Contains("--method 0", factory.LastPsi!.Arguments, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Login_WithTwoFactorCodeAndMethod_IncludesMethodInArgs()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session123\n", stderr: "", exitCode: 0));
    await svc.LoginAsync("user@test.com", "pass", "123456", 1);
    Assert.Contains("--method 1", factory.LastPsi!.Arguments, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Login_WithExplicitNullMethod_OmitsMethodFlag()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session123\n", stderr: "", exitCode: 0));
    await svc.LoginAsync("user@test.com", "pass", "123456", twoFactorMethod: null);
    Assert.DoesNotContain("--method", factory.LastPsi!.Arguments, StringComparison.Ordinal);
    Assert.Contains("--code", factory.LastPsi!.Arguments, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(3)]
  public async Task Login_NonEmailMethodWithoutCode_OmitsMethodFlag(int method)
  {
    // Non-Email methods omit --method on the first attempt: a self-hosted server picking the wrong default would reject a valid code (#139/#158).
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Two-step login is required.\n", exitCode: 1));
    await svc.LoginAsync("user@test.com", "pass", twoFactorCode: null, twoFactorMethod: method);
    Assert.DoesNotContain("--method", factory.LastPsi!.Arguments, StringComparison.Ordinal);
    Assert.DoesNotContain("--code", factory.LastPsi!.Arguments, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Login_EmailMethodWithoutCode_PassesMethodToTriggerSend()
  {
    // The CLI only emails a code when --method 1 is passed (it calls send-email-login), so Email sends it on the first attempt (#157).
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Two-step login is required.\n", exitCode: 1));
    await svc.LoginAsync("user@test.com", "pass", twoFactorCode: null, twoFactorMethod: 1);
    Assert.Contains("--method 1", factory.LastPsi!.Arguments, StringComparison.Ordinal);
    Assert.DoesNotContain("--code", factory.LastPsi!.Arguments, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Login_EmailMethod_ServerRejectsWith422_ReturnsUseAnotherMethodError()
  {
    // vaultwarden without the #7225 fix 422s send-email-login, so no code arrives and a "use another method" error is surfaced.
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "{\"response\":null,\"statusCode\":422}\n", stderr: "", exitCode: 1));
    var (success, error, twoFa, deviceVerification) = await svc.LoginAsync("user@test.com", "pass", twoFactorCode: null, twoFactorMethod: 1);
    Assert.False(success);
    Assert.False(twoFa);
    Assert.False(deviceVerification);
    Assert.Contains("Email 2FA isn't supported", error!, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Login_SanitizesEmail()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session123\n", stderr: "", exitCode: 0));
    await svc.LoginAsync("user\"@test.com", "pass");
    Assert.DoesNotContain("\"@", factory.LastPsi!.Arguments, StringComparison.Ordinal);
  }

  [Fact]
  public async Task SubmitDeviceVerification_NoPendingProcess_ReturnsError()
  {
    var (svc, _) = CreateService();
    var (success, error) = await svc.SubmitDeviceVerificationAsync("123456");
    Assert.False(success);
    Assert.Contains("No pending device verification", error, StringComparison.Ordinal);
  }

  [Fact]
  public async Task SubmitDeviceVerification_Success_SetsSession()
  {
    var (svc, factory) = CreateService();

    var loginProcess = new FakeCliProcess(exitCode: 0, id: 20);
    loginProcess.StderrStream.Enqueue("? A login request was made... device verification required\n");
    loginProcess.StderrStream.Complete();
    factory.Enqueue(loginProcess);

    var (_, _, _, deviceVerification) = await svc.LoginAsync("user@test.com", "pass");
    Assert.True(deviceVerification);

    loginProcess.StdoutStream.Enqueue("session_key_otp_verified\n");
    loginProcess.StdoutStream.Complete();

    var submitTask = svc.SubmitDeviceVerificationAsync("123456");
    var (success, error) = await submitTask;
    Assert.True(success);
    Assert.Null(error);
    Assert.True(svc.IsUnlocked);
  }

  [Fact]
  public async Task SubmitDeviceVerification_Failure_ReturnsError()
  {
    var (svc, factory) = CreateService();

    var loginProcess = new FakeCliProcess(exitCode: 1, id: 21);
    loginProcess.StderrStream.Enqueue("device verification prompt\n");
    loginProcess.StderrStream.Complete();
    factory.Enqueue(loginProcess);

    await svc.LoginAsync("user@test.com", "pass");

    loginProcess.StdoutStream.Enqueue("");
    loginProcess.StdoutStream.Complete();

    var (success, error) = await svc.SubmitDeviceVerificationAsync("wrong-code");
    Assert.False(success);
  }

  [Fact]
  public async Task Logout_ClearsSessionAndStatus()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("session-key");
    factory.Enqueue(new FakeCliProcess(stdout: "You have logged out.\n", exitCode: 0));
    var statusChanged = false;
    svc.StatusChanged += () => statusChanged = true;
    var realIconDir = FaviconService.CacheDir;
    var tempIconDir = Path.Combine(Path.GetTempPath(), $"bw-icons-{Guid.NewGuid():N}");
    FaviconService.CacheDir = tempIconDir;
    try
    {
      await svc.LogoutAsync();
    }
    finally
    {
      FaviconService.CacheDir = realIconDir;
      if (Directory.Exists(tempIconDir)) Directory.Delete(tempIconDir, recursive: true);
    }
    Assert.False(svc.IsUnlocked);
    Assert.Equal(VaultStatus.Unauthenticated, svc.LastStatus);
    Assert.True(statusChanged);
  }

  [Fact]
  public async Task Lock_ClearsSessionAndSetsLocked()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("session-key");
    factory.Enqueue(new FakeCliProcess(stdout: "Your vault is locked.\n", exitCode: 0));
    var statusChanged = false;
    svc.StatusChanged += () => statusChanged = true;
    await svc.LockAsync();
    Assert.False(svc.IsUnlocked);
    Assert.Equal(VaultStatus.Locked, svc.LastStatus);
    Assert.True(statusChanged);
  }

  [Fact]
  public async Task Lock_DuringRefresh_KillsProcessesAndDropsResult()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("session-key");
    var folders = new FakeCliProcess(exitCode: 0, id: 101);
    var items = new FakeCliProcess(exitCode: 0, id: 102);
    var orgs = new FakeCliProcess(exitCode: 0, id: 103);
    factory.Enqueue(folders);
    factory.Enqueue(items);
    factory.Enqueue(orgs);
    var cacheUpdated = false;
    svc.CacheUpdated += () => cacheUpdated = true;

    var refresh = svc.RefreshCacheAsync();
    factory.Enqueue(new FakeCliProcess(stdout: "Your vault is locked.\n", exitCode: 0, id: 104));
    await svc.LockAsync();

    foreach (var p in new[] { folders, items, orgs })
    {
      p.StdoutStream.Enqueue("[]\n");
      p.StdoutStream.Complete();
      p.StderrStream.Complete();
    }
    await refresh;

    Assert.True(items.Killed);
    Assert.False(svc.IsCacheLoaded);
    Assert.False(cacheUpdated);
  }

  [Fact]
  public async Task SetServerUrl_Success_SetsUrlAndReturnsNull()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "Saved setting `config`.\n", exitCode: 0));
    var config = new ServerConfig("https://vault.example.com");
    BitwardenCliService.ResetStaticState();
    var error = await svc.SetServerUrlAsync(config);
    Assert.Null(error);
    Assert.Equal("https://vault.example.com", BitwardenCliService.ServerUrl);
  }

  [Fact]
  public async Task SetServerUrl_WithIconsOverride_SetsIconsUrl()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "Saved setting `config`.\n", exitCode: 0));
    var config = new ServerConfig("https://vault.example.com", IconsUrl: "https://icons.example.com");
    BitwardenCliService.ResetStaticState();
    var error = await svc.SetServerUrlAsync(config);
    Assert.Null(error);
    Assert.Equal("https://icons.example.com", BitwardenCliService.IconsUrl);
  }

  [Fact]
  public async Task SetServerUrl_WithAllOverrides_PassesAllFlags()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "Saved setting `config`.\n", exitCode: 0));
    var config = new ServerConfig("https://vault.example.com",
        WebVaultUrl: "https://web.example.com",
        ApiUrl: "https://api.example.com",
        IdentityUrl: "https://id.example.com",
        IconsUrl: "https://icons.example.com",
        NotificationsUrl: "https://notify.example.com",
        EventsUrl: "https://events.example.com",
        KeyConnectorUrl: "https://keys.example.com");
    var error = await svc.SetServerUrlAsync(config);
    Assert.Null(error);
    Assert.Contains("--web-vault", factory.LastPsi!.Arguments, StringComparison.Ordinal);
    Assert.Contains("--api", factory.LastPsi.Arguments, StringComparison.Ordinal);
    Assert.Contains("--identity", factory.LastPsi.Arguments, StringComparison.Ordinal);
    Assert.Contains("--icons", factory.LastPsi.Arguments, StringComparison.Ordinal);
    Assert.Contains("--notifications", factory.LastPsi.Arguments, StringComparison.Ordinal);
    Assert.Contains("--events", factory.LastPsi.Arguments, StringComparison.Ordinal);
    Assert.Contains("--key-connector", factory.LastPsi.Arguments, StringComparison.Ordinal);
  }

  [Fact]
  public async Task SetServerUrl_Failure_ReturnsError()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Something went wrong\n", exitCode: 1));
    var config = new ServerConfig("https://bad-url.example.com");
    var error = await svc.SetServerUrlAsync(config);
    Assert.NotNull(error);
    Assert.Contains("Something went wrong", error, StringComparison.Ordinal);
  }

  [Fact]
  public async Task SetServerUrl_NoOutput_ReturnsGenericError()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "", exitCode: 1));
    var config = new ServerConfig("https://bad-url.example.com");
    var error = await svc.SetServerUrlAsync(config);
    Assert.Equal("Failed to set server URL", error);
  }

  [Fact]
  public async Task SetServerUrl_MarksServerUrlResolved()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "Saved setting `config`.\n", exitCode: 0));
    BitwardenCliService.ResetStaticState();
    Assert.False(BitwardenCliService.ServerUrlResolved);
    await svc.SetServerUrlAsync(new ServerConfig("https://vault.example.com"));
    Assert.True(BitwardenCliService.ServerUrlResolved);
  }

  [Fact]
  public void ResetStaticState_ClearsServerUrlResolved()
  {
    BitwardenCliService.ResetStaticState();
    Assert.False(BitwardenCliService.ServerUrlResolved);
    Assert.Null(BitwardenCliService.ServerUrl);
    Assert.Null(BitwardenCliService.IconsUrl);
  }

  [Fact]
  public async Task SetServerUrl_SetsStatusToUnauthenticated()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "Saved setting `config`.\n", exitCode: 0));
    var config = new ServerConfig("https://vault.example.com");
    await svc.SetServerUrlAsync(config);
    Assert.Equal(VaultStatus.Unauthenticated, svc.LastStatus);
  }

  [Fact]
  public async Task RefreshCache_LoadsItemsAndFolders()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("key");
    var items = new JsonArray
    {
      new JsonObject
      {
        ["id"] = "item1",
        ["name"] = "Test Login",
        ["type"] = 1,
        ["revisionDate"] = "2025-01-01T00:00:00Z",
        ["login"] = new JsonObject
        {
          ["username"] = "user",
          ["password"] = "pass",
          ["uris"] = new JsonArray()
        }
      }
    };
    var folders = new JsonArray { new JsonObject { ["id"] = "f1", ["name"] = "Work" } };
    factory.Enqueue(new FakeCliProcess(stdout: folders.ToJsonString() + "\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: items.ToJsonString() + "\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));

    var cacheUpdated = false;
    svc.CacheUpdated += () => cacheUpdated = true;
    await svc.RefreshCacheAsync();
    Assert.True(svc.IsCacheLoaded);
    Assert.True(cacheUpdated);
    var results = svc.SearchCached();
    Assert.Single(results);
    Assert.Equal("Test Login", results[0].Name);
  }

  [Fact]
  public async Task RefreshCache_InvalidSession_ThrowsAndResetsState()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("bad-key");
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Your vault is locked.\n", exitCode: 1));

    var statusChanged = false;
    svc.StatusChanged += () => statusChanged = true;
    await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RefreshCacheAsync());
    Assert.True(statusChanged);
  }

  [Fact]
  public async Task RefreshCache_InFlight_ReportsRefreshingUntilDone()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("key");
    var held = new[] { new FakeCliProcess(exitCode: 0, id: 301), new FakeCliProcess(exitCode: 0, id: 302), new FakeCliProcess(exitCode: 0, id: 303) };
    foreach (var p in held) factory.Enqueue(p);
    var statusChanged = false;
    svc.StatusChanged += () => statusChanged = true;

    var refresh = svc.RefreshCacheAsync();
    Assert.True(svc.IsRefreshing);

    foreach (var p in held)
    {
      p.StdoutStream.Enqueue("[]\n");
      p.StdoutStream.Complete();
      p.StderrStream.Complete();
    }
    await refresh;

    Assert.False(svc.IsRefreshing);
    Assert.True(svc.IsCacheLoaded);
    Assert.False(statusChanged);
  }

  [Fact]
  public async Task RefreshCache_FailsWithNoCache_RaisesStatusChangedAfterRefreshEnds()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("key");
    var held = new[] { new FakeCliProcess(exitCode: 0, id: 311), new FakeCliProcess(exitCode: 0, id: 312), new FakeCliProcess(exitCode: 0, id: 313) };
    foreach (var p in held) factory.Enqueue(p);
    var refreshingWhenRaised = (bool?)null;
    svc.StatusChanged += () => refreshingWhenRaised = svc.IsRefreshing;

    var refresh = svc.RefreshCacheAsync();
    foreach (var p in held)
    {
      p.StdoutStream.Fail(new IOException("pipe broken"));
      p.StderrStream.Complete();
    }
    await refresh;

    Assert.False(svc.IsCacheLoaded);
    Assert.False(refreshingWhenRaised);
  }

  [Fact]
  public async Task SyncVault_RunsSyncThenRefreshesCache()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("key");
    factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.\n", exitCode: 0));
    // Folders, items and organizations are fetched in parallel and dequeued in invocation order.
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));

    await svc.SyncVaultAsync();
    Assert.True(svc.IsCacheLoaded);
  }

  [Fact]
  public async Task SyncVault_RefreshInFlight_RefreshesAgainWhenInFlightFinishes()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("key");
    var held = new[] { new FakeCliProcess(exitCode: 0, id: 201), new FakeCliProcess(exitCode: 0, id: 202), new FakeCliProcess(exitCode: 0, id: 203) };
    foreach (var p in held) factory.Enqueue(p);
    var refresh = svc.RefreshCacheAsync();

    factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.\n", exitCode: 0, id: 204));
    await svc.SyncVaultAsync();

    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0, id: 205));
    factory.Enqueue(new FakeCliProcess(stdout: "[{\"id\":\"1\",\"name\":\"Synced\",\"type\":1,\"revisionDate\":\"2025-01-01T00:00:00Z\",\"login\":{\"username\":\"u\",\"password\":\"p\",\"uris\":[]}}]\n", exitCode: 0, id: 206));
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0, id: 207));
    foreach (var p in held)
    {
      p.StdoutStream.Enqueue("[]\n");
      p.StdoutStream.Complete();
      p.StderrStream.Complete();
    }
    await refresh;

    Assert.Equal("Synced", Assert.Single(svc.SearchCached()).Name);
  }

  [Fact]
  public async Task RefreshQueued_VaultLocksBeforeInFlightFinishes_DoesNotRefreshAgain()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("key");
    var held = new[] { new FakeCliProcess(exitCode: 0, id: 211), new FakeCliProcess(exitCode: 0, id: 212), new FakeCliProcess(exitCode: 0, id: 213) };
    foreach (var p in held) factory.Enqueue(p);
    var refresh = svc.RefreshCacheAsync();
    await svc.RefreshCacheAsync();

    factory.Enqueue(new FakeCliProcess(stdout: "Your vault is locked.\n", exitCode: 0, id: 214));
    await svc.LockAsync();
    foreach (var p in held)
    {
      p.StdoutStream.Enqueue("[]\n");
      p.StdoutStream.Complete();
      p.StderrStream.Complete();
    }
    await refresh;

    Assert.False(svc.IsCacheLoaded);
  }

  [Fact]
  public async Task RefreshQueued_VaultLocksAndSessionRestored_RunsQueuedRefresh()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("key");
    var held = new[] { new FakeCliProcess(exitCode: 0, id: 221), new FakeCliProcess(exitCode: 0, id: 222), new FakeCliProcess(exitCode: 0, id: 223) };
    foreach (var p in held) factory.Enqueue(p);
    var refresh = svc.RefreshCacheAsync();
    await svc.RefreshCacheAsync();

    factory.Enqueue(new FakeCliProcess(stdout: "Your vault is locked.\n", exitCode: 0, id: 224));
    await svc.LockAsync();
    svc.SetSession("restored-key");
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0, id: 225));
    factory.Enqueue(new FakeCliProcess(stdout: "[{\"id\":\"1\",\"name\":\"Restored\",\"type\":1,\"revisionDate\":\"2025-01-01T00:00:00Z\",\"login\":{\"username\":\"u\",\"password\":\"p\",\"uris\":[]}}]\n", exitCode: 0, id: 226));
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0, id: 227));
    foreach (var p in held)
    {
      p.StdoutStream.Enqueue("[]\n");
      p.StdoutStream.Complete();
      p.StderrStream.Complete();
    }
    await refresh;

    Assert.True(svc.IsCacheLoaded);
    Assert.Equal("Restored", Assert.Single(svc.SearchCached()).Name);
  }

  [Fact]
  public async Task Lock_AfterSyncDetached_DoesNotKillSync()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("key");
    var sync = new FakeCliProcess(exitCode: 0, id: 301);
    sync.StdoutStream.Enqueue("Syncing complete.\n");
    factory.Enqueue(sync);
    for (var i = 0; i < 3; i++) factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0, id: 302 + i));
    await svc.SyncVaultAsync();

    factory.Enqueue(new FakeCliProcess(stdout: "Your vault is locked.\n", exitCode: 0, id: 310));
    await svc.LockAsync();

    Assert.False(sync.Killed);
    sync.StdoutStream.Complete();
    sync.StderrStream.Complete();
  }

  [Fact]
  public void Matches_RepromptNoteOutsideGrace_SkipsNotes()
  {
    RepromptPage.GracePeriodSeconds = 60;
    RepromptPage.ClearGracePeriod();
    var item = new BitwardenItem { Id = "rp-1", Name = "Bank", Type = BitwardenItemType.SecureNote, Notes = ProtectedSecret.From("pin 4321"), Reprompt = 1 };

    Assert.False(BitwardenCliService.Matches(item, "4321"));
    Assert.True(BitwardenCliService.Matches(item, "bank"));

    RepromptPage.RecordVerification("rp-1");
    Assert.True(BitwardenCliService.Matches(item, "4321"));
    RepromptPage.ClearGracePeriod();
  }

  [Fact]
  public async Task RunCliAsync_SessionExpired_HandlesInvalidSession()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("expired-key");
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "session key is invalid\n", exitCode: 1));

    var statusChanged = false;
    svc.StatusChanged += () => statusChanged = true;
    await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RefreshCacheAsync());
    Assert.Equal(VaultStatus.Locked, svc.LastStatus);
    Assert.True(statusChanged);
  }

  [Fact]
  public async Task RunCliAsync_JsonOutput_ReturnsParsedLine()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("key");
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "[{\"id\":\"1\",\"name\":\"Item\",\"type\":1,\"revisionDate\":\"2025-01-01T00:00:00Z\",\"login\":{\"username\":\"u\",\"password\":\"p\",\"uris\":[]}}]\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));

    await svc.RefreshCacheAsync();
    var results = svc.SearchCached();
    Assert.Single(results);
  }

  [Fact]
  public async Task Login_DisposePreviousDeviceVerification_OnNewLogin()
  {
    var (svc, factory) = CreateService();

    var process1 = new FakeCliProcess(exitCode: 0, id: 30);
    process1.StderrStream.Enqueue("device verification required\n");
    process1.StderrStream.Complete();
    factory.Enqueue(process1);

    var (_, _, _, dv1) = await svc.LoginAsync("user@test.com", "pass");
    Assert.True(dv1);

    process1.StdoutStream.Complete();

    factory.Enqueue(new FakeCliProcess(stdout: "session_new\n", stderr: "", exitCode: 0));
    var (success, _, _, _) = await svc.LoginAsync("user@test.com", "pass");
    Assert.True(success);
    Assert.True(process1.Disposed);
  }

  [Fact]
  public void SearchCached_ContextPinning_NoRecentItem_ReturnsContextFirst()
  {
    var svc = new BitwardenCliService();
    var now = DateTime.UtcNow;
    svc.LoadTestData([
      new() { Id = "ctx1", Name = "GitHub", Type = BitwardenItemType.Login, Uris = [new ItemUri("https://github.com", UriMatchType.Default)], RevisionDate = now },
      new() { Id = "other", Name = "Other", Type = BitwardenItemType.Login, Uris = [], RevisionDate = now },
    ], []);

    var context = new ForegroundContext
    {
      Windows = [new WindowContext { ProcessName = "chrome", WindowTitle = "GitHub", IsBrowser = true, BrowserUrl = "https://github.com" }]
    };

    var result = svc.SearchCached(null, context, 3);
    Assert.Equal("ctx1", result[0].Id);
  }

  [Fact]
  public void SearchCached_FavoritesOrderedFirst_InNullQuery()
  {
    var svc = new BitwardenCliService();
    var now = DateTime.UtcNow;
    svc.LoadTestData([
      new() { Id = "normal", Name = "AAA Normal", Type = BitwardenItemType.Login, Uris = [], RevisionDate = now },
      new() { Id = "fav", Name = "ZZZ Favorite", Type = BitwardenItemType.Login, Uris = [], RevisionDate = now, Favorite = true },
    ], []);

    var result = svc.SearchCached(null);
    Assert.Equal("fav", result[0].Id);
  }

  [Fact]
  public void SearchCached_TextQuery_WithContext_ContextBoostApplied()
  {
    var svc = new BitwardenCliService();
    var now = DateTime.UtcNow;
    svc.LoadTestData([
      new() { Id = "gh", Name = "GitHub", Type = BitwardenItemType.Login, Uris = [new ItemUri("https://github.com", UriMatchType.Default)], RevisionDate = now },
      new() { Id = "ghalt", Name = "GitHub Alt", Type = BitwardenItemType.Login, Uris = [], RevisionDate = now },
    ], []);

    var context = new ForegroundContext
    {
      Windows = [new WindowContext { ProcessName = "chrome", WindowTitle = "GitHub", IsBrowser = true, BrowserUrl = "https://github.com" }]
    };

    var result = svc.SearchCached("GitHub", context);
    Assert.Equal(2, result.Count);
  }

  [Fact]
  public async Task GetVaultStatus_SetsServerUrlFromStatusJson()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"unauthenticated\",\"serverUrl\":\"https://custom.vault.com\"}\n", exitCode: 0));
    BitwardenCliService.ResetStaticState();
    await svc.GetVaultStatusAsync();
    Assert.Equal("https://custom.vault.com", BitwardenCliService.ServerUrl);
  }

  [Fact]
  public async Task DisablingRememberSession_ClearsSessionAndLocksVault()
  {
    var tempPath = Path.Combine(Path.GetTempPath(), $"bw_test_{Guid.NewGuid():N}.json");
    try
    {
      var settings = new BitwardenSettingsManager(tempPath);
      settings.RememberSession.Value = true;
      var factory = new FakeProcessFactory();
      var svc = new BitwardenCliService(settings, factory.Create);

      svc.SetSession("some-session-key");
      SessionStore.Save("some-session-key");
      Assert.True(svc.IsUnlocked);

      factory.Enqueue(new FakeCliProcess(stdout: "Your vault is locked.\n", exitCode: 0));

      settings.RememberSession.Value = false;
      // Settings.SettingsChanged can only be raised through an internal SDK method.
      var raiseMethod = settings.Settings.GetType().GetMethod(
          "RaiseSettingsChanged",
          System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
      raiseMethod.Invoke(settings.Settings, null);

      await Task.Delay(200, TestContext.Current.CancellationToken);

      Assert.False(svc.IsUnlocked);
      Assert.Equal(VaultStatus.Locked, svc.LastStatus);
      Assert.Null(SessionStore.Load());
    }
    finally
    {
      File.Delete(tempPath);
    }
  }

  [Fact]
  public async Task StartProcess_SetsUtf8Encoding()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("test-key");
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"unlocked\"}\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.\n", exitCode: 0));
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));
    await svc.GetVaultStatusAsync();
    Assert.NotNull(factory.LastPsi);
    Assert.Equal(System.Text.Encoding.UTF8, factory.LastPsi!.StandardOutputEncoding);
    Assert.Equal(System.Text.Encoding.UTF8, factory.LastPsi!.StandardErrorEncoding);
  }

  [Fact]
  public async Task VerifyMasterPassword_CorrectPassword_ReturnsTrue()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session_key_valid\n", stderr: "", exitCode: 0));
    var result = await svc.VerifyMasterPasswordAsync("correct_password");
    Assert.True(result);
  }

  [Fact]
  public async Task VerifyMasterPassword_WrongPassword_ReturnsFalse()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Invalid master password.\n", exitCode: 1));
    var result = await svc.VerifyMasterPasswordAsync("wrong_password");
    Assert.False(result);
  }

  [Fact]
  public async Task VerifyMasterPassword_PassesBwMpEnvVar()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "key123\n", stderr: "", exitCode: 0));
    await svc.VerifyMasterPasswordAsync("test_pass");
    Assert.Equal("test_pass", factory.LastPsi?.Environment["BW_MP"]);
  }

  [Fact]
  public async Task VerifyMasterPassword_SetsNoInteraction()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "key123\n", stderr: "", exitCode: 0));
    await svc.VerifyMasterPasswordAsync("test_pass");
    Assert.Equal("true", factory.LastPsi?.Environment["BW_NOINTERACTION"]);
  }

  [Fact]
  public async Task VerifyMasterPassword_UpdatesSessionKey()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("original_session");
    factory.Enqueue(new FakeCliProcess(stdout: "different_session\n", stderr: "", exitCode: 0));
    await svc.VerifyMasterPasswordAsync("pass");
    Assert.True(svc.IsUnlocked);
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "", exitCode: 0));
  }

  [Fact]
  public async Task VerifyMasterPassword_EmptyStdout_ReturnsFalse()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "\n", stderr: "", exitCode: 0));
    var result = await svc.VerifyMasterPasswordAsync("pass");
    Assert.False(result);
  }

  [Fact]
  public async Task VerifyMasterPassword_StdoutWithSpaces_ReturnsFalse()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "not a valid key\n", stderr: "", exitCode: 0));
    var result = await svc.VerifyMasterPasswordAsync("pass");
    Assert.False(result);
  }
}
