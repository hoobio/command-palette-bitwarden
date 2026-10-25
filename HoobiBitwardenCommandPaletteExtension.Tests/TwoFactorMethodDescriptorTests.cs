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
    Assert.True(d.Validator("cccccccbcdefghijklnrtuv1234567890abcdefghijk"));
    Assert.False(d.Validator("123456"));
  }

  [Theory]
  [InlineData(null)]
  [InlineData(2)]
  [InlineData(99)]
  public void Describe_UnknownOrNull_FallsBackToGenericNumeric(int? method)
  {
    var d = HoobiBitwardenCommandPaletteExtensionPage.DescribeTwoFactorMethod(method);
    Assert.Equal("2FA code", d.TypedNoun);
    Assert.True(d.Validator("123456"));
    Assert.False(d.Validator("ab"));
  }

  [Theory]
  [InlineData("12345")]
  [InlineData("123456789")]
  [InlineData("12 3456")]
  [InlineData("-123456")]
  [InlineData("12.3456")]
  [InlineData("")]
  public void NumericValidator_RejectsInvalidShapes(string raw)
  {
    var d = HoobiBitwardenCommandPaletteExtensionPage.DescribeTwoFactorMethod(0);
    Assert.False(d.Validator(raw));
  }

  [Theory]
  [InlineData("short")]
  [InlineData("with space inside the otp xxxxxxxxxxxxxxxxxxx")]
  [InlineData("")]
  public void YubikeyValidator_RejectsInvalidShapes(string raw)
  {
    var d = HoobiBitwardenCommandPaletteExtensionPage.DescribeTwoFactorMethod(3);
    Assert.False(d.Validator(raw));
  }

  [Theory]
  [InlineData("123456", true)]
  [InlineData("12345678", true)]
  [InlineData("12345", false)]
  [InlineData("123456789", false)]
  [InlineData("12a456", false)]
  public void IsNumericCode_AcceptsSixToEightDigits(string raw, bool expected) =>
    Assert.Equal(expected, HoobiBitwarden.Ipc.TwoFactorRules.IsNumericCode(raw));
}
