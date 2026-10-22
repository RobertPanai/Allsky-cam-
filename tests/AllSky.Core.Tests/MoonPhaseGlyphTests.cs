using System.Globalization;
using AllSky.Core.Tonight;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class MoonPhaseGlyphTests
{
    /// <summary>Area del contorno (formula dei lacci) divisa per l'area del disco di raggio 1.</summary>
    private static double LitFraction(IReadOnlyList<GlyphPoint> outline)
    {
        double twiceArea = 0.0;
        for (int i = 0; i < outline.Count; i++)
        {
            GlyphPoint a = outline[i];
            GlyphPoint b = outline[(i + 1) % outline.Count];
            twiceArea += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(twiceArea / 2.0) / Math.PI;
    }

    private static double MeanX(IReadOnlyList<GlyphPoint> outline) => outline.Average(p => p.X);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    public void Outline_EnclosedArea_EqualsIlluminatedFraction(double fraction)
    {
        // Il contorno è un poligono con 48 segmenti per arco: l'errore sull'area è piccolo.
        IReadOnlyList<GlyphPoint> outline = MoonPhaseGlyph.Outline(fraction, litOnRight: true, steps: 96);

        Assert.Equal(fraction, LitFraction(outline), precision: 2);
    }

    [Fact]
    public void Outline_LitOnRight_LiesOnTheRightHalf_ForCrescentAndGibbous()
    {
        Assert.True(MeanX(MoonPhaseGlyph.Outline(0.25, litOnRight: true)) > 0.0);
        Assert.True(MeanX(MoonPhaseGlyph.Outline(0.75, litOnRight: true)) > 0.0);
    }

    [Fact]
    public void Outline_LitOnLeft_MirrorsTheRightSideOutline()
    {
        Assert.True(MeanX(MoonPhaseGlyph.Outline(0.25, litOnRight: false)) < 0.0);
        Assert.True(MeanX(MoonPhaseGlyph.Outline(0.75, litOnRight: false)) < 0.0);
    }

    [Fact]
    public void Outline_RejectsFractionOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MoonPhaseGlyph.Outline(1.2, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => MoonPhaseGlyph.Outline(double.NaN, true));
    }

    [Theory]
    [InlineData(0.01, true, MoonPhaseName.New)]
    [InlineData(0.99, false, MoonPhaseName.Full)]
    [InlineData(0.5, true, MoonPhaseName.FirstQuarter)]
    [InlineData(0.5, false, MoonPhaseName.LastQuarter)]
    [InlineData(0.2, true, MoonPhaseName.WaxingCrescent)]
    [InlineData(0.2, false, MoonPhaseName.WaningCrescent)]
    [InlineData(0.8, true, MoonPhaseName.WaxingGibbous)]
    [InlineData(0.8, false, MoonPhaseName.WaningGibbous)]
    public void Classify_NamesThePhase(double fraction, bool waxing, MoonPhaseName expected)
    {
        Assert.Equal(expected, MoonPhaseGlyph.Classify(fraction, waxing));
    }

    [Fact]
    public void At_ReportsWaxingBeforeFullMoon_AndWaningAfter()
    {
        // Luna piena del 12/05/2025 intorno alle 15 UTC (verifica di consistenza, non di precisione: il test
        // usa due giorni di margine e controlla solo il verso e la fase vicina alla piena).
        var fullMoon = new DateTimeOffset(2025, 5, 12, 15, 0, 0, TimeSpan.Zero);

        MoonPhaseGlyph before = MoonPhaseGlyph.At(fullMoon.AddDays(-2), 45.07);
        MoonPhaseGlyph atFull = MoonPhaseGlyph.At(fullMoon, 45.07);
        MoonPhaseGlyph after = MoonPhaseGlyph.At(fullMoon.AddDays(2), 45.07);

        Assert.True(before.Waxing);
        Assert.False(after.Waxing);
        Assert.True(atFull.IlluminatedFraction > 0.98);
        Assert.Equal(MoonPhaseName.Full, atFull.Name);
    }

    [Fact]
    public void At_SouthernHemisphere_FlipsTheLitSide()
    {
        var instant = new DateTimeOffset(2025, 5, 8, 12, 0, 0, TimeSpan.Zero);

        MoonPhaseGlyph north = MoonPhaseGlyph.At(instant, 45.07);
        MoonPhaseGlyph south = MoonPhaseGlyph.At(instant, -33.9);

        Assert.Equal(north.Waxing, south.Waxing);
        Assert.Equal(north.IlluminatedFraction, south.IlluminatedFraction, precision: 12);
        Assert.True(Math.Sign(MeanX(north.LitOutline)) != Math.Sign(MeanX(south.LitOutline)));
    }
}
