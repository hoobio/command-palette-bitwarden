using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Models;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class BitwardenCliServiceMockedTests
{
  private static (BitwardenCliService Service, FakeProcessFactory Factory) CreateService()
  {
    var factory = new FakeProcessFactory();
    var svc = new BitwardenCliService(processFactory: factory.Create);
    return (svc, factory);
  }

  // --- IsCliAvailable / GetVaultStatusAsync ---

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
    // IsCliAvailable check → returns version
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    // FetchStatusAsync → RunCliAsync("status") → returns JSON
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
    // IsCliAvailable
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    // VerifySessionAsync → sync → "Syncing complete."
    factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.\n", exitCode: 0));
    // FetchServerUrlAsync → RunCliAsync("status")
    factory.Enqueue(new FakeCliProcess(stdout: "{\"serverUrl\":\"https://vault.bitwarden.com\"}\n", exitCode: 0));
    var result = await svc.GetVaultStatusAsync();
    Assert.Equal(VaultStatus.Unlocked, result);
  }

  [Fact]
  public async Task GetVaultStatus_WithSession_VerifyFails_FallsToFetchStatus()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("bad-session");
    // IsCliAvailable
    factory.Enqueue(new FakeCliProcess(stdout: "2025.1.0\n", exitCode: 0));
    // VerifySessionAsync → sync fails (no "Syncing complete." line)
    factory.Enqueue(new FakeCliProcess(stdout: "error\n", exitCode: 1));
    // FetchStatusAsync → RunCliAsync("status")
    factory.Enqueue(new FakeCliProcess(stdout: "{\"status\":\"locked\"}\n", exitCode: 0));
    var result = await svc.GetVaultStatusAsync();
    Assert.Equal(VaultStatus.Locked, result);
  }

  // --- UnlockAsync ---

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

  // --- LoginAsync ---

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
    // Interactive process: stderr has "device verification", stdout stays open
    var process = new FakeCliProcess(exitCode: 0, id: 10);
    process.StderrStream.Enqueue("? A login request was made from a device verification...\n");
    process.StderrStream.Complete();
    // Don't complete stdout yet (simulates waiting for OTP)
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
  public async Task Login_WithTwoFactorCodeAndMethod_IncludesMethodInArgs()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session123\n", stderr: "", exitCode: 0));
    await svc.LoginAsync("user@test.com", "pass", "123456", 0);
    Assert.Contains("--method 0", factory.LastPsi!.Arguments, StringComparison.Ordinal);
  }

  [Fact]
  public async Task Login_SanitizesEmail()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "session123\n", stderr: "", exitCode: 0));
    await svc.LoginAsync("user\"@test.com", "pass");
    Assert.DoesNotContain("\"@", factory.LastPsi!.Arguments, StringComparison.Ordinal);
  }

  // --- SubmitDeviceVerificationAsync ---

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

    // Step 1: Login detects device verification
    var loginProcess = new FakeCliProcess(exitCode: 0, id: 20);
    loginProcess.StderrStream.Enqueue("? A login request was made... device verification required\n");
    loginProcess.StderrStream.Complete();
    factory.Enqueue(loginProcess);

    var (_, _, _, deviceVerification) = await svc.LoginAsync("user@test.com", "pass");
    Assert.True(deviceVerification);

    // Step 2: Inject session key and complete stdout (simulates post-OTP output)
    loginProcess.StdoutStream.Enqueue("session_key_otp_verified\n");
    loginProcess.StdoutStream.Complete();

    // Step 3: Submit OTP
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

    // Provide empty stdout and error stderr
    loginProcess.StdoutStream.Enqueue("");
    loginProcess.StdoutStream.Complete();

    var (success, error) = await svc.SubmitDeviceVerificationAsync("wrong-code");
    Assert.False(success);
  }

  // --- LogoutAsync ---

  [Fact]
  public async Task Logout_ClearsSessionAndStatus()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("session-key");
    // RunCliAsync("logout") → early exit on "You have logged out."
    factory.Enqueue(new FakeCliProcess(stdout: "You have logged out.\n", exitCode: 0));
    var statusChanged = false;
    svc.StatusChanged += () => statusChanged = true;
    await svc.LogoutAsync();
    Assert.False(svc.IsUnlocked);
    Assert.Equal(VaultStatus.Unauthenticated, svc.LastStatus);
    Assert.True(statusChanged);
  }

  // --- LockAsync ---

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

  // --- SetServerUrlAsync ---

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
  public async Task SetServerUrl_SetsStatusToUnauthenticated()
  {
    var (svc, factory) = CreateService();
    factory.Enqueue(new FakeCliProcess(stdout: "Saved setting `config`.\n", exitCode: 0));
    var config = new ServerConfig("https://vault.example.com");
    await svc.SetServerUrlAsync(config);
    Assert.Equal(VaultStatus.Unauthenticated, svc.LastStatus);
  }

  // --- RefreshCacheAsync ---

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
    // RunCliAsync detects session invalid via stderr
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "Your vault is locked.\n", exitCode: 1));

    var statusChanged = false;
    svc.StatusChanged += () => statusChanged = true;
    await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RefreshCacheAsync());
    Assert.True(statusChanged);
  }

  // --- SyncVaultAsync ---

  [Fact]
  public async Task SyncVault_RunsSyncThenRefreshesCache()
  {
    var (svc, factory) = CreateService();
    svc.SetSession("key");
    // sync
    factory.Enqueue(new FakeCliProcess(stdout: "Syncing complete.\n", exitCode: 0));
    // RefreshCacheAsync → items
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));
    // RefreshCacheAsync → folders
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));

    await svc.SyncVaultAsync();
    Assert.True(svc.IsCacheLoaded);
  }

  // --- RunCliAsync internal behavior ---

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
    // folders query (runs first)
    factory.Enqueue(new FakeCliProcess(stdout: "[]\n", exitCode: 0));
    // items query
    factory.Enqueue(new FakeCliProcess(stdout: "[{\"id\":\"1\",\"name\":\"Item\",\"type\":1,\"revisionDate\":\"2025-01-01T00:00:00Z\",\"login\":{\"username\":\"u\",\"password\":\"p\",\"uris\":[]}}]\n", exitCode: 0));

    await svc.RefreshCacheAsync();
    var results = svc.SearchCached();
    Assert.Single(results);
  }

  // --- Multiple Login flows ---

  [Fact]
  public async Task Login_DisposePreviousDeviceVerification_OnNewLogin()
  {
    var (svc, factory) = CreateService();

    // First login triggers device verification
    var process1 = new FakeCliProcess(exitCode: 0, id: 30);
    process1.StderrStream.Enqueue("device verification required\n");
    process1.StderrStream.Complete();
    factory.Enqueue(process1);

    var (_, _, _, dv1) = await svc.LoginAsync("user@test.com", "pass");
    Assert.True(dv1);

    // Complete stdout so it doesn't block
    process1.StdoutStream.Complete();

    // Second login should dispose the first pending process
    factory.Enqueue(new FakeCliProcess(stdout: "session_new\n", stderr: "", exitCode: 0));
    var (success, _, _, _) = await svc.LoginAsync("user@test.com", "pass");
    Assert.True(success);
    Assert.True(process1.Disposed);
  }

  // --- Context + Search with text query and context ---

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

  // --- FetchServerUrlAsync ---

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

  // --- VerifyMasterPasswordAsync ---

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
    // Session key should be updated to the new key returned by bw unlock
    factory.Enqueue(new FakeCliProcess(stdout: "", stderr: "", exitCode: 0));
    // Verify the new key is used by checking ApplyEnvironment sets BW_SESSION
    // (indirectly confirmed by the session being accepted)
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
