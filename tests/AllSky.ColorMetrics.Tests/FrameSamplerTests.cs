using Xunit;

namespace AllSky.ColorMetrics.Tests;

public sealed class FrameSamplerTests
{
    // Tolleranza sulla luminanza Rec.709 di (51,102,153): 0.2126*0.2 + 0.7152*0.4 + 0.0722*0.6 = 0.371920.
    private const double LumaUniform = 0.371920;

    [Fact]
    public void SfondoUniforme_MedianaEsattaEMediaLuminanza()
    {
        Rgb8Image img = ColorTestData.Uniform(64, 64, 51, 102, 153);
        FrameSample s = FrameSampler.Create(img, ColorTestData.Sidecar(ColorTestData.T0, "night", 5, 300));

        Assert.Equal(0.2, s.SkyR, 1e-9);
        Assert.Equal(0.4, s.SkyG, 1e-9);
        Assert.Equal(0.6, s.SkyB, 1e-9);
        Assert.Equal(LumaUniform, s.MeanLuminance, 1e-4);
        Assert.Equal("night", s.Mode);
        Assert.Equal(5.0, s.ExposureMs);
        Assert.Equal(300.0, s.Gain);
        Assert.Equal(ColorTestData.T0, s.TimeUtc);
    }

    [Fact]
    public void AngoliNeri_FuoriDalCerchio_NonEntranoNelFondo()
    {
        // Angoli a (0,0,0): fuori dal cerchio ridotto. Se entrassero, la mediana del fondo scenderebbe.
        Rgb8Image img = ColorTestData.Make(64, 64, (x, y) =>
        {
            bool corner = (x < 10 || x > 53) && (y < 10 || y > 53);
            return corner ? ((byte)0, (byte)0, (byte)0) : ((byte)51, (byte)102, (byte)153);
        });
        FrameSample s = FrameSampler.Create(img, ColorTestData.Sidecar(ColorTestData.T0));

        Assert.Equal(0.2, s.SkyR, 1e-9);
        Assert.Equal(0.4, s.SkyG, 1e-9);
        Assert.Equal(LumaUniform, s.MeanLuminance, 1e-4);
    }

    [Fact]
    public void PixelBrillanti_SopraIl70Percentile_NonEntranoNelFondo()
    {
        // Un blocco 8x8 di bianco puro al centro (circa 1.5% della zona) sta sopra il 70° percentile e non sposta il fondo.
        Rgb8Image img = ColorTestData.Make(64, 64, (x, y) =>
            x is >= 28 and < 36 && y is >= 28 and < 36 ? ((byte)255, (byte)255, (byte)255) : ((byte)51, (byte)102, (byte)153));
        FrameSample s = FrameSampler.Create(img, ColorTestData.Sidecar(ColorTestData.T0));

        Assert.Equal(0.2, s.SkyR, 1e-9);
        Assert.Equal(0.4, s.SkyG, 1e-9);
        Assert.Equal(0.6, s.SkyB, 1e-9);
    }

    [Fact]
    public void ZonaValida_RaggioNovantaPercentoDelCerchioInscritto()
    {
        ValidZone z = ValidZone.Inscribed(1280, 960);
        Assert.Equal(0.9 * 480.0, z.Radius, 1e-9);
        Assert.True(z.Contains(640, 480));
        Assert.False(z.Contains(0, 0));
        Assert.True(z.PixelIndices().Length > 0);
    }

    [Fact]
    public void ModeNonRiconosciuto_DiventaNull()
    {
        FrameSample s = FrameSampler.Create(ColorTestData.Uniform(16, 16, 10, 10, 10), ColorTestData.Sidecar(ColorTestData.T0, "dusk"));
        Assert.Null(s.Mode);
    }

    [Fact]
    public void Creazione_ConArgomentoNullo_Lancia()
    {
        Assert.Throws<ArgumentNullException>(() => FrameSampler.Create(null!, ColorTestData.Sidecar(ColorTestData.T0)));
    }
}
