using System;
using System.IO;
using SkiaSharp;
using Svg.Skia;

namespace HoobiBitwardenCommandPaletteExtension.Services;

internal static class SvgRasterizer
{
    internal static byte[]? TryRasterize(byte[] svgBytes, int size = 64)
    {
        try
        {
            using var svg = new SKSvg();
            using var stream = new MemoryStream(svgBytes);
            if (svg.Load(stream) is null)
                return null;

            var picture = svg.Picture;
            if (picture is null)
                return null;

            var bounds = picture.CullRect;
            float scale = bounds is { Width: > 0, Height: > 0 }
                ? Math.Min(size / bounds.Width, size / bounds.Height)
                : 1f;

            var imageInfo = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.DrawPicture(picture);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
