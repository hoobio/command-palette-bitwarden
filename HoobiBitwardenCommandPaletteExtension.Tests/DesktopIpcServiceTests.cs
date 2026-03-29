using System;
using System.Security.Cryptography;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class DesktopIpcServiceTests
{
  // --- GetWindowsPipeName ---

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

  // --- EncryptWithSessionKey ---

  [Fact]
  public void EncryptWithSessionKey_Returns_Base64_BinaryType2()
  {
    var sessionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    var data = new byte[] { 1, 2, 3, 4, 5 };

    var result = DesktopIpcService.EncryptWithSessionKey(data, sessionKey);
    var decoded = Convert.FromBase64String(result);

    Assert.Equal(2, decoded[0]);                    // EncryptionType = 2
    Assert.True(decoded.Length >= 1 + 16 + 32 + 16); // header + at least one AES block
  }

  [Fact]
  public void EncryptWithSessionKey_ProducesUniqueOutputEachCall()
  {
    var sessionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    var data = new byte[] { 1, 2, 3 };

    var result1 = DesktopIpcService.EncryptWithSessionKey(data, sessionKey);
    var result2 = DesktopIpcService.EncryptWithSessionKey(data, sessionKey);

    Assert.NotEqual(result1, result2); // different IVs produce different ciphertext
  }

  [Fact]
  public void EncryptWithSessionKey_ThrowsOnWrongKeyLength()
  {
    var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); // only 32 bytes
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

    // Layout: [type(1), iv(16), mac(32), ciphertext]
    var iv = decoded[1..17];
    var mac = decoded[17..49];
    var ciphertext = decoded[49..];

    // Verify HMAC
    using var hmac = new HMACSHA256(keyBytes[32..]);
    hmac.TransformBlock(iv, 0, iv.Length, null, 0);
    hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    Assert.True(CryptographicOperations.FixedTimeEquals(mac, hmac.Hash));

    // Decrypt
    using var aes = Aes.Create();
    aes.Key = keyBytes[..32];
    aes.IV = iv;
    aes.Mode = CipherMode.CBC;
    aes.Padding = PaddingMode.PKCS7;
    using var dec = aes.CreateDecryptor();
    var decrypted = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    Assert.Equal(plaintext, decrypted);
  }
}
