using HoobiBitwardenCommandPaletteExtension;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class TwoFactorMethodDescriptorTests
{
  [Fact]
  public void Describe_Authenticator_NumericCopyAndValidator()
  {
    var d = HoobiBitwardenCommandPaletteExtensionPage.DescribeTwoFactorMethod(0);
    Assert.Contains("authenticator", d.Placeholder, StringComparison.OrdinalIgnoreCase);
    Assert.Equal("authenticator code", d.TypedNoun);
    Assert.True(d.Validator("123456"));
    Assert.False(d.Validator("abcdef"));
  }

  [Fact]
  public void Describe_Email_NumericCopyAndValidator()
  {
    var d = HoobiBitwardenCommandPaletteExtensionPage.DescribeTwoFactorMethod(1);
    Assert.Contains("email", d.Placeholder, StringComparison.OrdinalIgnoreCase);
    Assert.Equal("email code", d.TypedNoun);
    Assert.True(d.Validator("654321"));
  }

  [Fact]
  public void Describe_Yubikey_TouchCopyAndAlphanumericValidator()
  {
    var d = HoobiBitwardenCommandPaletteExtensionPage.DescribeTwoFactorMethod(3);
    Assert.Contains("YubiKey", d.Placeholder, StringComparison.Ordinal);
    Assert.Equal("YubiKey OTP", d.TypedNoun);
    // 44-char modhex-style OTP
    Assert.True(d.Validator("cccccccbcdefghijklnrtuv1234567890abcdefghijk"));
    // Numeric TOTP code should be rejected for YubiKey
    Assert.False(d.Validator("123456"));
  }

  [Theory]
  [InlineData(null)]
  [InlineData(2)]   // Duo (not in dropdown)
  [InlineData(99)]  // Unknown
  public void Describe_UnknownOrNull_FallsBackToGenericNumeric(int? method)
  {
    var d = HoobiBitwardenCommandPaletteExtensionPage.DescribeTwoFactorMethod(method);
    Assert.Equal("2FA code", d.TypedNoun);
    Assert.True(d.Validator("123456"));
    Assert.False(d.Validator("ab"));
  }

  [Theory]
  [InlineData("12345")]      // too short
  [InlineData("123456789")]  // too long
  [InlineData("12 3456")]    // whitespace
  [InlineData("-123456")]    // signed
  [InlineData("12.3456")]    // not integer
  [InlineData("")]
  public void NumericValidator_RejectsInvalidShapes(string raw)
  {
    var d = HoobiBitwardenCommandPaletteExtensionPage.DescribeTwoFactorMethod(0);
    Assert.False(d.Validator(raw));
  }

  [Theory]
  [InlineData("short")]                                                       // too short
  [InlineData("with space inside the otp xxxxxxxxxxxxxxxxxxx")]               // non-alphanumeric
  [InlineData("")]
  public void YubikeyValidator_RejectsInvalidShapes(string raw)
  {
    var d = HoobiBitwardenCommandPaletteExtensionPage.DescribeTwoFactorMethod(3);
    Assert.False(d.Validator(raw));
  }
}
