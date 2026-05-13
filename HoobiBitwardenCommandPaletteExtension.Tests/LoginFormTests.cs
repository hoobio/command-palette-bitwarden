using System.Text.Json.Nodes;
using HoobiBitwardenCommandPaletteExtension.Pages;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class LoginFormTests
{
  [Fact]
  public void BuildCustomDataDirWarningBlock_EscapesPathAndParsesAsJson()
  {
    var block = LoginForm.BuildCustomDataDirWarningBlock(@"C:\Users\test\bw data");
    Assert.EndsWith(",", block.TrimEnd(), StringComparison.Ordinal);

    var jsonObject = block.TrimEnd().TrimEnd(',');
    var parsed = JsonNode.Parse(jsonObject)?.AsObject();
    Assert.NotNull(parsed);
    Assert.Equal("TextBlock", parsed!["type"]!.GetValue<string>());
    Assert.Equal("attention", parsed["color"]!.GetValue<string>());
    Assert.Contains(@"C:\Users\test\bw data", parsed["text"]!.GetValue<string>(), StringComparison.Ordinal);
    Assert.Contains("BITWARDENCLI_APPDATA_DIR", parsed["text"]!.GetValue<string>(), StringComparison.Ordinal);
  }


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
