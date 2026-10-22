using AllSky.Astro;
using AllSky.Nina;
using Xunit;

namespace AllSky.Nina.Tests;

public sealed class TargetCurveTests
{
    // Sito di prova (valori arbitrari, non quelli di Robert).
    private const double Lat = 45.0;
    private const double Lon = 0.0;

    // GMST a 2000-01-01 12:00 UT (JD 2451545.0), Meeus eq. 12.4: 280.46061837 gradi.
    private const double GmstJ2000Noon = 280.46061837;
    private static readonly DateTimeOffset NoonJ2000 = new(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // Finestra di 12 ore centrata sul transito di NoonJ2000.
    private static readonly DateTimeOffset WindowStart = new(2000, 1, 1, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2000, 1, 1, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DayStart = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayEnd = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GmstJ2000_CoincideConRiferimentoMeeus()
    {
        // Il tempo sidereo locale a lon 0 e' il GMST: serve come controllo indipendente del transito.
        double lst = EquatorialToHorizontal.LocalMeanSiderealDeg(NoonJ2000, 0.0);
        Assert.Equal(GmstJ2000Noon, lst, 6);
    }

    [Fact]
    public void Zenit_AltezzaMassimaVicina90NelTransito()
    {
        // Dec = latitudine: nel transito l'oggetto e' allo zenit.
        TargetCurveResult r = TargetCurve.Compute("Zenit", GmstJ2000Noon, Lat, CoordinateEpoch.J2000, WindowStart, WindowEnd, Lat, Lon);

        Assert.Equal(TargetCurveStatus.Ok, r.Status);
        Assert.NotNull(r.TransitUtc);
        Assert.True(Math.Abs((r.TransitUtc!.Value - NoonJ2000).TotalMinutes) < 1.0,
            $"Transito atteso alle 12:00 UT, trovato {r.TransitUtc.Value:O}.");
        Assert.InRange(r.MaxAltitudeDeg!.Value, 89.99, 90.0001);
        Assert.InRange(r.TransitAltitudeDeg!.Value, 89.99, 90.0001);
    }

    [Fact]
    public void Serie_NumeroPuntiEMoonNelloStessoIstante()
    {
        TargetCurveResult r = TargetCurve.Compute("Zenit", GmstJ2000Noon, Lat, CoordinateEpoch.J2000, WindowStart, WindowEnd, Lat, Lon, 10);

        // 12 ore con passo di 10 minuti: 72 intervalli, 73 campioni (fine inclusa).
        Assert.Equal(73, r.Points.Count);
        Assert.Equal(WindowStart, r.Points[0].UtcTime);
        Assert.Equal(WindowEnd, r.Points[^1].UtcTime);

        foreach (TargetCurvePoint p in r.Points)
        {
            Assert.Equal(MoonPosition.Altitude(p.UtcTime, Lat, Lon), p.MoonAltitudeDeg, 9);
        }
        Assert.Equal(MoonPosition.Altitude(r.TransitUtc!.Value, Lat, Lon), r.MoonAltitudeAtTransitDeg!.Value, 9);
    }

    [Fact]
    public void TransitoAMeta_AltezzaDataDallaDeclinazione()
    {
        // Dec = -10, lat = 45: al transito superiore h = 90 - (45 - (-10)) = 35 gradi.
        TargetCurveResult r = TargetCurve.Compute("Sud", GmstJ2000Noon, -10.0, CoordinateEpoch.J2000, WindowStart, WindowEnd, Lat, Lon);

        Assert.Equal(TargetCurveStatus.Ok, r.Status);
        Assert.InRange(r.TransitAltitudeDeg!.Value, 34.99, 35.01);
        Assert.InRange(r.MaxAltitudeDeg!.Value, 34.99, 35.01);
    }

    [Fact]
    public void SottoOrizzonteTuttaLaNotte_OreSopraSogliaZero()
    {
        // Dec = -80, lat = 45: altezza massima al transito superiore = 90 - 125 = -35 gradi.
        TargetCurveResult r = TargetCurve.Compute("Sotto", GmstJ2000Noon, -80.0, CoordinateEpoch.J2000, DayStart, DayEnd, Lat, Lon, 10, 0.0);

        Assert.Equal(TargetCurveStatus.Ok, r.Status);
        Assert.All(r.Points, p => Assert.True(p.AltitudeDeg < 0.0, $"Altezza {p.AltitudeDeg} non negativa."));
        Assert.InRange(r.MaxAltitudeDeg!.Value, -35.01, -34.99);
        Assert.Equal(0.0, r.HoursAboveThreshold!.Value, 9);
    }

    [Fact]
    public void Circumpolare_SempreSopraSogliaZero_OreUgualiAllaFinestra()
    {
        // Dec = 60, lat = 45: altezza minima = 45 + 60 - 90 = 15 gradi, sempre sopra l'orizzonte.
        TargetCurveResult r = TargetCurve.Compute("Circumpolare", GmstJ2000Noon, 60.0, CoordinateEpoch.J2000, DayStart, DayEnd, Lat, Lon, 10, 0.0);

        Assert.Equal(TargetCurveStatus.Ok, r.Status);
        Assert.Equal(24.0, r.HoursAboveThreshold!.Value, 6);
    }

    [Fact]
    public void Equatore_OreSopraSogliaSonoMeta()
    {
        // Dec = 0: sopra l'orizzonte circa meta' del tempo in ogni giorno (a qualunque latitudine).
        TargetCurveResult r = TargetCurve.Compute("Equatore", GmstJ2000Noon, 0.0, CoordinateEpoch.J2000, DayStart, DayEnd, Lat, Lon, 10, 0.0);

        Assert.InRange(r.HoursAboveThreshold!.Value, 11.9, 12.1);
    }

    [Fact]
    public void SenzaSoglia_OreNonCalcolate()
    {
        TargetCurveResult r = TargetCurve.Compute("Zenit", GmstJ2000Noon, Lat, CoordinateEpoch.J2000, WindowStart, WindowEnd, Lat, Lon);

        Assert.Null(r.ThresholdDeg);
        Assert.Null(r.HoursAboveThreshold);
    }

    [Theory]
    [InlineData(CoordinateEpoch.OfDate)]
    [InlineData(CoordinateEpoch.Unknown)]
    public void EpocaNonJ2000_UnsupportedEpochSenzaSerie(CoordinateEpoch epoca)
    {
        TargetCurveResult r = TargetCurve.Compute("Zenit", GmstJ2000Noon, Lat, epoca, WindowStart, WindowEnd, Lat, Lon, 10, 0.0);

        Assert.Equal(TargetCurveStatus.UnsupportedEpoch, r.Status);
        Assert.NotNull(r.Reason);
        Assert.Empty(r.Points);
        Assert.Null(r.TransitUtc);
        Assert.Null(r.MaxAltitudeDeg);
    }

    [Fact]
    public void FinestraInvertita_InvalidWindow()
    {
        TargetCurveResult r = TargetCurve.Compute("Zenit", GmstJ2000Noon, Lat, CoordinateEpoch.J2000, WindowEnd, WindowStart, Lat, Lon);

        Assert.Equal(TargetCurveStatus.InvalidWindow, r.Status);
        Assert.Empty(r.Points);
        Assert.Empty(r.PhaseBands);
    }

    [Fact]
    public void FinestraVuota_InvalidWindow()
    {
        TargetCurveResult r = TargetCurve.Compute("Zenit", GmstJ2000Noon, Lat, CoordinateEpoch.J2000, WindowStart, WindowStart, Lat, Lon);

        Assert.Equal(TargetCurveStatus.InvalidWindow, r.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(-5)]
    public void PassoFuoriIntervallo_Errore(int passo)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TargetCurve.Compute("Zenit", GmstJ2000Noon, Lat, CoordinateEpoch.J2000, WindowStart, WindowEnd, Lat, Lon, passo));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void PassoAiLimiti_Accettato(int passo)
    {
        TargetCurveResult r = TargetCurve.Compute("Zenit", GmstJ2000Noon, Lat, CoordinateEpoch.J2000, WindowStart, WindowEnd, Lat, Lon, passo);

        Assert.Equal(TargetCurveStatus.Ok, r.Status);
    }

    [Fact]
    public void SoggettoAssente_NoTargetConSerieVuotaEFasi()
    {
        TargetCurveResult r = TargetCurve.Compute((NinaTargetState?)null, CoordinateEpoch.J2000, DayStart, DayEnd, Lat, Lon);

        Assert.Equal(TargetCurveStatus.NoTarget, r.Status);
        Assert.NotNull(r.Reason);
        Assert.Empty(r.Points);
        Assert.Null(r.TransitUtc);
        // Le fasi crepuscolari non dipendono da NINA.
        Assert.NotEmpty(r.PhaseBands);
    }

    [Fact]
    public void CoordinateMancanti_NoTarget()
    {
        var senzaDec = new NinaTargetState("Senza Dec", 10.0, null);
        TargetCurveResult r = TargetCurve.Compute(senzaDec, CoordinateEpoch.J2000, DayStart, DayEnd, Lat, Lon);

        Assert.Equal(TargetCurveStatus.NoTarget, r.Status);
        Assert.Empty(r.Points);
    }

    [Fact]
    public void DeclinazioneFuoriIntervallo_NoTarget()
    {
        TargetCurveResult r = TargetCurve.Compute("Errato", GmstJ2000Noon, 95.0, CoordinateEpoch.J2000, DayStart, DayEnd, Lat, Lon);

        Assert.Equal(TargetCurveStatus.NoTarget, r.Status);
        Assert.Empty(r.Points);
    }

    [Fact]
    public void FasiCrepuscolari_ContigueECopronoLaFinestra()
    {
        IReadOnlyList<PhaseBand> bands = TargetCurve.Compute("Zenit", GmstJ2000Noon, Lat, CoordinateEpoch.J2000, DayStart, DayEnd, Lat, Lon).PhaseBands;

        Assert.Equal(DayStart, bands[0].From);
        Assert.Equal(DayEnd, bands[^1].To);
        for (int i = 1; i < bands.Count; i++)
        {
            Assert.Equal(bands[i - 1].To, bands[i].From);
        }
        Assert.Contains(bands, b => b.Phase == SkyPhase.Night);
        Assert.Contains(bands, b => b.Phase == SkyPhase.Day);
    }
}
