using System.Security.Cryptography.X509Certificates;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class SignatureVerifierTests
{
  [Fact]
  public void IsBitwardenSubject_RealBitwardenSigner_ReturnsTrue()
  {
    var subject = new X500DistinguishedName("CN=Bitwarden Inc., O=Bitwarden Inc., L=Santa Barbara, S=California, C=US, SERIALNUMBER=7654941");
    Assert.True(SignatureVerifier.IsBitwardenSubject(subject));
  }

  [Theory]
  [InlineData("CN=Bitwarden Inc. Evil, O=Evil Bitwarden Inc., C=US")]
  [InlineData("CN=Evil Corp, O=Evil Corp, OU=Bitwarden Inc., C=US")]
  [InlineData("CN=Bitwarden Inc., O=Bitwarden Incorporated, C=US")]
  [InlineData("O=Bitwarden Inc., C=US")]
  [InlineData("CN=Bitwarden Inc., C=US")]
  [InlineData("CN=Bitwarden Inc., O=Bitwarden Inc., O=Evil Corp")]
  [InlineData("CN=bitwarden inc., O=bitwarden inc.")]
  public void IsBitwardenSubject_OtherSigners_ReturnFalse(string dn)
  {
    Assert.False(SignatureVerifier.IsBitwardenSubject(new X500DistinguishedName(dn)));
  }

  [Theory]
  [InlineData(unchecked((int)0x800B010E), true)]
  [InlineData(unchecked((int)0x80092013), true)]
  [InlineData(unchecked((int)0x80092012), true)]
  [InlineData(unchecked((int)0x800B010C), false)]
  [InlineData(unchecked((int)0x800B0100), false)]
  [InlineData(0, false)]
  public void IsRevocationUnknown_ClassifiesResults(int result, bool expected)
  {
    Assert.Equal(expected, SignatureVerifier.IsRevocationUnknown(result));
  }

  [Fact]
  public void IsTrustedBitwardenBinary_UnsignedFile_ReturnsFalse()
  {
    Assert.False(SignatureVerifier.IsTrustedBitwardenBinary(typeof(SignatureVerifierTests).Assembly.Location));
  }

  [Fact]
  public void IsTrustedBitwardenBinary_MissingFile_ReturnsFalse()
  {
    Assert.False(SignatureVerifier.IsTrustedBitwardenBinary(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")));
  }

  [Fact]
  public void IsTrustedBitwardenBinary_NonBitwardenSigner_ReturnsFalse()
  {
    Assert.False(SignatureVerifier.IsTrustedBitwardenBinary(Environment.ProcessPath!));
  }
}
