using System;
using System.Collections.Generic;
using System.Linq;
using AllSky.Core.Design;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class StarfieldGeneratorTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>Angolo della banda predefinito, usato per ricalcolare la distanza dalla banda nei test.</summary>
    private static double BandDistance(Star star, int width, int height, StarfieldOptions options)
    {
        double theta = options.BandAngleDegrees * Math.PI / 180.0;
        double dirX = Math.Cos(theta);
        double dirY = -Math.Sin(theta);
        double perpX = -dirY;
        double perpY = dirX;
        return (star.X - width / 2.0) * perpX + (star.Y - height / 2.0) * perpY;
    }

    /// <summary>
    /// Frazione delle stelle nella fascia centrale della banda (entro 1 sigma) e in una fascia lontana (oltre 3 sigma).
    /// </summary>
    private static (double Core, double Far) BandFractions(IReadOnlyList<Star> stars, StarfieldOptions options)
    {
        double sigmaPx = options.BandSigmaDegrees * Width / options.FieldWidthDegrees;
        int core = stars.Count(s => Math.Abs(BandDistance(s, Width, Height, options)) < sigmaPx);
        int far = stars.Count(s => Math.Abs(BandDistance(s, Width, Height, options)) > 3 * sigmaPx);
        return (core / (double)stars.Count, far / (double)stars.Count);
    }

    [Fact]
    public void SameSeed_GivesSamePoints()
    {
        IReadOnlyList<Star> first = StarfieldGenerator.Generate(20261022, Width, Height);
        IReadOnlyList<Star> second = StarfieldGenerator.Generate(20261022, Width, Height);

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], second[i]);
        }
    }

    [Fact]
    public void DifferentSeed_GivesDifferentPoints()
    {
        IReadOnlyList<Star> first = StarfieldGenerator.Generate(1, Width, Height);
        IReadOnlyList<Star> second = StarfieldGenerator.Generate(2, Width, Height);

        Assert.NotEqual(first[0], second[0]);
    }

    [Fact]
    public void Count_FollowsNakedEyeDensityOverTheField()
    {
        var options = new StarfieldOptions();

        // Campo di 60° in orizzontale: l'altezza segue il rapporto d'aspetto, quindi l'area è 60 × 33.75 gradi².
        double skyDegrees2 = options.FieldWidthDegrees * (Height / (Width / options.FieldWidthDegrees));
        int expected = (int)Math.Round(StarfieldGenerator.NakedEyeStarsPerSquareDegree * skyDegrees2);

        Assert.Equal(expected, StarfieldGenerator.Generate(7, Width, Height, options).Count);
    }

    [Fact]
    public void Count_ScalesWithDensityScale()
    {
        var doubled = new StarfieldOptions { DensityScale = 2.0 };

        int normal = StarfieldGenerator.Generate(7, Width, Height).Count;
        int twice = StarfieldGenerator.Generate(7, Width, Height, doubled).Count;

        Assert.InRange(twice / (double)normal, 1.98, 2.02);
    }

    [Fact]
    public void Stars_StayInsideTheArea_WithinMagnitudeLimits()
    {
        var options = new StarfieldOptions();

        foreach (Star star in StarfieldGenerator.Generate(99, Width, Height, options))
        {
            Assert.InRange(star.X, 0, Width);
            Assert.InRange(star.Y, 0, Height);
            Assert.InRange(star.Magnitude, options.MinMagnitude, options.MaxMagnitude);
            Assert.InRange(star.Alpha, 0.2, 1.0);
            Assert.True(star.Radius > 0);
        }
    }

    [Fact]
    public void Magnitudes_FollowTheLogarithmicCount()
    {
        // Senza banda, la distribuzione è quella pura: N(<m) ∝ 10^(b·m) − 10^(b·m_min).
        var options = new StarfieldOptions { BandFraction = 0, DensityScale = 400 };
        IReadOnlyList<Star> stars = StarfieldGenerator.Generate(5, Width, Height, options);

        double b = options.LogSlope;
        double low = Math.Pow(10, b * options.MinMagnitude);
        double Cumulative(double m) => Math.Pow(10, b * m) - low;

        double expectedRatio = Cumulative(4) / Cumulative(2);
        double observedRatio = stars.Count(s => s.Magnitude < 4) / (double)stars.Count(s => s.Magnitude < 2);

        Assert.InRange(observedRatio / expectedRatio, 0.9, 1.1);
    }

    [Fact]
    public void Milky_Way_Band_ConcentratesStars()
    {
        // Confronto di densità, non di frazioni: la fascia centrale occupa un'area diversa da quella lontana.
        // Con campione uniforme la frazione in ciascuna fascia è proporzionale all'area.
        var uniform = new StarfieldOptions { BandFraction = 0, DensityScale = 50 };
        var banded = new StarfieldOptions { BandFraction = 0.4, DensityScale = 50 };

        (double uCore, double uFar) = BandFractions(StarfieldGenerator.Generate(3, Width, Height, uniform), uniform);
        (double bCore, double bFar) = BandFractions(StarfieldGenerator.Generate(3, Width, Height, banded), banded);

        double concentration = (bCore / uCore) / (bFar / uFar);
        Assert.True(concentration > 2.0, $"Concentrazione nella banda: {concentration:F2} (attesa > 2).");
    }

    [Fact]
    public void Brighter_Stars_AreLarger()
    {
        IReadOnlyList<Star> stars = StarfieldGenerator.Generate(11, Width, Height);

        Star brightest = stars.OrderBy(s => s.Magnitude).First();
        Star faintest = stars.OrderByDescending(s => s.Magnitude).First();

        Assert.True(brightest.Radius > faintest.Radius);
        Assert.True(brightest.Alpha > faintest.Alpha);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void NonPositiveSize_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StarfieldGenerator.Generate(1, width, height));
    }

    [Fact]
    public void InvalidBandFraction_Throws()
    {
        var options = new StarfieldOptions { BandFraction = 1.5 };

        Assert.Throws<ArgumentOutOfRangeException>(() => StarfieldGenerator.Generate(1, Width, Height, options));
    }
}
