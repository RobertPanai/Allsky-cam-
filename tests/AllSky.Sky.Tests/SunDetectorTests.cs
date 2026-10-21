using AllSky.Sky.Calibration;
using Xunit;

namespace AllSky.Sky.Tests;

// Frame sintetici in luminanza 8 bit. I dischi usano centri non interi per verificare la precisione sub-pixel del centroide.
public class SunDetectorTests
{
    private const byte Background = 30;
    private const double CentroidTolerancePx = 0.3;

    private static byte[] Frame(int width, int height, byte value = Background)
    {
        var luma = new byte[width * height];
        Array.Fill(luma, value);
        return luma;
    }

    private static void FillDisk(byte[] luma, int width, double cx, double cy, double radius, byte value)
    {
        int height = luma.Length / width;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                if (dx * dx + dy * dy <= radius * radius)
                {
                    luma[y * width + x] = value;
                }
            }
        }
    }

    private static void FillRect(byte[] luma, int width, int x0, int y0, int w, int h, byte value)
    {
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = x0; x < x0 + w; x++)
            {
                luma[y * width + x] = value;
            }
        }
    }

    [Fact]
    public void SaturatedDiskWithHalo_CentroidWithinSubPixelTolerance()
    {
        const int width = 120, height = 90;
        const double cx = 50.4, cy = 40.7;
        byte[] luma = Frame(width, height);
        // Alone luminoso attorno al disco saturo: sotto la soglia, quindi non entra nella componente.
        FillDisk(luma, width, cx, cy, 16.0, 200);
        FillDisk(luma, width, cx, cy, 10.0, 255);

        Assert.True(SunDetector.TryFindSun(luma, width, height, out SunDetection d));

        Assert.Equal(cx, d.X, CentroidTolerancePx);
        Assert.Equal(cy, d.Y, CentroidTolerancePx);
        Assert.Equal(255.0, d.PeakLuma, 1e-9);
    }

    [Fact]
    public void TwoCandidates_ChoosesLargestCompactOne()
    {
        const int width = 160, height = 100;
        byte[] luma = Frame(width, height);
        FillDisk(luma, width, 30.0, 30.0, 6.0, 255);
        FillDisk(luma, width, 90.2, 60.6, 12.0, 255);

        Assert.True(SunDetector.TryFindSun(luma, width, height, out SunDetection d));

        Assert.Equal(90.2, d.X, CentroidTolerancePx);
        Assert.Equal(60.6, d.Y, CentroidTolerancePx);
        Assert.True(d.AreaPx > 400.0, $"Area inattesa {d.AreaPx}.");
    }

    [Fact]
    public void BurntSky_MoreThanFivePercentSaturated_ReturnsFalse()
    {
        // Disco di raggio 16 su 100x100: circa 804 px saturi, cioe' circa l'8% del frame.
        const int width = 100, height = 100;
        byte[] luma = Frame(width, height);
        FillDisk(luma, width, 50.0, 50.0, 16.0, 255);

        Assert.False(SunDetector.TryFindSun(luma, width, height, out _));
    }

    [Fact]
    public void BrightBar_IsIgnored()
    {
        // Barra 60x4: area/bbox pari a 1, ma aspetto 4/60, quindi non e' compatta.
        const int width = 200, height = 120;
        byte[] luma = Frame(width, height);
        FillRect(luma, width, 20, 50, 60, 4, 255);

        Assert.False(SunDetector.TryFindSun(luma, width, height, out _));
    }

    [Fact]
    public void BrightBar_LargerThanDisk_DiskIsChosen()
    {
        const int width = 200, height = 120;
        byte[] luma = Frame(width, height);
        FillRect(luma, width, 10, 10, 90, 4, 255);
        FillDisk(luma, width, 150.0, 80.0, 7.0, 255);

        Assert.True(SunDetector.TryFindSun(luma, width, height, out SunDetection d));

        Assert.Equal(150.0, d.X, CentroidTolerancePx);
        Assert.Equal(80.0, d.Y, CentroidTolerancePx);
    }

    [Fact]
    public void ComponentBelowMinimumArea_IsIgnored()
    {
        // Blocco 4x4 = 16 px, sotto l'area minima di 20 px.
        const int width = 60, height = 60;
        byte[] luma = Frame(width, height);
        FillRect(luma, width, 20, 20, 4, 4, 255);

        Assert.False(SunDetector.TryFindSun(luma, width, height, out _));
    }

    [Fact]
    public void NoPixelAboveThreshold_ReturnsFalse()
    {
        const int width = 60, height = 60;
        byte[] luma = Frame(width, height, value: 249);

        Assert.False(SunDetector.TryFindSun(luma, width, height, out _));
    }

    [Fact]
    public void CustomThreshold_IsApplied()
    {
        const int width = 80, height = 80;
        byte[] luma = Frame(width, height, value: 200);
        FillDisk(luma, width, 40.0, 40.0, 8.0, 220);

        Assert.False(SunDetector.TryFindSun(luma, width, height, out _));
        Assert.True(SunDetector.TryFindSun(luma, width, height, 210, out SunDetection d));
        Assert.Equal(40.0, d.X, CentroidTolerancePx);
    }

    [Fact]
    public void BufferShorterThanFrame_Throws()
    {
        var luma = new byte[10];

        Assert.Throws<ArgumentException>(() => _ = SunDetector.TryFindSun(luma, 5, 4, out _));
    }

    [Fact]
    public void NonPositiveDimensions_Throw()
    {
        var luma = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = SunDetector.TryFindSun(luma, 0, 4, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = SunDetector.TryFindSun(luma, 5, -1, out _));
    }
}
