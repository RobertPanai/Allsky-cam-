using Xunit;

namespace AllSky.Astro.Tests;

// Fuso di riferimento: Europe/Rome (CEST, UTC+2, in vigore il 19 ottobre 2026: il cambio ora e' il 25 ottobre).
public class ObservingNightTests
{
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);

    private static TimeZoneInfo Rome() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    [Fact]
    public void GetNightId_At0300LocalOnOct19_IsOct18()
    {
        var local = new DateTimeOffset(2026, 10, 19, 3, 0, 0, Cest);

        string id = ObservingNight.GetNightId(local, Rome());

        Assert.Equal("2026-10-18", id);
    }

    [Fact]
    public void GetNightId_At1300LocalOnOct19_IsOct19()
    {
        var local = new DateTimeOffset(2026, 10, 19, 13, 0, 0, Cest);

        string id = ObservingNight.GetNightId(local, Rome());

        Assert.Equal("2026-10-19", id);
    }

    [Fact]
    public void GetNightId_AtExactlyNoon_StartsNewNight()
    {
        var noon = new DateTimeOffset(2026, 10, 19, 12, 0, 0, Cest);

        Assert.Equal("2026-10-19", ObservingNight.GetNightId(noon, Rome()));
    }

    [Fact]
    public void GetNightId_OneSecondBeforeNoon_BelongsToPreviousNight()
    {
        var beforeNoon = new DateTimeOffset(2026, 10, 19, 11, 59, 59, Cest);

        Assert.Equal("2026-10-18", ObservingNight.GetNightId(beforeNoon, Rome()));
    }

    [Fact]
    public void GetNightId_UsesLocalTimeNotUtc()
    {
        // 23:30 UTC del 19/10 sono le 01:30 locali del 20/10 (CEST): la notte inizia il 19/10.
        var utc = new DateTimeOffset(2026, 10, 19, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal("2026-10-19", ObservingNight.GetNightId(utc, Rome()));
    }

    [Fact]
    public void GetBands_JuneNightInTurin_IsContiguousAndSpansNoonToNoon()
    {
        TimeZoneInfo tz = Rome();

        IReadOnlyList<PhaseBand> bands = ObservingNight.GetBands("2026-06-20", 45.07, 7.69, tz);

        Assert.NotEmpty(bands);
        Assert.Equal(new DateTimeOffset(2026, 6, 20, 12, 0, 0, Cest), bands[0].From);
        Assert.Equal(new DateTimeOffset(2026, 6, 21, 12, 0, 0, Cest), bands[bands.Count - 1].To);

        for (int i = 0; i + 1 < bands.Count; i++)
        {
            Assert.Equal(bands[i].To, bands[i + 1].From);
            Assert.True(bands[i].Phase != bands[i + 1].Phase, "Intervalli consecutivi con la stessa fase non sono stati uniti.");
            Assert.True(bands[i].To > bands[i].From, "Intervallo vuoto o invertito.");
        }
    }

    [Fact]
    public void GetBands_JuneNightInTurin_StartsAndEndsInDayAndContainsNight()
    {
        // Il Sole e' sopra l'orizzonte a mezzogiorno locale; a mezzanotte e' circa -21.5 gradi (notte).
        IReadOnlyList<PhaseBand> bands = ObservingNight.GetBands("2026-06-20", 45.07, 7.69, Rome());

        Assert.Equal(SkyPhase.Day, bands[0].Phase);
        Assert.Equal(SkyPhase.Day, bands[bands.Count - 1].Phase);
        Assert.Contains(bands, b => b.Phase == SkyPhase.Night);
    }

    [Fact]
    public void GetBands_NonPositiveStep_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ObservingNight.GetBands("2026-06-20", 45.07, 7.69, Rome(), TimeSpan.Zero));
    }

    [Fact]
    public void GetBands_DefaultStep_MatchesOneMinuteStep()
    {
        TimeZoneInfo tz = Rome();

        IReadOnlyList<PhaseBand> byDefault = ObservingNight.GetBands("2026-06-20", 45.07, 7.69, tz);
        IReadOnlyList<PhaseBand> explicitOneMinute = ObservingNight.GetBands(
            "2026-06-20", 45.07, 7.69, tz, TimeSpan.FromMinutes(1));

        Assert.Equal(explicitOneMinute, byDefault);
    }
}
