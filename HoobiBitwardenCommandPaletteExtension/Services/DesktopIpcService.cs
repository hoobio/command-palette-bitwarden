using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HoobiBitwardenCommandPaletteExtension.Services;

/// <summary>
/// Biometrics status enum matching the Bitwarden Desktop app.
/// </summary>
internal enum BiometricsStatus
{
  Available = 0,
  UnlockNeeded = 1,
  HardwareUnavailable = 2,
  AutoSetupNeeded = 3,
  ManualSetupNeeded = 4,
  PlatformUnsupported = 5,
  DesktopDisconnected = 6,
  NotEnabledLocally = 7,
  NotEnabledInConnectedDesktopApp = 8,
  NativeMessagingPermissionMissing = 9,
}

/// <summary>
/// Implements the Bitwarden Desktop IPC protocol for biometric unlock (Windows Hello).
/// Uses the same protocol as the browser extension / bwbio.
///
/// Protocol overview:
///   1. Connect to Desktop app via Windows named pipe.
///   2. RSA-2048 key exchange ("setupEncryption" handshake).
///   3. All subsequent messages are AES-256-CBC + HMAC-SHA256 encrypted.
///   4. Commands: getBiometricsStatusForUser → unlockWithBiometricsForUser.
///   5. userKeyB64 is stored in CLI data.json (encrypted with a freshly generated session key),
///      making that session key valid for "bw list items --session KEY" etc.
/// </summary>
internal static partial class DesktopIpcService
{
  private const int ConnectTimeoutMs = 5_000;

  /// <summary>
  /// Attempt Windows Hello biometric unlock via the Bitwarden Desktop app.
  /// Returns the BW_SESSION key if successful, null if unavailable or cancelled.
  /// </summary>
  public static async Task<string?> TryBiometricUnlockAsync(string? dataDirectory, Action<string>? onStatus = null)
  {
    var userId = GetActiveUserId(dataDirectory);
    if (userId == null)
    {
      DebugLogService.Log("Biometric", "No active user ID found in CLI data.json");
      return null;
    }

    DebugLogService.Log("Biometric", $"Attempting biometric unlock for user {userId[..Math.Min(8, userId.Length)]}...");

    using var client = new IpcClient();
    try
    {
      onStatus?.Invoke("Connecting to Bitwarden Desktop app...");
      await client.ConnectAsync(userId, onStatus);

      onStatus?.Invoke("Checking biometrics availability...");
      var status = await client.GetBiometricsStatusForUserAsync(userId);
      if (status != BiometricsStatus.Available)
      {
        DebugLogService.Log("Biometric", $"Biometrics not available: {status}");
        var reason = status switch
        {
          BiometricsStatus.UnlockNeeded => "Bitwarden Desktop app is locked — unlock it first",
          BiometricsStatus.HardwareUnavailable => "Windows Hello hardware not available",
          BiometricsStatus.AutoSetupNeeded or BiometricsStatus.ManualSetupNeeded => "Biometrics not set up in Bitwarden Desktop app",
          BiometricsStatus.NotEnabledLocally or BiometricsStatus.NotEnabledInConnectedDesktopApp => "Enable 'Unlock with biometrics' in Bitwarden Desktop settings",
          BiometricsStatus.DesktopDisconnected => "Bitwarden Desktop app disconnected",
          _ => $"Biometrics unavailable (status: {status})",
        };
        throw new InvalidOperationException(reason);
      }

      DebugLogService.Log("Biometric", "Requesting Windows Hello prompt from Desktop app...");
      onStatus?.Invoke("Waiting for Windows Hello...");
      var userKeyB64 = await client.UnlockWithBiometricsForUserAsync(userId);
      if (userKeyB64 == null)
      {
        DebugLogService.Log("Biometric", "Biometric unlock denied or failed");
        return null;
      }

      var sessionKey = GenerateSessionKey();
      StoreUserKey(userKeyB64, userId, sessionKey, dataDirectory);
      DebugLogService.Log("Biometric", "Biometric unlock successful, session key stored");
      return sessionKey;
    }
    catch (TimeoutException ex)
    {
      DebugLogService.Log("Biometric", $"Biometric unlock timed out: {ex.Message}");
      throw;
    }
    catch (InvalidOperationException)
    {
      throw;
    }
    catch (Exception ex)
    {
      DebugLogService.Log("Biometric", $"Biometric unlock failed: {ex.GetType().Name}: {ex.Message}");
      throw new IOException($"Cannot connect to Bitwarden Desktop app: {ex.Message}", ex);
    }
  }

  /// <summary>
  /// Check whether biometric unlock is available without prompting the user.
  /// </summary>
  public static async Task<bool> IsBiometricsAvailableAsync(string? dataDirectory)
  {
    var userId = GetActiveUserId(dataDirectory);
    if (userId == null) return false;

    using var client = new IpcClient();
    try
    {
      await client.ConnectAsync(userId);
      var status = await client.GetBiometricsStatusForUserAsync(userId);
      return status == BiometricsStatus.Available;
    }
    catch
    {
      return false;
    }
  }

  private static string GenerateSessionKey() =>
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

  private static void StoreUserKey(string userKeyB64, string userId, string sessionKey, string? dataDirectory)
  {
    var dataPath = GetCliDataPath(dataDirectory);
    var storageKey = $"__PROTECTED__{userId}_user_auto";

    JsonObject data;
    try
    {
      var content = File.ReadAllText(dataPath, Encoding.UTF8);
      data = JsonNode.Parse(content)?.AsObject() ?? new JsonObject();
    }
    catch
    {
      data = new JsonObject();
    }

    var userKeyBytes = Convert.FromBase64String(userKeyB64);
    data[storageKey] = EncryptWithSessionKey(userKeyBytes, sessionKey);

    var dir = Path.GetDirectoryName(dataPath)!;
    Directory.CreateDirectory(dir);
    File.WriteAllText(dataPath, data.ToJsonString(), Encoding.UTF8);
  }

  /// <summary>
  /// Encrypt data using AES-256-CBC + HMAC-SHA256 in Bitwarden's binary type-2 format.
  /// Layout: [2 (1 byte) | IV (16 bytes) | MAC (32 bytes) | ciphertext] → base64
  /// </summary>
  internal static string EncryptWithSessionKey(byte[] data, string sessionKey)
  {
    var keyBytes = Convert.FromBase64String(sessionKey);
    if (keyBytes.Length != 64)
      throw new ArgumentException("Session key must be 64 bytes when decoded", nameof(sessionKey));

    ReadOnlySpan<byte> encKey = keyBytes.AsSpan(0, 32);
    ReadOnlySpan<byte> macKey = keyBytes.AsSpan(32, 32);

    var iv = RandomNumberGenerator.GetBytes(16);

    byte[] ciphertext;
    using (var aes = Aes.Create())
    {
      aes.Key = encKey.ToArray();
      aes.IV = iv;
      aes.Mode = CipherMode.CBC;
      aes.Padding = PaddingMode.PKCS7;
      using var enc = aes.CreateEncryptor();
      ciphertext = enc.TransformFinalBlock(data, 0, data.Length);
    }

    using var hmac = new HMACSHA256(macKey.ToArray());
    hmac.TransformBlock(iv, 0, iv.Length, null, 0);
    hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    var mac = hmac.Hash!;

    var result = new byte[1 + 16 + 32 + ciphertext.Length];
    result[0] = 2; // EncryptionType = AesCbc256_HmacSha256_B64
    Buffer.BlockCopy(iv, 0, result, 1, 16);
    Buffer.BlockCopy(mac, 0, result, 17, 32);
    Buffer.BlockCopy(ciphertext, 0, result, 49, ciphertext.Length);
    return Convert.ToBase64String(result);
  }

  private static string? GetActiveUserId(string? dataDirectory)
  {
    try
    {
      var dataPath = GetCliDataPath(dataDirectory);
      var content = File.ReadAllText(dataPath, Encoding.UTF8);
      var data = JsonNode.Parse(content)?.AsObject();
      return data?["global_account_activeAccountId"]?.GetValue<string>();
    }
    catch
    {
      return null;
    }
  }

  private static string GetCliDataPath(string? dataDirectory)
  {
    var dir = dataDirectory
      ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bitwarden CLI");
    return Path.Combine(dir, "data.json");
  }

  /// <summary>
  /// Compute the Windows named pipe name for the Bitwarden Desktop app.
  /// Algorithm: SHA-256(UTF-8 bytes of home directory) → URL-safe base64 without padding → append ".s.bw"
  /// </summary>
  internal static string GetWindowsPipeName()
  {
    var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(homeDir));
    var hashB64 = Convert.ToBase64String(hash)
      .Replace('+', '-')
      .Replace('/', '_')
      .TrimEnd('=');
    return $"{hashB64}.s.bw";
  }

  // ───────────────────────────────────────────────────────────────────────────
  // IpcClient — handles connection, encryption handshake, and command dispatch
  // ───────────────────────────────────────────────────────────────────────────

  private sealed partial class IpcClient : IDisposable
  {
    private const int ProtocolTimeoutMs = 10_000;
    private const int BiometricTimeoutMs = 60_000;
    // Stable app ID — Desktop whitelists this once; a random ID would force approval on every run
    private const string AppId = "hoobi-bitwarden-cmdpal";

    private static readonly string KeyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HoobiBitwardenCommandPalette",
        "ipc_key.pem");

    private NamedPipeClientStream? _pipe;
    private RSA? _rsa;
    private byte[]? _publicKeyDer;
    private byte[]? _sharedSecret; // 64 bytes once key exchange completes
    private int _nextMessageId;
    private Action<string>? _onStatus;
    private string? _userId;

    private readonly Dictionary<int, TaskCompletionSource<JsonObject>> _pending = [];
    private TaskCompletionSource<bool>? _encryptionReady;

    private CancellationTokenSource? _readCts;
    private Task? _readLoop;

    public async Task ConnectAsync(string? userId = null, Action<string>? onStatus = null)
    {
      _onStatus = onStatus;
      _userId = userId;
      var pipeName = GetWindowsPipeName();
      DebugLogService.Log("Biometric", "Connecting to Desktop app pipe");

      _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
      try
      {
        using var cts = new CancellationTokenSource(ConnectTimeoutMs);
        await _pipe.ConnectAsync(cts.Token);
      }
      catch (OperationCanceledException)
      {
        throw new IOException("Bitwarden Desktop app is not running or 'Allow browser integration' is disabled");
      }

      DebugLogService.Log("Biometric", "Pipe connected, initiating encrypted handshake");
      onStatus?.Invoke("Establishing secure channel with Bitwarden Desktop...");

      _readCts = new CancellationTokenSource();
      _readLoop = Task.Run(() => ReadLoopAsync(_readCts.Token));

      await SetupEncryptionAsync();
    }

    private async Task SetupEncryptionAsync()
    {
      _rsa = LoadOrCreateRsaKey();
      _publicKeyDer = _rsa.ExportSubjectPublicKeyInfo();
      var publicKeyB64 = Convert.ToBase64String(_publicKeyDer);

      _encryptionReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

      var msgId = _nextMessageId++;
      var setupMsg = new JsonObject
      {
        ["appId"] = AppId,
        ["message"] = new JsonObject
        {
          ["command"] = "setupEncryption",
          ["publicKey"] = publicKeyB64,
          ["userId"] = _userId,
          ["messageId"] = msgId,
          ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        }
      };

      SendRaw(setupMsg);

      // Proactively prompt user — Desktop may show a fingerprint dialog without notifying us
      _ = Task.Delay(2000).ContinueWith(_ =>
      {
        if (_encryptionReady is { Task.IsCompleted: false })
          _onStatus?.Invoke("Check Bitwarden Desktop app — you may need to approve the connection");
      }, TaskScheduler.Default);

      using var cts = new CancellationTokenSource(ProtocolTimeoutMs * 6); // allow for fingerprint acceptance
      cts.Token.Register(() => _encryptionReady.TrySetException(new TimeoutException("IPC key exchange timed out — check Bitwarden Desktop app is running with 'Allow browser integration' enabled")));
#pragma warning disable VSTHRD003 // _encryptionReady is a ThreadPool-based TCS; awaiter is on Task.Run thread
      await _encryptionReady.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
      DebugLogService.Log("Biometric", "Encrypted IPC channel established");
    }

    public async Task<BiometricsStatus> GetBiometricsStatusForUserAsync(string userId)
    {
      var response = await CallCommandAsync(new JsonObject
      {
        ["command"] = "getBiometricsStatusForUser",
        ["userId"] = userId,
      }, ProtocolTimeoutMs);

      var val = response["response"];
      if (val == null) return BiometricsStatus.DesktopDisconnected;
      return (BiometricsStatus)(int)val;
    }

    public async Task<string?> UnlockWithBiometricsForUserAsync(string userId)
    {
      var response = await CallCommandAsync(new JsonObject
      {
        ["command"] = "unlockWithBiometricsForUser",
        ["userId"] = userId,
      }, BiometricTimeoutMs);

      var success = response["response"]?.GetValue<bool>() ?? false;
      return success ? response["userKeyB64"]?.GetValue<string>() : null;
    }

    private async Task<JsonObject> CallCommandAsync(JsonObject message, int timeoutMs)
    {
      if (_sharedSecret == null)
        throw new InvalidOperationException("Encrypted channel not established");

      var msgId = _nextMessageId++;
      message["messageId"] = msgId;
      message["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

      var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);

      using var cts = new CancellationTokenSource(timeoutMs);
      cts.Token.Register(() =>
      {
        if (_pending.Remove(msgId, out _))
          tcs.TrySetException(new TimeoutException($"IPC command '{message["command"]}' timed out"));
      });

      _pending[msgId] = tcs;

      var encryptedMsg = EncryptMessage(message);
      var outer = new JsonObject
      {
        ["appId"] = AppId,
        ["message"] = encryptedMsg
      };
      SendRaw(outer);

      return await tcs.Task;
    }

    private JsonObject EncryptMessage(JsonObject message)
    {
      var secret = _sharedSecret!;
      ReadOnlySpan<byte> encKey = secret.AsSpan(0, 32);
      ReadOnlySpan<byte> macKey = secret.AsSpan(32, 32);

      var json = message.ToJsonString();
      var plaintext = Encoding.UTF8.GetBytes(json);
      var iv = RandomNumberGenerator.GetBytes(16);

      byte[] ciphertext;
      using (var aes = Aes.Create())
      {
        aes.Key = encKey.ToArray();
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var enc = aes.CreateEncryptor();
        ciphertext = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
      }

      using var hmac = new HMACSHA256(macKey.ToArray());
      hmac.TransformBlock(iv, 0, iv.Length, null, 0);
      hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
      var mac = hmac.Hash!;

      return new JsonObject
      {
        ["encryptionType"] = 2,
        ["encryptedString"] = $"2.{Convert.ToBase64String(iv)}|{Convert.ToBase64String(ciphertext)}|{Convert.ToBase64String(mac)}",
        ["iv"] = Convert.ToBase64String(iv),
        ["data"] = Convert.ToBase64String(ciphertext),
        ["mac"] = Convert.ToBase64String(mac),
      };
    }

    private JsonObject? DecryptMessage(JsonObject encrypted)
    {
      var secret = _sharedSecret;
      if (secret == null) return null;

      try
      {
        ReadOnlySpan<byte> encKey = secret.AsSpan(0, 32);
        ReadOnlySpan<byte> macKey = secret.AsSpan(32, 32);

        var iv = Convert.FromBase64String(encrypted["iv"]!.GetValue<string>());
        var data = Convert.FromBase64String(encrypted["data"]!.GetValue<string>());
        var mac = Convert.FromBase64String(encrypted["mac"]!.GetValue<string>());

        // Verify HMAC
        using var hmac = new HMACSHA256(macKey.ToArray());
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);
        hmac.TransformFinalBlock(data, 0, data.Length);
        var expectedMac = hmac.Hash!;
        if (!CryptographicOperations.FixedTimeEquals(mac, expectedMac))
          throw new CryptographicException("IPC message MAC verification failed");

        // Decrypt
        using var aes = Aes.Create();
        aes.Key = encKey.ToArray();
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var dec = aes.CreateDecryptor();
        var decrypted = dec.TransformFinalBlock(data, 0, data.Length);

        return JsonNode.Parse(Encoding.UTF8.GetString(decrypted))?.AsObject();
      }
      catch (Exception ex)
      {
        DebugLogService.Log("Biometric", $"IPC message decryption failed: {ex.GetType().Name}: {ex.Message}");
        return null;
      }
    }

    private void SendRaw(JsonObject message)
    {
      if (_pipe == null || !_pipe.IsConnected)
        throw new InvalidOperationException("Not connected to Bitwarden Desktop app");

      var json = message.ToJsonString();
      var payload = Encoding.UTF8.GetBytes(json);
      var buffer = new byte[4 + payload.Length];
      buffer[0] = (byte)(payload.Length & 0xFF);
      buffer[1] = (byte)((payload.Length >> 8) & 0xFF);
      buffer[2] = (byte)((payload.Length >> 16) & 0xFF);
      buffer[3] = (byte)((payload.Length >> 24) & 0xFF);
      Buffer.BlockCopy(payload, 0, buffer, 4, payload.Length);
      _pipe.Write(buffer, 0, buffer.Length);
      _pipe.Flush();
      DebugLogService.Log("Biometric", $"IPC sent: {SummarizeIpcMessage(message)} ({payload.Length} bytes)");
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
      var lenBuf = new byte[4];
      try
      {
        while (!ct.IsCancellationRequested && _pipe?.IsConnected == true)
        {
          // Read 4-byte length prefix
          if (!await ReadExactAsync(lenBuf, 4, ct)) break;

          var msgLen = lenBuf[0] | (lenBuf[1] << 8) | (lenBuf[2] << 16) | (lenBuf[3] << 24);
          if (msgLen <= 0 || msgLen > 1024 * 1024) break; // sanity check

          var msgBuf = new byte[msgLen];
          if (!await ReadExactAsync(msgBuf, msgLen, ct)) break;

          var json = Encoding.UTF8.GetString(msgBuf);
          var msg = JsonNode.Parse(json)?.AsObject();
          if (msg != null)
          {
            DebugLogService.Log("Biometric", $"IPC received: {SummarizeIpcMessage(msg)} ({msgBuf.Length} bytes)");
            HandleMessage(msg);
          }
        }
      }
      catch (OperationCanceledException) { }
      catch (Exception ex)
      {
        DebugLogService.Log("Biometric", $"IPC read loop error: {ex.GetType().Name}: {ex.Message}");
      }

      // Connection lost — reject all pending
      foreach (var tcs in _pending.Values)
        tcs.TrySetException(new IOException("Disconnected from Bitwarden Desktop app"));
      _pending.Clear();
      _encryptionReady?.TrySetException(new IOException("Disconnected from Bitwarden Desktop app"));
    }

    private static string SummarizeIpcMessage(JsonObject msg)
    {
      var command = msg["command"]?.GetValue<string>();
      var messageId = msg["messageId"]?.GetValue<int>();

      if (command != null)
        return $"command={command}, messageId={messageId}";

      var innerMsg = msg["message"]?.AsObject();
      if (innerMsg != null)
      {
        var innerCommand = innerMsg["command"]?.GetValue<string>();
        if (innerCommand != null)
          return $"command={innerCommand}, messageId={innerMsg["messageId"]}";

        if (innerMsg["encryptionType"] != null)
          return $"encrypted, messageId={messageId}";
      }

      if (msg["sharedSecret"] != null)
        return $"sharedSecret (key exchange), messageId={messageId}";

      return $"messageId={messageId}";
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, int count, CancellationToken ct)
    {
      var offset = 0;
      while (offset < count)
      {
        var read = await _pipe!.ReadAsync(buffer.AsMemory(offset, count - offset), ct);
        if (read == 0) return false;
        offset += read;
      }
      return true;
    }

    private void HandleMessage(JsonObject msg)
    {
      var command = msg["command"]?.GetValue<string>();
      var appId = msg["appId"]?.GetValue<string>();

      // During setupEncryption handshake, also check if this message carries sharedSecret
      // regardless of command — Desktop may respond with a different structure
      if (_encryptionReady is { Task.IsCompleted: false } && msg["sharedSecret"] != null)
      {
        DebugLogService.Log("Biometric", "Received sharedSecret, completing key exchange");
        HandleSetupEncryption(msg);
        return;
      }

      switch (command)
      {
        case "setupEncryption":
          HandleSetupEncryption(msg);
          break;

        case "invalidateEncryption":
          if (appId != AppId) return;
var invalidErr = new InvalidOperationException("IPC encryption invalidated by Desktop app");
          _encryptionReady?.TrySetException(invalidErr);
          foreach (var tcs in _pending.Values) tcs.TrySetException(invalidErr);
          _pending.Clear();
          break;

        case "wrongUserId":
          if (appId != AppId) return;
          var wrongUserErr = new InvalidOperationException("Account mismatch: CLI and Desktop app are logged into different accounts");
          _encryptionReady?.TrySetException(wrongUserErr);
          foreach (var tcs in _pending.Values) tcs.TrySetException(wrongUserErr);
          _pending.Clear();
          break;

        case "verifyDesktopIPCFingerprint":
          var fingerprint = _publicKeyDer != null
            ? Helpers.FingerprintHelper.ComputeFingerprint(AppId, _publicKeyDer)
            : "unknown";
          DebugLogService.Log("Biometric", $"Desktop requests fingerprint verification: {fingerprint}");
          _onStatus?.Invoke($"Verify fingerprint: {fingerprint}");
          break;

        default:
          if (appId != AppId) return;
          var innerMsg = msg["message"]?.AsObject();
          if (innerMsg != null)
            HandleEncryptedResponse(innerMsg);
          else
            HandleSetupEncryption(msg); // may contain sharedSecret after fingerprint approval
          break;
      }
    }

    private void HandleSetupEncryption(JsonObject msg)
    {
      try
      {
        var sharedSecretB64 = msg["sharedSecret"]?.GetValue<string>();
        if (string.IsNullOrEmpty(sharedSecretB64) || _rsa == null)
          return;

        var encrypted = Convert.FromBase64String(sharedSecretB64);
        var decrypted = _rsa.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA1);
        _sharedSecret = decrypted;
        _encryptionReady?.TrySetResult(true);
      }
      catch (Exception ex)
      {
        DebugLogService.Log("Biometric", $"Key exchange failed: {ex.GetType().Name}: {ex.Message}");
        _encryptionReady?.TrySetException(ex);
      }
    }

    private void HandleEncryptedResponse(JsonObject rawMsg)
    {
      JsonObject? decrypted;

      // Check if it's an encrypted message or plain JSON
      if (rawMsg["encryptionType"] != null || rawMsg["encryptedString"] != null)
        decrypted = DecryptMessage(rawMsg);
      else
        decrypted = rawMsg;

      if (decrypted == null) return;

      // Timestamp check (10-second validity window)
      var timestamp = decrypted["timestamp"]?.GetValue<long>() ?? 0;
      if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp) > 10_000)
      {
        DebugLogService.Log("Biometric", "Dropping IPC message: timestamp out of window");
        return;
      }

      var msgId = decrypted["messageId"]?.GetValue<int>() ?? -1;
      if (_pending.Remove(msgId, out var tcs))
        tcs.TrySetResult(decrypted);
    }

    private static RSA LoadOrCreateRsaKey()
    {
      try
      {
        if (File.Exists(KeyFilePath))
        {
          var pem = File.ReadAllText(KeyFilePath, Encoding.UTF8);
          var rsa = RSA.Create();
          rsa.ImportFromPem(pem);
          DebugLogService.Log("Biometric", "Loaded persisted IPC RSA key");
          return rsa;
        }
      }
      catch (Exception ex)
      {
        DebugLogService.Log("Biometric", $"Failed to load IPC RSA key, generating new one: {ex.Message}");
      }

      var newRsa = RSA.Create(2048);
      try
      {
        Directory.CreateDirectory(Path.GetDirectoryName(KeyFilePath)!);
        var pem = newRsa.ExportRSAPrivateKeyPem();
        File.WriteAllText(KeyFilePath, pem, Encoding.UTF8);
        DebugLogService.Log("Biometric", "Generated and saved new IPC RSA key");
      }
      catch (Exception ex)
      {
        DebugLogService.Log("Biometric", $"Could not persist IPC RSA key: {ex.Message}");
      }

      return newRsa;
    }

    public void Dispose()
    {
      _readCts?.Cancel();
      _readCts?.Dispose();
      _pipe?.Dispose();
      _rsa?.Dispose();
    }
  }
}
