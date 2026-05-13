using HoobiBitwardenCommandPaletteExtension.Pages;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class LoginFormTests
{
  [Theory]
  [InlineData("0", 0)]
  [InlineData("1", 1)]
  [InlineData("3", 3)]
  public void ParseTwoFactorMethod_NumericValue_ReturnsInt(string raw, int expected)
  {
    Assert.Equal(expected, LoginForm.ParseTwoFactorMethod(raw));
  }

  [Theory]
  [InlineData("none")]
  [InlineData("NONE")]
  [InlineData("")]
  [InlineData(null)]
  public void ParseTwoFactorMethod_NoneOrEmpty_ReturnsNull(string? raw)
  {
    Assert.Null(LoginForm.ParseTwoFactorMethod(raw));
  }

  [Fact]
  public void ParseTwoFactorMethod_Garbage_FallsBackToTotp()
  {
    Assert.Equal(0, LoginForm.ParseTwoFactorMethod("not-a-number"));
  }
}
