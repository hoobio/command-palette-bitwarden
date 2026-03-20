using System.Text;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class FaviconServiceTests
{
  [Theory]
  [InlineData("image/svg+xml", true)]
  [InlineData("IMAGE/SVG+XML", true)]
  [InlineData("image/svg", true)]
  [InlineData("image/png", false)]
  [InlineData("image/jpeg", false)]
  [InlineData("", false)]
  public void IsSvg_DetectsContentType(string contentType, bool expected)
  {
    var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00 };
    Assert.Equal(expected, FaviconService.IsSvg(contentType, pngBytes));
  }

  [Fact]
  public void IsSvg_DetectsSvgBytesWhenContentTypeWrong()
  {
    var svgBytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"><rect/></svg>");
    Assert.True(FaviconService.IsSvg("application/octet-stream", svgBytes));
  }

  [Fact]
  public void IsSvg_DetectsSvgTagWithoutXmlDeclaration()
  {
    var svgBytes = Encoding.UTF8.GetBytes("<svg viewBox=\"0 0 100 100\"><circle r=\"50\"/></svg>");
    Assert.True(FaviconService.IsSvg("", svgBytes));
  }

  [Fact]
  public void IsSvg_FalseForRasterBytes()
  {
    var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };
    Assert.False(FaviconService.IsSvg("image/png", pngBytes));
  }
}
