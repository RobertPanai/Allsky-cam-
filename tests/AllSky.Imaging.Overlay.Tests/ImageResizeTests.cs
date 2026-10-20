using AllSky.Imaging.Overlay;
using Xunit;

namespace AllSky.Imaging.Overlay.Tests;

public class ImageResizeTests
{
    /// <summary>Costruisce un'immagine RGB dove R = G = B = valore, un pixel per valore.</summary>
    private static byte[] Gray(params byte[] values)
    {
        var rgb = new byte[values.Length * 3];
        for (int i = 0; i < values.Length; i++)
        {
            rgb[i * 3] = values[i];
            rgb[i * 3 + 1] = values[i];
            rgb[i * 3 + 2] = values[i];
        }

        return rgb;
    }

    [Fact]
    public void Box_TwoByTwoToOne_AveragesAndRounds()
    {
        // (0 + 100 + 200 + 255) / 4 = 138.75, arrotondato a 139.
        byte[] dst = new byte[3];
        ImageResize.Box(Gray(0, 100, 200, 255), 2, 2, dst, 1, 1);
        Assert.Equal(new byte[] { 139, 139, 139 }, dst);
    }

    [Fact]
    public void Box_NonIntegerRatio_ThreeToTwo_SplitsTheMiddlePixel()
    {
        // 3 pixel su 2: il pixel centrale (255) si divide a meta' tra le due uscite,
        // quindi ognuna vale 255 * 0.5 / 1.5 = 85.
        byte[] dst = new byte[6];
        ImageResize.Box(Gray(0, 255, 0), 3, 1, dst, 2, 1);
        Assert.Equal(new byte[] { 85, 85, 85, 85, 85, 85 }, dst);
    }

    [Fact]
    public void Box_UniformImage_StaysUniform()
    {
        byte[] src = Enumerable.Repeat((byte)77, 7 * 5 * 3).ToArray();
        byte[] dst = new byte[3 * 2 * 3];
        ImageResize.Box(src, 7, 5, dst, 3, 2);
        Assert.All(dst, b => Assert.Equal((byte)77, b));
    }

    [Fact]
    public void Box_SameSize_CopiesPixels()
    {
        byte[] src = Enumerable.Range(0, 3 * 2 * 3).Select(i => (byte)(i * 7)).ToArray();
        byte[] dst = new byte[src.Length];
        ImageResize.Box(src, 3, 2, dst, 3, 2);
        Assert.Equal(src, dst);
    }

    [Fact]
    public void Bilinear_TwoToFour_InterpolatesWithCenteredSamples()
    {
        // Coordinate sorgente dei centri: -0.25 (clamp a 0), 0.25, 0.75, 1.25 (clamp a 1).
        // Valori attesi: 0, 63.75 -> 64, 191.25 -> 191, 255.
        byte[] dst = new byte[4 * 3];
        ImageResize.Bilinear(Gray(0, 255), 2, 1, dst, 4, 1);
        Assert.Equal(new byte[] { 0, 0, 0, 64, 64, 64, 191, 191, 191, 255, 255, 255 }, dst);
    }

    [Fact]
    public void Bilinear_ConstantImage_StaysConstant()
    {
        byte[] src = Enumerable.Repeat((byte)200, 3 * 3 * 3).ToArray();
        byte[] dst = new byte[7 * 5 * 3];
        ImageResize.Bilinear(src, 3, 3, dst, 7, 5);
        Assert.All(dst, b => Assert.Equal((byte)200, b));
    }

    [Fact]
    public void Resize_WritesOnlyTheDestinationArea()
    {
        // Il buffer di destinazione e' piu' grande del necessario: il resto non deve cambiare.
        const int needed = 3;
        byte[] backing = CanaryBuffer.Allocate(needed + 9);
        Span<byte> dst = CanaryBuffer.Payload(backing, needed + 9);
        dst.Fill(0x55);

        ImageResize.Box(Gray(10, 20, 30, 40), 2, 2, dst, 1, 1);

        Assert.Equal((byte)25, dst[0]);
        for (int i = needed; i < dst.Length; i++)
        {
            Assert.Equal((byte)0x55, dst[i]);
        }

        CanaryBuffer.AssertIntact(backing, needed + 9);
    }

    [Fact]
    public void InvalidSizesOrShortBuffers_Throw()
    {
        byte[] src = new byte[12];
        byte[] dst = new byte[12];

        Assert.Throws<ArgumentOutOfRangeException>(() => ImageResize.Box(src, 0, 2, dst, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageResize.Bilinear(src, 2, 2, dst, 0, 1));
        Assert.Throws<ArgumentException>(() => ImageResize.Box(src, 3, 2, dst, 1, 1));
        Assert.Throws<ArgumentException>(() => ImageResize.Bilinear(src, 2, 2, new byte[2], 1, 1));
    }
}
