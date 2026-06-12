using SkiaSharp;
using Sweeprr.API.Services;

namespace Sweeprr.Tests.Services;

public class OverlayRenderingServiceTests
{
    private const int Width = 600;
    private const int Height = 900;
    private const string ExtendUrl = "https://sweeprr.example.com/extend?itemId=abc123";

    private static byte[] CreateTestImage(int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    [Fact]
    public void RenderOverlay_WithExtendUrl_DrawsQrCode()
    {
        var original = CreateTestImage(Width, Height, SKColors.SteelBlue);

        var withQr = OverlayRenderingService.RenderOverlay(original, "Leaving Soon", ExtendUrl);
        var withoutQr = OverlayRenderingService.RenderOverlay(original, "Leaving Soon", null);

        Assert.NotNull(withQr);
        Assert.NotNull(withoutQr);
        Assert.NotEqual(withoutQr, withQr);

        using var decoded = SKBitmap.Decode(withQr);
        Assert.Equal(Width, decoded.Width);
        Assert.Equal(Height, decoded.Height);
    }

    [Fact]
    public void RenderOverlay_WithoutExtendUrl_RendersBannerOnly()
    {
        var original = CreateTestImage(Width, Height, SKColors.SteelBlue);

        var result = OverlayRenderingService.RenderOverlay(original, "Leaving Soon", null);

        Assert.NotNull(result);
        using var decoded = SKBitmap.Decode(result);
        Assert.Equal(Width, decoded.Width);
        Assert.Equal(Height, decoded.Height);

        // The right edge of the banner (where a QR code would otherwise be drawn) must
        // remain part of the red gradient banner, not a white QR backing.
        var pixel = decoded.GetPixel(Width - 10, (int)(Height * 0.9f));
        Assert.False(pixel.Red > 200 && pixel.Green > 200 && pixel.Blue > 200);
    }

    [Fact]
    public void RenderOverlay_CorruptInput_ReturnsNull()
    {
        var corrupt = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        Assert.Null(OverlayRenderingService.RenderOverlay(corrupt, "Leaving Soon", null));
        Assert.Null(OverlayRenderingService.RenderOverlay(corrupt, "Leaving Soon", ExtendUrl));
    }

    [Fact]
    public void RenderOverlay_TinyImage_SkipsQrWithoutException()
    {
        // 80x120: qrSize would be ~12.8px, well under the 40px floor — QR must be skipped.
        var original = CreateTestImage(80, 120, SKColors.SteelBlue);

        var result = OverlayRenderingService.RenderOverlay(original, "Soon", ExtendUrl);

        Assert.NotNull(result);
        using var decoded = SKBitmap.Decode(result);
        Assert.Equal(80, decoded.Width);
        Assert.Equal(120, decoded.Height);
    }

    [Fact]
    public void TryGenerateQrBitmap_ValidUrl_ReturnsBitmap()
    {
        using var bitmap = OverlayRenderingService.TryGenerateQrBitmap(ExtendUrl);

        Assert.NotNull(bitmap);
        Assert.True(bitmap!.Width > 0);
        Assert.True(bitmap.Height > 0);
    }
}
