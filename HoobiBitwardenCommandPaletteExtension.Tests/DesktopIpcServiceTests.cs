using System;
using System.Security.Cryptography;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

[Collection("SessionStore")]
public class DesktopIpcServiceTests
{
  [Fact]
  public void GetWindowsPipeName_ReturnsExpectedFormat()
  {
    var pipeName = DesktopIpcService.GetWindowsPipeName();
    Assert.EndsWith(".s.bw", pipeName, StringComparison.Ordinal);
    Assert.DoesNotContain('+', pipeName);
    Assert.DoesNotContain('/', pipeName);
    Assert.DoesNotContain('=', pipeName);
  }

  [Fact]
  public void GetWindowsPipeName_IsDeterministic()
  {
    var name1 = DesktopIpcService.GetWindowsPipeName();
    var name2 = DesktopIpcService.GetWindowsPipeName();
    Assert.Equal(name1, name2);
  }

  [Fact]
  public async Task IsPipeServerBitwarden_NonBitwardenServer_ReturnsFalse()
  {
    var name = $"bw-test-{Guid.NewGuid():N}";
    using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
    using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
    var accept = server.WaitForConnectionAsync(TestContext.Current.CancellationToken);
    await client.ConnectAsync(5000, TestContext.Current.CancellationToken);
    await accept;

    Assert.False(DesktopIpcService.IsPipeServerBitwarden(client));
  }

  [Fact]
  public void IsPipeServerBitwarden_UnconnectedPipe_ReturnsFalse()
  {
    using var client = new System.IO.Pipes.NamedPipeClientStream(".", $"bw-test-{Guid.NewGuid():N}");
    Assert.False(DesktopIpcService.IsPipeServerBitwarden(client));
  }

  [Fact]
  public void EncryptWithSessionKey_Returns_Base64_BinaryType2()
  {
    var sessionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    var data = new byte[] { 1, 2, 3, 4, 5 };

    var result = DesktopIpcService.EncryptWithSessionKey(data, sessionKey);
    var decoded = Convert.FromBase64String(result);

    Assert.Equal(2, decoded[0]);
    Assert.True(decoded.Length >= 1 + 16 + 32 + 16);
  }

  [Fact]
  public void EncryptWithSessionKey_ProducesUniqueOutputEachCall()
  {
    var sessionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    var data = new byte[] { 1, 2, 3 };

    var result1 = DesktopIpcService.EncryptWithSessionKey(data, sessionKey);
    var result2 = DesktopIpcService.EncryptWithSessionKey(data, sessionKey);

    Assert.NotEqual(result1, result2);
  }

  [Fact]
  public void EncryptWithSessionKey_ThrowsOnWrongKeyLength()
  {
    var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    var data = new byte[] { 1, 2, 3 };

    Assert.Throws<ArgumentException>(() => DesktopIpcService.EncryptWithSessionKey(data, shortKey));
  }

  [Fact]
  public void EncryptWithSessionKey_CanBeDecryptedManually()
  {
    var keyBytes = RandomNumberGenerator.GetBytes(64);
    var sessionKey = Convert.ToBase64String(keyBytes);
    var plaintext = System.Text.Encoding.UTF8.GetBytes("hello bitwarden");

    var result = DesktopIpcService.EncryptWithSessionKey(plaintext, sessionKey);
    var decoded = Convert.FromBase64String(result);

    var iv = decoded[1..17];
    var mac = decoded[17..49];
    var ciphertext = decoded[49..];

    using var hmac = new HMACSHA256(keyBytes[32..]);
    hmac.TransformBlock(iv, 0, iv.Length, null, 0);
    hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    Assert.True(CryptographicOperations.FixedTimeEquals(mac, hmac.Hash));

    using var aes = Aes.Create();
    aes.Key = keyBytes[..32];
    aes.IV = iv;
    aes.Mode = CipherMode.CBC;
    aes.Padding = PaddingMode.PKCS7;
    using var dec = aes.CreateDecryptor();
    var decrypted = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    Assert.Equal(plaintext, decrypted);
  }

  private static string NewTempDir()
  {
    var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bwtest-" + Guid.NewGuid().ToString("N"));
    System.IO.Directory.CreateDirectory(dir);
    return dir;
  }

  private static readonly string SessionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
  private static readonly string UserKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

  [Fact]
  public void StoreUserKey_PreservesExistingDataAndAddsKey()
  {
    var dir = NewTempDir();
    var path = System.IO.Path.Combine(dir, "data.json");
    System.IO.File.WriteAllText(path, "{\"global_account_activeAccountId\":\"u1\",\"other\":42}");

    DesktopIpcService.StoreUserKey(UserKey, "u1", SessionKey, dir);

    var data = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(path))!.AsObject();
    Assert.Equal(42, (int)data["other"]!);
    Assert.NotNull(data["__PROTECTED__u1_user_auto"]);
    Assert.Single(System.IO.Directory.GetFiles(dir));
    System.IO.Directory.Delete(dir, true);
  }

  [Theory]
  [InlineData("{not json")]
  [InlineData("[1,2]")]
  [InlineData("")]
  public void StoreUserKey_UnreadableFile_ThrowsAndLeavesFileUntouched(string content)
  {
    var dir = NewTempDir();
    var path = System.IO.Path.Combine(dir, "data.json");
    System.IO.File.WriteAllText(path, content);

    Assert.Throws<InvalidOperationException>(() => DesktopIpcService.StoreUserKey(UserKey, "u1", SessionKey, dir));

    Assert.Equal(content, System.IO.File.ReadAllText(path));
    Assert.Single(System.IO.Directory.GetFiles(dir));
    System.IO.Directory.Delete(dir, true);
  }

  [Fact]
  public void StoreUserKey_MissingFile_Throws()
  {
    var dir = NewTempDir();

    Assert.Throws<InvalidOperationException>(() => DesktopIpcService.StoreUserKey(UserKey, "u1", SessionKey, dir));

    Assert.Empty(System.IO.Directory.GetFiles(dir));
    System.IO.Directory.Delete(dir, true);
  }
}
