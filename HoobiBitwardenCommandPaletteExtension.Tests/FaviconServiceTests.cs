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

  [Fact]
  public void ClearMemCache_ResetsState()
  {
    FaviconService.ClearMemCache();
  }

  [Fact]
  public void GetOrQueue_ReturnsOneAbsolutePathIcon_ForTheSameHost()
  {
    var host = $"shared-{Guid.NewGuid():N}.example";
    Directory.CreateDirectory(FaviconService.CacheDir);
    var path = Path.Combine(FaviconService.CacheDir, host + ".png");
    File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

    var first = FaviconService.GetOrQueue(host, "https://icons.invalid/icon.png");
    var second = FaviconService.GetOrQueue(host, "https://icons.invalid/icon.png");

    Assert.Same(first, second);
    Assert.True(Path.IsPathFullyQualified(first.Light.Icon ?? string.Empty));
    Assert.Equal(path, first.Light.Icon);
  }

  [Fact]
  public void GetOrQueue_ReturnsTheFallback_WhileTheHostIsNegativelyCached()
  {
    var host = $"missing-{Guid.NewGuid():N}.example";
    Directory.CreateDirectory(FaviconService.CacheDir);
    File.WriteAllText(Path.Combine(FaviconService.CacheDir, host + ".miss"), "404");
    var fallback = new Microsoft.CommandPalette.Extensions.Toolkit.IconInfo("");

    Assert.Same(fallback, FaviconService.GetOrQueue(host, "https://icons.invalid/icon.png", fallback));
    Assert.Same(fallback, FaviconService.GetOrQueue(host, "https://icons.invalid/icon.png", fallback));
  }
}
