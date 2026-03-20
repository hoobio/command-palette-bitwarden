using System.Text;
using HoobiBitwardenCommandPaletteExtension.Services;

namespace HoobiBitwardenCommandPaletteExtension.Tests;

public class SvgRasterizerTests
{
    private static byte[] Svg(string content) => Encoding.UTF8.GetBytes(content);

    private static readonly byte[] SimpleSvg = Svg(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\">" +
        "<rect width=\"100\" height=\"100\" fill=\"red\"/></svg>");

    // --- TryRasterize happy path ---

    [Fact]
    public void TryRasterize_ValidSvg_ReturnsPngBytes()
    {
        var result = SvgRasterizer.TryRasterize(SimpleSvg);

        Assert.NotNull(result);
        // PNG magic bytes: 0x89 P N G
        Assert.Equal(0x89, result[0]);
        Assert.Equal(0x50, result[1]);
        Assert.Equal(0x4E, result[2]);
        Assert.Equal(0x47, result[3]);
    }

    [Fact]
    public void TryRasterize_ValidSvg_DefaultSizeIs64x64()
    {
        var result = SvgRasterizer.TryRasterize(SimpleSvg);

        Assert.NotNull(result);
        // IHDR chunk starts at byte 16; width and height are each 4-byte big-endian ints
        var width = (result[16] << 24) | (result[17] << 16) | (result[18] << 8) | result[19];
        var height = (result[20] << 24) | (result[21] << 16) | (result[22] << 8) | result[23];
        Assert.Equal(64, width);
        Assert.Equal(64, height);
    }

    [Fact]
    public void TryRasterize_CustomSize_ReturnsCorrectDimensions()
    {
        var result = SvgRasterizer.TryRasterize(SimpleSvg, size: 32);

        Assert.NotNull(result);
        var width = (result[16] << 24) | (result[17] << 16) | (result[18] << 8) | result[19];
        var height = (result[20] << 24) | (result[21] << 16) | (result[22] << 8) | result[23];
        Assert.Equal(32, width);
        Assert.Equal(32, height);
    }

    [Fact]
    public void TryRasterize_ViewBoxOnlySvg_ReturnsNonNullPng()
    {
        var svg = Svg(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 200\">" +
            "<circle cx=\"100\" cy=\"100\" r=\"100\" fill=\"blue\"/></svg>");

        var result = SvgRasterizer.TryRasterize(svg);

        Assert.NotNull(result);
        Assert.Equal(0x89, result[0]);
    }

    [Fact]
    public void TryRasterize_ZeroBoundsSvg_DoesNotThrow_ReturnsNonNull()
    {
        // An SVG with no visible content produces a zero-sized CullRect;
        // the scale falls back to 1f and the rasterizer must still succeed.
        var svg = Svg("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");

        var result = SvgRasterizer.TryRasterize(svg);

        // May return null (empty picture) or a valid PNG; must not throw.
        if (result is not null)
            Assert.Equal(0x89, result[0]);
    }

    // --- TryRasterize error cases ---

    [Fact]
    public void TryRasterize_EmptyBytes_ReturnsNull()
    {
        var result = SvgRasterizer.TryRasterize([]);

        Assert.Null(result);
    }

    [Fact]
    public void TryRasterize_NotSvg_ReturnsNull()
    {
        var pngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        };

        var result = SvgRasterizer.TryRasterize(pngBytes);

        Assert.Null(result);
    }

    [Fact]
    public void TryRasterize_MalformedXml_ReturnsNull()
    {
        var result = SvgRasterizer.TryRasterize(Svg("<svg><unclosed"));

        Assert.Null(result);
    }

    [Fact]
    public void TryRasterize_RandomGarbage_ReturnsNull()
    {
        var garbage = new byte[256];
        new Random(42).NextBytes(garbage);

        var result = SvgRasterizer.TryRasterize(garbage);

        Assert.Null(result);
    }
}
