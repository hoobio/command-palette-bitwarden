using System.Security.Cryptography;
using HoobiBitwardenCommandPaletteExtension.Helpers;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class FingerprintHelperTests
{
  [Fact]
  public void ComputeFingerprint_ReturnsFiveWordPhrase()
  {
    using var rsa = RSA.Create(2048);
    var publicKeyDer = rsa.ExportSubjectPublicKeyInfo();

    var result = FingerprintHelper.ComputeFingerprint("test-app-id", publicKeyDer);

    var words = result.Split('-');
    Assert.Equal(5, words.Length);
    Assert.All(words, w => Assert.False(string.IsNullOrWhiteSpace(w)));
  }

  [Fact]
  public void ComputeFingerprint_IsDeterministic()
  {
    using var rsa = RSA.Create(2048);
    var publicKeyDer = rsa.ExportSubjectPublicKeyInfo();

    var first = FingerprintHelper.ComputeFingerprint("test-app-id", publicKeyDer);
    var second = FingerprintHelper.ComputeFingerprint("test-app-id", publicKeyDer);

    Assert.Equal(first, second);
  }

  [Fact]
  public void ComputeFingerprint_DifferentAppIds_ProduceDifferentPhrases()
  {
    using var rsa = RSA.Create(2048);
    var publicKeyDer = rsa.ExportSubjectPublicKeyInfo();

    var a = FingerprintHelper.ComputeFingerprint("app-a", publicKeyDer);
    var b = FingerprintHelper.ComputeFingerprint("app-b", publicKeyDer);

    Assert.NotEqual(a, b);
  }

  [Fact]
  public void ComputeFingerprint_DifferentKeys_ProduceDifferentPhrases()
  {
    using var rsa1 = RSA.Create(2048);
    using var rsa2 = RSA.Create(2048);

    var a = FingerprintHelper.ComputeFingerprint("same-id", rsa1.ExportSubjectPublicKeyInfo());
    var b = FingerprintHelper.ComputeFingerprint("same-id", rsa2.ExportSubjectPublicKeyInfo());

    Assert.NotEqual(a, b);
  }

  [Fact]
  public void ComputeFingerprint_WordsAreFromEFFWordList()
  {
    using var rsa = RSA.Create(2048);
    var publicKeyDer = rsa.ExportSubjectPublicKeyInfo();

    var result = FingerprintHelper.ComputeFingerprint("test-id", publicKeyDer);
    var words = result.Split('-');

    Assert.All(words, w => Assert.Contains(w, EFFWordList.Words));
  }

  [Theory]
  [InlineData("test-app-id", 0, "postcard-etching-neglector-mushiness-delusion")]
  [InlineData("bitwarden", 7, "illusion-jarring-tanning-ember-anaerobic")]
  [InlineData("", 200, "library-footgear-ruse-carton-hurler")]
  public void ComputeFingerprint_MatchesGoldenOutput(string appId, int seed, string expected)
  {
    var publicKeyDer = Enumerable.Range(0, 64).Select(i => (byte)(i * 3 + seed)).ToArray();
    Assert.Equal(expected, FingerprintHelper.ComputeFingerprint(appId, publicKeyDer));
  }

  [Fact]
  public void EFFWordList_Has7776Words()
  {
    Assert.Equal(7776, EFFWordList.Words.Length);
  }
}
