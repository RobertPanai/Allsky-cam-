using Xunit;

namespace AllSky.ColorMetrics.Tests;

public sealed class StarColorTests
{
    // Fondo cielo di prova (byte): R 26, G 31, B 38.
    private const byte BgR = 26;
    private const byte BgG = 31;
    private const byte BgB = 38;

    /// <summary>
    /// Fondo uniforme con stelle gaussiane (sigma 1 pixel). Ogni stella somma (ampR, ampG, ampB) × gaussiana, in ADU.
    /// </summary>
    private static Rgb8Image Stars(int w, int h, IEnumerable<(int X, int Y, double AmpR, double AmpG, double AmpB)> stars)
    {
        var list = stars.ToList();
        return ColorTestData.Make(w, h, (x, y) =>
        {
            double r = BgR;
            double g = BgG;
            double b = BgB;
            foreach ((int sx, int sy, double ar, double ag, double ab) in list)
            {
                double d2 = (x - sx) * (x - sx) + (y - sy) * (y - sy);
                double k = Math.Exp(-d2 / 2.0);
                r += ar * k;
                g += ag * k;
                b += ab * k;
            }

            return ((byte)Math.Clamp(Math.Round(r), 0, 255), (byte)Math.Clamp(Math.Round(g), 0, 255), (byte)Math.Clamp(Math.Round(b), 0, 255));
        });
    }

    private static FrameSample Sky(Rgb8Image img) =>
        FrameSampler.Create(img, ColorTestData.Sidecar(ColorTestData.T0));

    [Fact]
    public void StelleBianche_RapportiVicinoAUno()
    {
        var pos = new[] { (20, 20), (44, 20), (20, 44), (44, 44), (32, 32) };
        Rgb8Image img = Stars(64, 64, pos.Select(p => (p.Item1, p.Item2, 120.0, 120.0, 120.0)));
        StarColorFrame f = StarColor.Measure(img, Sky(img));

        Assert.Equal(5, f.Candidates);
        Assert.Equal(5, f.Stars);
        Assert.Equal(1.0, f.MedianRg!.Value, 0.02);
        Assert.Equal(1.0, f.MedianBg!.Value, 0.02);
    }

    [Fact]
    public void StellaColorata_RapportiAttesi()
    {
        // Ampiezze (1.3, 1.0, 0.7) × 120 ADU: R/G atteso 1.3, B/G atteso 0.7.
        var pos = new[] { (20, 20), (44, 44), (20, 44), (44, 20), (32, 32) };
        Rgb8Image img = Stars(64, 64, pos.Select(p => (p.Item1, p.Item2, 156.0, 120.0, 84.0)));
        StarColorFrame f = StarColor.Measure(img, Sky(img));

        Assert.Equal(5, f.Stars);
        Assert.Equal(1.3, f.MedianRg!.Value, 0.03);
        Assert.Equal(0.7, f.MedianBg!.Value, 0.03);
    }

    [Fact]
    public void StellaFuoriDallaZonaValida_Ignorata()
    {
        // La stella in (2,2) è fuori dal cerchio ridotto di un 64x64 (raggio 28.8 dal centro): non è un candidato.
        Rgb8Image img = Stars(64, 64, new[] { (20, 20, 120.0, 120.0, 120.0), (44, 44, 120.0, 120.0, 120.0), (2, 2, 120.0, 120.0, 120.0) });
        StarColorFrame f = StarColor.Measure(img, Sky(img));

        Assert.Equal(2, f.Candidates);
        Assert.Equal(2, f.Stars);
    }

    [Fact]
    public void GradienteForteDelCielo_StellaRilevata()
    {
        // Cielo neutro che sale da 60 a 200 ADU lungo x (gradiente forte), una stella bianca da 90 ADU al centro.
        // La MAD della luminanza grezza (~35 ADU) farebbe una soglia irraggiungibile; le differenze orizzontali
        // del gradiente sono costanti e non gonfiano il rumore stimato.
        Rgb8Image img = ColorTestData.Make(64, 64, (x, y) =>
        {
            double g = 60 + 140.0 * x / 63.0;
            double k = Math.Exp(-((x - 32) * (x - 32) + (y - 32) * (y - 32)) / 2.0);
            byte v = (byte)Math.Clamp(Math.Round(g + 90 * k), 0, 255);
            return (v, v, v);
        });
        StarColorFrame f = StarColor.Measure(img, Sky(img));

        Assert.Equal(1, f.Stars);
        Assert.Equal(1.0, f.MedianRg!.Value, 0.02);
    }

    [Fact]
    public void StellaSaturata_Esclusa()
    {
        Rgb8Image img = Stars(64, 64, new[] { (32, 32, 240.0, 240.0, 240.0) });
        StarColorFrame f = StarColor.Measure(img, Sky(img));

        Assert.Equal(0, f.Stars);
        Assert.Null(f.MedianRg);
    }

    [Fact]
    public void Summarize_UsaSoloFrameConAlmenoCinqueStelle()
    {
        var frames = new List<StarColorFrame>
        {
            new(ColorTestData.T0, 10, 10, 1.0, 1.0),
            new(ColorTestData.T0.AddMinutes(1), 10, 10, 1.1, 1.1),
            new(ColorTestData.T0.AddMinutes(2), 10, 10, 1.2, 1.2),
            new(ColorTestData.T0.AddMinutes(3), 2, 2, 9.0, 9.0),
        };
        StarColorSummary s = StarColor.Summarize(frames);

        Assert.Equal(3, s.FramesUsed);
        Assert.Equal(4, s.FramesWithStars);
        Assert.Equal(1.1, s.MedianRg!.Value, 1e-9);
        Assert.Equal(1.05, s.MedianRgQ25!.Value, 1e-9);
        Assert.Equal(1.15, s.MedianRgQ75!.Value, 1e-9);
    }
}
