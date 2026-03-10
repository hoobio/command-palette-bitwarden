using HoobiBitwardenCommandPaletteExtension.Commands;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class CopyOtpCommandTests
{
  [Fact]
  public void ParseTotpSecret_RawBase32_ReturnsDefaults()
  {
    var (key, digits, period) = CopyOtpCommand.ParseTotpSecret("JBSWY3DPEHPK3PXP");
    Assert.NotEmpty(key);
    Assert.Equal(6, digits);
    Assert.Equal(30, period);
  }

  [Fact]
  public void ParseTotpSecret_Base32WithSpaces_Strips()
  {
    var (key1, _, _) = CopyOtpCommand.ParseTotpSecret("JBSWY3DPEHPK3PXP");
    var (key2, _, _) = CopyOtpCommand.ParseTotpSecret("JBSW Y3DP EHPK 3PXP");
    Assert.Equal(key1, key2);
  }

  [Fact]
  public void ParseTotpSecret_Base32WithDashes_Strips()
  {
    var (key1, _, _) = CopyOtpCommand.ParseTotpSecret("JBSWY3DPEHPK3PXP");
    var (key2, _, _) = CopyOtpCommand.ParseTotpSecret("JBSW-Y3DP-EHPK-3PXP");
    Assert.Equal(key1, key2);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_ExtractsParameters()
  {
    var uri = "otpauth://totp/Test:user@example.com?secret=JBSWY3DPEHPK3PXP&digits=8&period=60";
    var (key, digits, period) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.NotEmpty(key);
    Assert.Equal(8, digits);
    Assert.Equal(60, period);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_DefaultsWhenMissing()
  {
    var uri = "otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP";
    var (key, digits, period) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.NotEmpty(key);
    Assert.Equal(6, digits);
    Assert.Equal(30, period);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_CaseInsensitive()
  {
    var uri = "OTPAUTH://totp/Test?SECRET=JBSWY3DPEHPK3PXP&DIGITS=7&PERIOD=45";
    var (_, digits, period) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.Equal(7, digits);
    Assert.Equal(45, period);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_UrlEncodedSecret()
  {
    var uri = "otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP&issuer=My%20App";
    var (key, digits, period) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.NotEmpty(key);
    Assert.Equal(6, digits);
    Assert.Equal(30, period);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_InvalidDigits_FallsBackToDefault()
  {
    var uri = "otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP&digits=abc";
    var (_, digits, _) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.Equal(6, digits);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_ZeroPeriod_FallsBackToDefault()
  {
    var uri = "otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP&period=0";
    var (_, _, period) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.Equal(30, period);
  }
}
