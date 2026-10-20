using Xunit;

namespace AllSky.Imaging.Tests;

public class SkyMaskTests
{
    // Immagine 100 x 80, centro (50, 40), raggio normalizzato 1.0 -> R = min(100, 80) / 2 = 40 pixel.
    private const int W = 100;
    private const int H = 80;

    private static SkyMask Circle(IReadOnlyList<(double AzimuthDeg, double ElevationDeg)>? horizon = null, bool eastLeft = true)
        => SkyMask.Create(W, H, new SkyMaskOptions(0.5, 0.5, 1.0, horizon, eastLeft));

    [Fact]
    public void Circle_InsideIsSky_OutsideIsNot()
    {
        SkyMask mask = Circle();

        Assert.True(mask.IsSky(50, 40));
        Assert.True(mask.IsSky(50 + 39, 40));
        Assert.False(mask.IsSky(50 + 41, 40));
        Assert.False(mask.IsSky(0, 0));
    }

    [Fact]
    public void Circle_PixelCount_MatchesBruteForceCount()
    {
        int expected = 0;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                double dx = x - 50.0;
                double dy = y - 40.0;
                if (Math.Sqrt(dx * dx + dy * dy) <= 40.0)
                {
                    expected++;
                }
            }
        }

        Assert.Equal(expected, Circle().PixelCount);
    }

    [Fact]
    public void FlatHorizon_RemovesLowElevation()
    {
        // Orizzonte a 30 gradi: il limite e' a r = R * 60 / 90 = 26.7 pixel dal centro.
        SkyMask mask = Circle(new List<(double, double)> { (0, 30) });

        Assert.True(mask.IsSky(50, 20));   // r = 20, elevazione 45
        Assert.False(mask.IsSky(50, 10));  // r = 30, elevazione 22.5
    }

    [Fact]
    public void AzimuthHorizon_BlocksNorthOnly()
    {
        // Nord (alto) con orizzonte a 60 gradi, il resto piatto a 0.
        var horizon = new List<(double, double)> { (0, 60), (90, 0), (180, 0), (270, 0) };
        SkyMask mask = Circle(horizon);

        Assert.True(mask.IsSky(50, 30));   // Nord, r = 10, elevazione 67.5 >= 60
        Assert.False(mask.IsSky(50, 20));  // Nord, r = 20, elevazione 45 < 60
        Assert.True(mask.IsSky(50, 60));   // Sud, r = 20, elevazione 45 >= 0
    }

    [Fact]
    public void AzimuthHorizon_IsInterpolatedCircularly_BetweenLastAndFirstPoint()
    {
        // Tra 270 gradi (0) e 360 gradi (60) l'orizzonte sale linearmente.
        // Il pixel (53, 30) ha azimut ~343.3 e orizzonte ~48.9: elevazione 66.5, quindi cielo.
        // Il pixel (53, 10) ha r ~30.2, elevazione ~22.4: sotto l'orizzonte.
        var horizon = new List<(double, double)> { (0, 60), (90, 0), (180, 0), (270, 0) };
        SkyMask mask = Circle(horizon);

        Assert.True(mask.IsSky(53, 30));
        Assert.False(mask.IsSky(53, 10));
    }

    [Fact]
    public void EastLeft_ChoosesWhichSideIsEast()
    {
        // Est (90 gradi) con orizzonte a 60; ovest e nord a 0.
        var horizon = new List<(double, double)> { (0, 0), (90, 60), (180, 0), (270, 0) };
        const int leftX = 30;   // dx = -20, r = 20, elevazione 45
        const int rightX = 70;  // dx = +20

        SkyMask eastLeft = Circle(horizon, eastLeft: true);
        Assert.False(eastLeft.IsSky(leftX, 40));  // a sinistra c'e' l'Est: bloccato
        Assert.True(eastLeft.IsSky(rightX, 40));  // a destra c'e' l'Ovest: libero

        SkyMask eastRight = Circle(horizon, eastLeft: false);
        Assert.True(eastRight.IsSky(leftX, 40));
        Assert.False(eastRight.IsSky(rightX, 40));
    }

    [Fact]
    public void WithExcludedDiscs_RemovesDiscPixels_AndLeavesOriginalUnchanged()
    {
        SkyMask full = SkyMask.Full(W, H);
        SkyMask masked = full.WithExcludedDiscs(new[] { (50.0, 40.0, 10.0) });

        int inside = 0;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                double dx = x - 50.0;
                double dy = y - 40.0;
                if (dx * dx + dy * dy <= 100.0)
                {
                    inside++;
                }
            }
        }

        Assert.False(masked.IsSky(50, 40));
        Assert.True(masked.IsSky(0, 0));
        Assert.Equal(W * H - inside, masked.PixelCount);
        Assert.Equal(W * H, full.PixelCount);
    }

    [Fact]
    public void ToByteMask_MatchesPixelCount()
    {
        SkyMask mask = Circle();
        byte[] bytes = mask.ToByteMask();

        Assert.Equal(W * H, bytes.Length);
        Assert.Equal(mask.PixelCount, bytes.Count(v => v == 255));
        Assert.Equal(W * H - mask.PixelCount, bytes.Count(v => v == 0));
        Assert.Equal(255, bytes[40 * W + 50]);
    }

    [Fact]
    public void Create_RejectsNonPositiveRadius()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SkyMask.Create(10, 10, new SkyMaskOptions(0.5, 0.5, 0.0)));
    }

    [Fact]
    public void Create_RejectsEmptyHorizon()
    {
        var empty = new List<(double, double)>();
        Assert.Throws<ArgumentException>(() => SkyMask.Create(10, 10, new SkyMaskOptions(0.5, 0.5, 1.0, empty)));
    }
}
