using AllSky.Astro;
using AllSky.Core.Tonight;
using Xunit;

namespace AllSky.Core.Tests;

/// <summary>
/// Confronto dei crepuscoli con una fonte indipendente.
/// Fonte: timeanddate.com, pagina "Astronomy" di Torino (Italia), dati del 21/05/2025, ora locale (CEST, UTC+2).
/// URL: https://www.timeanddate.com/astronomy/italy/turin (letta il 22/10/2026 tramite WebFetch).
/// USNO (aa.usno.navy.mil) non e' stata letta: il permesso di accesso non e' stato concesso in questa sessione.
/// Coordinate del sito di test: 45.07 N, 7.69 E (come il default di AppConfig). La fonte non dichiara le coordinate
/// esatte; la differenza con Torino centro (45.0703 N, 7.6869 E) produce meno di 1 s di scarto sull'orario.
/// Tolleranza: 2 minuti sui crepuscoli civile, nautico e astronomico, che sono passaggi del centro del Sole a
/// -6, -12 e -18 gradi. L'arrotondamento al minuto della fonte e il campionamento di 1 minuto di ObservingNight
/// stanno entro questa tolleranza. Tramonto e alba della fonte sono al bordo superiore del Sole (refrazione
/// e semidiametro, circa -0,83 gradi). Il modello usa il centro a 0 gradi: il tramonto del modello cade 2-5 minuti
/// prima di quello della fonte, l'alba 2-5 minuti dopo. Il test controlla questo scarto, non un'uguaglianza.
/// </summary>
public sealed class TonightCalculatorTests
{
    private const double Lat = 45.07;
    private const double Lon = 7.69;
    private const int ToleranceMinutes = 2;

    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    /// <summary>Istante UTC di un orario locale di Torino (CEST) del 21/05/2025.</summary>
    private static DateTimeOffset Local(int hour, int minute, int dayOffset = 0)
    {
        var local = new DateTime(2025, 5, 21, hour, minute, 0, DateTimeKind.Unspecified).AddDays(dayOffset);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Rome), TimeSpan.Zero);
    }

    /// <summary>Istante in cui una fase inizia: primo intervallo della fase indicata che comincia dopo l'inizio notte.</summary>
    private static DateTimeOffset StartOf(IReadOnlyList<PhaseBand> bands, SkyPhase phase, int occurrence = 0)
    {
        return bands.Where(b => b.Phase == phase).Skip(occurrence).First().From;
    }

    /// <summary>Istante in cui una fase inizia, prendendo l'ultima occorrenza nella notte (il mattino).</summary>
    private static DateTimeOffset LastStartOf(IReadOnlyList<PhaseBand> bands, SkyPhase phase) =>
        bands.Where(b => b.Phase == phase).Last().From;

    [Fact]
    public void Dusk_MatchesTimeAndDate_WithinTolerance()
    {
        // Notte che contiene il tardo pomeriggio del 21/05/2025 (la notte inizia a mezzogiorno locale).
        IReadOnlyList<PhaseBand> bands = ObservingNight.GetBands("2025-05-21", Lat, Lon, Rome);

        // Fonte: crepuscolo civile 21:33, nautico 22:19, astronomico 22:19-23:13 (fine notte astronomica 23:13 circa).
        AssertWithin(Local(21, 33), StartOf(bands, SkyPhase.NauticalTwilight));
        AssertWithin(Local(22, 19), StartOf(bands, SkyPhase.AstronomicalTwilight));
        AssertWithin(Local(23, 13), StartOf(bands, SkyPhase.Night));
    }

    [Fact]
    public void Dawn_MatchesTimeAndDate_WithinTolerance()
    {
        // Mattina del 21/05/2025: appartiene alla notte del 20/05 (prima di mezzogiorno locale).
        IReadOnlyList<PhaseBand> bands = ObservingNight.GetBands("2025-05-20", Lat, Lon, Rome);

        // Fonte: astronomico 03:39, nautico 04:33, civile 05:18. Nella notte c'e' anche il crepuscolo serale del 20/05,
        // quindi si prende l'ultima occorrenza di ogni fase, che e' quella del mattino.
        AssertWithin(Local(3, 39), LastStartOf(bands, SkyPhase.AstronomicalTwilight));
        AssertWithin(Local(4, 33), LastStartOf(bands, SkyPhase.NauticalTwilight));
        AssertWithin(Local(5, 18), LastStartOf(bands, SkyPhase.CivilTwilight));
    }

    [Fact]
    public void SunriseAndSunset_DifferFromTimeAndDate_OnlyByRefractionAndLimb()
    {
        // Fonte: tramonto 20:58 e alba 05:53, bordo superiore del Sole (centro a -0,83 gradi).
        // Il modello usa il centro a 0 gradi: al tramonto il centro passa per 0 prima che per -0,83 (il modello
        // anticipa di 2-5 minuti); all'alba il centro passa per 0 dopo (il modello ritarda di 2-5 minuti).
        IReadOnlyList<PhaseBand> evening = ObservingNight.GetBands("2025-05-21", Lat, Lon, Rome);
        IReadOnlyList<PhaseBand> morning = ObservingNight.GetBands("2025-05-20", Lat, Lon, Rome);

        DateTimeOffset sunset = StartOf(evening, SkyPhase.CivilTwilight);
        DateTimeOffset sunrise = morning.Last(b => b.Phase == SkyPhase.CivilTwilight).To;

        Assert.InRange((sunset - Local(20, 58)).TotalMinutes, -7.0, 0.0);
        Assert.InRange((sunrise - Local(5, 53)).TotalMinutes, 0.0, 7.0);
    }

    [Fact]
    public void SolarAltitude_AtTimeAndDateSunrise_IsAboutMinusEightTenths()
    {
        // Verifica indipendente dell'algoritmo solare: all'alba indicata dalla fonte (05:53 CEST, bordo superiore)
        // il centro del Sole deve stare a circa -0,83 gradi.
        double altitude = SolarPosition.Altitude(Local(5, 53), Lat, Lon);

        Assert.InRange(altitude, -0.95, -0.70);
    }

    [Fact]
    public void Compute_ReturnsTimelineWithSamplesForTheNight()
    {
        var now = new DateTimeOffset(2025, 5, 21, 20, 0, 0, TimeSpan.Zero);

        TonightSnapshot snapshot = TonightCalculator.Compute(now, Lat, Lon, Rome);

        Assert.Equal("2025-05-21", snapshot.NightId);
        Assert.True(snapshot.Timeline.Available);
        Assert.NotEmpty(snapshot.Timeline.Samples);
        Assert.Equal(snapshot.Timeline.Sunset, snapshot.Timeline.Samples[0].Utc);
        Assert.Equal(snapshot.Timeline.Sunrise, snapshot.Timeline.Samples[^1].Utc);
        Assert.InRange(snapshot.Timeline.Samples[0].SunAltitudeDeg, -0.9, 0.9);
    }

    [Fact]
    public void Compute_MidnightSun_HasNoWindow()
    {
        // Latitudine 80 N al solstizio: il Sole non tramonta.
        var now = new DateTimeOffset(2025, 6, 21, 12, 0, 0, TimeSpan.Zero);

        TonightSnapshot snapshot = TonightCalculator.Compute(now, 80.0, Lon, Rome);

        Assert.False(snapshot.Timeline.Available);
    }

    [Fact]
    public void ResolveTimeZone_UnknownId_FallsBackToUtc()
    {
        Assert.Equal(TimeZoneInfo.Utc, TonightSite.ResolveTimeZone("Non/Esiste"));
        Assert.Equal(TimeZoneInfo.Utc, TonightSite.ResolveTimeZone(null));
    }

    private static void AssertWithin(DateTimeOffset expected, DateTimeOffset actual)
    {
        double diff = Math.Abs((actual - expected).TotalMinutes);
        Assert.True(
            diff <= ToleranceMinutes,
            $"Atteso {expected:HH:mm} UTC, ottenuto {actual:HH:mm} UTC: differenza {diff:F1} min.");
    }
}
