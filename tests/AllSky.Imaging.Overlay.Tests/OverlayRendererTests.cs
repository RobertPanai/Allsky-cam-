using AllSky.Imaging.Overlay;
using Xunit;

namespace AllSky.Imaging.Overlay.Tests;

public class OverlayRendererTests
{
    private const byte Background = 128;
    private static readonly (byte R, byte G, byte B) White = (255, 255, 255);

    [Fact]
    public void Text_WithNegativeCoordinates_IsClippedAndNeverWritesOutside()
    {
        const int w = 10, h = 10, size = w * h * 3;
        byte[] backing = CanaryBuffer.Allocate(size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, size);
        rgb.Fill(Background);

        // Con origine (-3, -2) l'asta della I (colonna 3, riga 3 del glifo) cade in (0, 1).
        OverlayRenderer.DrawText(rgb, w, h, "I", -3, -2, White, 1, false);

        CanaryBuffer.AssertIntact(backing, size);
        PixelAssert.Pixel(rgb, w, 0, 1, White);
    }

    [Fact]
    public void Text_BeyondRightEdge_IsClipped()
    {
        const int w = 10, h = 10, size = w * h * 3;
        byte[] backing = CanaryBuffer.Allocate(size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, size);
        rgb.Fill(Background);

        // Con origine (7, 0) la barra superiore della I va da x = 8 a 13: a destra del bordo (x = 9) si ferma.
        OverlayRenderer.DrawText(rgb, w, h, "I", 7, 0, White, 1, false);

        CanaryBuffer.AssertIntact(backing, size);
        PixelAssert.Pixel(rgb, w, 8, 2, White);
        PixelAssert.Pixel(rgb, w, 9, 2, White);
        PixelAssert.Pixel(rgb, w, 7, 2, (Background, Background, Background));
    }

    [Fact]
    public void Text_FullyOutsideBuffer_WritesNothing()
    {
        const int w = 10, h = 10, size = w * h * 3;
        byte[] backing = CanaryBuffer.Allocate(size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, size);
        rgb.Fill(Background);

        OverlayRenderer.DrawText(rgb, w, h, "Ciao", 100, 100, White, 4, true);
        OverlayRenderer.DrawText(rgb, w, h, "Ciao", -500, -500, White, 4, true);

        CanaryBuffer.AssertIntact(backing, size);
        Assert.All(rgb.ToArray(), b => Assert.Equal(Background, b));
    }

    [Fact]
    public void Shadow_IsBlackAndOnePixelDownRight()
    {
        const int w = 12, h = 12, size = w * h * 3;
        byte[] backing = CanaryBuffer.Allocate(size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, size);
        rgb.Fill(Background);

        OverlayRenderer.DrawText(rgb, w, h, "I", 0, 0, White, 1, true);

        CanaryBuffer.AssertIntact(backing, size);
        PixelAssert.Pixel(rgb, w, 3, 3, White);
        PixelAssert.Pixel(rgb, w, 4, 4, (0, 0, 0));
    }

    [Fact]
    public void WithoutShadow_TheShadowPixelKeepsTheBackground()
    {
        const int w = 12, h = 12, size = w * h * 3;
        byte[] backing = CanaryBuffer.Allocate(size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, size);
        rgb.Fill(Background);

        OverlayRenderer.DrawText(rgb, w, h, "I", 0, 0, White, 1, false);

        PixelAssert.Pixel(rgb, w, 4, 4, (Background, Background, Background));
    }

    [Fact]
    public void Scale_FourDrawsFourByFourBlocks()
    {
        const int w = 40, h = 40, size = w * h * 3;
        byte[] backing = CanaryBuffer.Allocate(size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, size);
        rgb.Fill(Background);

        OverlayRenderer.DrawText(rgb, w, h, "I", 0, 0, White, 4, false);

        CanaryBuffer.AssertIntact(backing, size);
        PixelAssert.Pixel(rgb, w, 13, 13, White);
        PixelAssert.Pixel(rgb, w, 11, 13, (Background, Background, Background));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Scale_OutsideOneToFour_Throws(int scale)
    {
        byte[] rgb = new byte[10 * 10 * 3];
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayRenderer.DrawText(rgb, 10, 10, "I", 0, 0, White, scale, false));
    }

    [Fact]
    public void Anchored_BottomRight_PlacesInkInsideTheCornerMargin()
    {
        // Ingombro del testo con ombra: 8 + 1 = 9 px di larghezza, 16 + 1 = 17 px di altezza.
        // Origine: x = 20 - 2 - 9 = 9, y = 20 - 2 - 17 = 1. La I colorata occupa x 10..15 e y 3..12.
        const int w = 20, h = 20, size = w * h * 3;
        byte[] backing = CanaryBuffer.Allocate(size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, size);
        rgb.Fill(Background);

        OverlayRenderer.DrawTextAnchored(rgb, w, h, "I", OverlayAnchor.BottomRight, 2, White, 1, true);

        CanaryBuffer.AssertIntact(backing, size);
        Assert.Equal((10, 3, 15, 12), PixelAssert.Bounds(rgb, w, h, White));
    }

    [Fact]
    public void Anchored_TopRight_WithoutShadow()
    {
        // Larghezza 8 px: x = 20 - 2 - 8 = 10, y = 2. La I colorata occupa x 11..16 e y 4..13.
        const int w = 20, h = 20, size = w * h * 3;
        byte[] backing = CanaryBuffer.Allocate(size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, size);
        rgb.Fill(Background);

        OverlayRenderer.DrawTextAnchored(rgb, w, h, "I", OverlayAnchor.TopRight, 2, White, 1, false);

        CanaryBuffer.AssertIntact(backing, size);
        Assert.Equal((11, 4, 16, 13), PixelAssert.Bounds(rgb, w, h, White));
    }

    [Fact]
    public void MeasureText_IncludesShadowOnlyWhenAsked()
    {
        Assert.Equal((49, 33), OverlayRenderer.MeasureText("abc", 2, true));
        Assert.Equal((48, 32), OverlayRenderer.MeasureText("abc", 2, false));
    }
}
