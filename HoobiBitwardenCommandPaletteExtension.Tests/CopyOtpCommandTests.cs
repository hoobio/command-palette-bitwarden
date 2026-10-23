using HoobiBitwardenCommandPaletteExtension.Commands;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class CopyOtpCommandTests
{
  [Fact]
  public void ParseTotpSecret_RawBase32_ReturnsDefaults()
  {
    var (key, digits, period, _) = CopyOtpCommand.ParseTotpSecret("JBSWY3DPEHPK3PXP");
    Assert.NotEmpty(key);
    Assert.Equal(6, digits);
    Assert.Equal(30, period);
  }

  [Fact]
  public void ParseTotpSecret_Base32WithSpaces_Strips()
  {
    var (key1, _, _, _) = CopyOtpCommand.ParseTotpSecret("JBSWY3DPEHPK3PXP");
    var (key2, _, _, _) = CopyOtpCommand.ParseTotpSecret("JBSW Y3DP EHPK 3PXP");
    Assert.Equal(key1, key2);
  }

  [Fact]
  public void ParseTotpSecret_Base32WithDashes_Strips()
  {
    var (key1, _, _, _) = CopyOtpCommand.ParseTotpSecret("JBSWY3DPEHPK3PXP");
    var (key2, _, _, _) = CopyOtpCommand.ParseTotpSecret("JBSW-Y3DP-EHPK-3PXP");
    Assert.Equal(key1, key2);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_ExtractsParameters()
  {
    var uri = "otpauth://totp/Test:user@example.com?secret=JBSWY3DPEHPK3PXP&digits=8&period=60";
    var (key, digits, period, _) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.NotEmpty(key);
    Assert.Equal(8, digits);
    Assert.Equal(60, period);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_DefaultsWhenMissing()
  {
    var uri = "otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP";
    var (key, digits, period, _) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.NotEmpty(key);
    Assert.Equal(6, digits);
    Assert.Equal(30, period);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_CaseInsensitive()
  {
    var uri = "OTPAUTH://totp/Test?SECRET=JBSWY3DPEHPK3PXP&DIGITS=7&PERIOD=45";
    var (_, digits, period, _) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.Equal(7, digits);
    Assert.Equal(45, period);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_UrlEncodedSecret()
  {
    var uri = "otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP&issuer=My%20App";
    var (key, digits, period, _) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.NotEmpty(key);
    Assert.Equal(6, digits);
    Assert.Equal(30, period);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_InvalidDigits_FallsBackToDefault()
  {
    var uri = "otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP&digits=abc";
    var (_, digits, _, _) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.Equal(6, digits);
  }

  [Fact]
  public void ParseTotpSecret_OtpAuthUri_ZeroPeriod_FallsBackToDefault()
  {
    var uri = "otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP&period=0";
    var (_, _, period, _) = CopyOtpCommand.ParseTotpSecret(uri);
    Assert.Equal(30, period);
  }

  [Theory]
  [InlineData("SHA1", "12345678901234567890", "94287082")]
  [InlineData("sha256", "12345678901234567890123456789012", "46119246")]
  [InlineData("Sha512", "1234567890123456789012345678901234567890123456789012345678901234", "90693936")]
  public void ComputeCode_OtpAuthAlgorithm_MatchesRfc6238Vectors(string algorithm, string seed, string expected)
  {
    var secret = OtpNet.Base32Encoding.ToString(System.Text.Encoding.ASCII.GetBytes(seed)).TrimEnd('=');
    var uri = $"otpauth://totp/Test?secret={secret}&algorithm={algorithm}&digits=8&period=30";
    var (code, remaining) = CopyOtpCommand.ComputeCode(uri, DateTimeOffset.FromUnixTimeSeconds(59));
    Assert.Equal(expected, code);
    Assert.Equal(1, remaining);
  }

  [Fact]
  public void ParseTotpSecret_UnknownAlgorithm_DefaultsToSha1()
  {
    var (_, _, _, mode) = CopyOtpCommand.ParseTotpSecret("otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP&algorithm=MD5");
    Assert.Equal(OtpNet.OtpHashMode.Sha1, mode);
  }

  [Fact]
  public void ComputeCode_OtpAuthPeriod_IsHonoured()
  {
    var (_, remaining) = CopyOtpCommand.ComputeCode("otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP&period=60", DateTimeOffset.FromUnixTimeSeconds(59));
    Assert.Equal(1, remaining);
    var (_, remaining2) = CopyOtpCommand.ComputeCode("otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP&period=60", DateTimeOffset.FromUnixTimeSeconds(61));
    Assert.Equal(59, remaining2);
  }

  [Fact]
  public void IsSteamSecret_DetectsSteamPrefix()
  {
    Assert.True(CopyOtpCommand.IsSteamSecret("steam://JBSWY3DPEHPK3PXP"));
    Assert.True(CopyOtpCommand.IsSteamSecret("STEAM://JBSWY3DPEHPK3PXP"));
    Assert.False(CopyOtpCommand.IsSteamSecret("JBSWY3DPEHPK3PXP"));
    Assert.False(CopyOtpCommand.IsSteamSecret("otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP"));
  }

  [Fact]
  public void ComputeSteamCode_ProducesFiveCharacterCode()
  {
    var key = OtpNet.Base32Encoding.ToBytes("JBSWY3DPEHPK3PXP");
    var code = CopyOtpCommand.ComputeSteamCode(key, 1000000000);
    Assert.Equal(5, code.Length);
    Assert.All(code.ToCharArray(), c => Assert.Contains(c, "23456789BCDFGHJKMNPQRTVWXY"));
  }

  [Fact]
  public void ComputeSteamCode_IsDeterministic()
  {
    var key = OtpNet.Base32Encoding.ToBytes("JBSWY3DPEHPK3PXP");
    var code1 = CopyOtpCommand.ComputeSteamCode(key, 1000000000);
    var code2 = CopyOtpCommand.ComputeSteamCode(key, 1000000000);
    Assert.Equal(code1, code2);
  }

  [Fact]
  public void ComputeSteamCode_DifferentTimeSteps_ProduceDifferentCodes()
  {
    var key = OtpNet.Base32Encoding.ToBytes("JBSWY3DPEHPK3PXP");
    var code1 = CopyOtpCommand.ComputeSteamCode(key, 1000000000);
    var code2 = CopyOtpCommand.ComputeSteamCode(key, 1000000030);
    Assert.NotEqual(code1, code2);
  }

  [Fact]
  public void ComputeSteamCode_SameTimeStep_ProducesSameCode()
  {
    var key = OtpNet.Base32Encoding.ToBytes("JBSWY3DPEHPK3PXP");
    var code1 = CopyOtpCommand.ComputeSteamCode(key, 999999990);
    var code2 = CopyOtpCommand.ComputeSteamCode(key, 1000000009);
    Assert.Equal(code1, code2);
  }

  [Fact]
  public void ComputeCode_SteamSecret_ReturnsValidCode()
  {
    var (code, remaining) = CopyOtpCommand.ComputeCode("steam://JBSWY3DPEHPK3PXP");
    Assert.Equal(5, code.Length);
    Assert.All(code.ToCharArray(), c => Assert.Contains(c, "23456789BCDFGHJKMNPQRTVWXY"));
    Assert.InRange(remaining, 1, 30);
  }

  [Fact]
  public void ComputeCode_StandardSecret_ReturnsSixDigitCode()
  {
    var (code, remaining) = CopyOtpCommand.ComputeCode("JBSWY3DPEHPK3PXP");
    Assert.Equal(6, code.Length);
    Assert.All(code.ToCharArray(), c => Assert.True(char.IsDigit(c)));
    Assert.InRange(remaining, 1, 30);
  }
}
