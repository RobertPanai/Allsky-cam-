using Xunit;

namespace AllSky.Sky.Tests;

// Calibrazione fotometrica e brillanza del cielo (P9.2, P9.3). Dati sintetici con parametri noti, generati in modo deterministico.
public class PhotometryTests
{
    private const double TrueZeroPoint = 20.0;
    private const double TrueExtinction = 0.25;
    private const double TrueColorTerm = 0.10;

    /// <summary>Stelle sintetiche: massa d'aria e B−V su griglie deterministiche, rumore sinusoidale di ampiezza nota.</summary>
    private static List<PhotometricSample> Samples(int count, double noiseMag)
    {
        var list = new List<PhotometricSample>();
        for (int i = 0; i < count; i++)
        {
            double airMass = 1.0 + (i % 7) * 0.15;
            double bv = -0.2 + (i * 13 % 25) * 0.07;
            double catalogue = 1.0 + (i * 11 % 40) * 0.1;
            double noise = noiseMag * (((i * 7919) % 101) / 50.0 - 1.0);
            double instrumental = catalogue - TrueZeroPoint - TrueExtinction * airMass - TrueColorTerm * bv + noise;
            list.Add(new PhotometricSample(instrumental, catalogue, airMass, bv));
        }

        return list;
    }

    [Fact]
    public void Fit_NoiseFree_RecoversZeroPointExtinctionAndColorTerm()
    {
        PhotometricFit fit = Photometry.Fit(Samples(40, noiseMag: 0.0));

        // Il modello è m_cat = m_strum + Z + k·X + c·(B−V): con m_strum = m_cat − Z − kX − cBV, il fit ritrova Z, k e c.
        Assert.InRange(fit.ZeroPoint, TrueZeroPoint - 1e-6, TrueZeroPoint + 1e-6);
        Assert.InRange(fit.Extinction, TrueExtinction - 1e-6, TrueExtinction + 1e-6);
        Assert.InRange(fit.ColorTerm, TrueColorTerm - 1e-6, TrueColorTerm + 1e-6);
        Assert.True(fit.RmsMag < 1e-6);
        Assert.Equal(40, fit.Count);
    }

    [Fact]
    public void Fit_WithNoise_StaysWithinTolerance()
    {
        PhotometricFit fit = Photometry.Fit(Samples(60, noiseMag: 0.02));

        Assert.InRange(fit.ZeroPoint, TrueZeroPoint - 0.05, TrueZeroPoint + 0.05);
        Assert.InRange(fit.Extinction, TrueExtinction - 0.02, TrueExtinction + 0.02);
        Assert.InRange(fit.ColorTerm, TrueColorTerm - 0.02, TrueColorTerm + 0.02);
    }

    [Fact]
    public void Fit_TooFewStars_Throws()
    {
        Assert.Throws<ArgumentException>(() => Photometry.Fit(Samples(3, noiseMag: 0.0)));
    }

    [Fact]
    public void Fit_DegenerateAirMassAndColor_Throws()
    {
        // Tutte le stelle alla stessa massa d'aria e stesso colore: estinzione e colore non si separano.
        var same = new List<PhotometricSample>();
        for (int i = 0; i < 6; i++)
        {
            same.Add(new PhotometricSample(5.0 + i * 0.1, 5.0 + i * 0.1, 1.0, 0.5));
        }

        Assert.Throws<ArgumentException>(() => Photometry.Fit(same));
    }

    [Fact]
    public void SkyClarityIndex_IsRatioClampedToOne()
    {
        Assert.Equal(0.5, Photometry.SkyClarityIndex(50, 100), precision: 12);
        Assert.Equal(1.0, Photometry.SkyClarityIndex(120, 100), precision: 12);
        Assert.Equal(0.0, Photometry.SkyClarityIndex(0, 100), precision: 12);
    }

    [Fact]
    public void SkyClarityIndex_RejectsNonPositiveExpected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Photometry.SkyClarityIndex(5, 0));
    }
}
