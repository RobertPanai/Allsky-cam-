using AllSky.Astro;
using AllSky.Core.Tonight;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class TonightTimelineTests
{
    private static DateTimeOffset At(int hour, int minute, int dayOffset = 0) =>
        new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero).AddDays(dayOffset).AddHours(hour - 12).AddMinutes(minute);

    /// <summary>Notte sintetica: giorno fino alle 18:00, crepuscoli, notte, alba alle 05:30 del giorno dopo.</summary>
    private static IReadOnlyList<PhaseBand> SyntheticNight() => new List<PhaseBand>
    {
        new(SkyPhase.Day, At(12, 0), At(18, 0)),
        new(SkyPhase.CivilTwilight, At(18, 0), At(18, 30)),
        new(SkyPhase.NauticalTwilight, At(18, 30), At(19, 0)),
        new(SkyPhase.AstronomicalTwilight, At(19, 0), At(20, 0)),
        new(SkyPhase.Night, At(20, 0), At(4, 0, 1)),
        new(SkyPhase.AstronomicalTwilight, At(4, 0, 1), At(4, 30, 1)),
        new(SkyPhase.NauticalTwilight, At(4, 30, 1), At(5, 0, 1)),
        new(SkyPhase.CivilTwilight, At(5, 0, 1), At(5, 30, 1)),
        new(SkyPhase.Day, At(5, 30, 1), At(12, 0, 1)),
    };

    [Fact]
    public void Build_FindsSunsetAndSunrise_FromDayTransitions()
    {
        TonightTimeline timeline = TonightTimeline.Build(SyntheticNight());

        Assert.True(timeline.Available);
        Assert.Equal(At(18, 0), timeline.Sunset);
        Assert.Equal(At(5, 30, 1), timeline.Sunrise);
    }

    [Fact]
    public void Build_SegmentsCoverTheWindowInOrder()
    {
        TonightTimeline timeline = TonightTimeline.Build(SyntheticNight());

        Assert.Equal(
            [SkyPhase.CivilTwilight, SkyPhase.NauticalTwilight, SkyPhase.AstronomicalTwilight, SkyPhase.Night,
             SkyPhase.AstronomicalTwilight, SkyPhase.NauticalTwilight, SkyPhase.CivilTwilight],
            timeline.Segments.Select(s => s.Phase).ToArray());
        Assert.Equal(timeline.Sunset, timeline.Segments[0].From);
        Assert.Equal(timeline.Sunrise, timeline.Segments[^1].To);
    }

    [Fact]
    public void Build_ChangesStartAtSunsetAndEndWithDay()
    {
        TonightTimeline timeline = TonightTimeline.Build(SyntheticNight());

        Assert.Equal(new TonightPhaseChange(SkyPhase.CivilTwilight, At(18, 0)), timeline.Changes[0]);
        Assert.Equal(new TonightPhaseChange(SkyPhase.Day, At(5, 30, 1)), timeline.Changes[^1]);
    }

    [Fact]
    public void ProgressAt_IsZeroAtSunset_OneAtSunrise_AndNullOutside()
    {
        TonightTimeline timeline = TonightTimeline.Build(SyntheticNight());

        Assert.Equal(0.0, timeline.ProgressAt(At(18, 0)));
        Assert.Equal(1.0, timeline.ProgressAt(At(5, 30, 1)));
        Assert.Null(timeline.ProgressAt(At(17, 0)));
        Assert.Null(timeline.ProgressAt(At(6, 0, 1)));
    }

    [Fact]
    public void NextChangeAfter_ReturnsFirstChangeStrictlyAfterInstant()
    {
        TonightTimeline timeline = TonightTimeline.Build(SyntheticNight());

        TonightPhaseChange? next = timeline.NextChangeAfter(At(19, 10));
        Assert.Equal(At(20, 0), next!.AtUtc);
        Assert.Equal(SkyPhase.Night, next.To);

        Assert.Null(timeline.NextChangeAfter(At(5, 30, 1)));
    }

    [Fact]
    public void Build_WithoutSunriseInTheNight_IsUnavailable()
    {
        // Notte polare: il Sole non sale mai, nessuna transizione Giorno.
        var bands = new List<PhaseBand> { new(SkyPhase.Night, At(12, 0), At(12, 0, 1)) };

        TonightTimeline timeline = TonightTimeline.Build(bands);

        Assert.False(timeline.Available);
        Assert.Null(timeline.Sunset);
        Assert.Null(timeline.Sunrise);
        Assert.Empty(timeline.Segments);
    }

    [Fact]
    public void Build_WithAllDay_IsUnavailable()
    {
        // Sole sempre sopra l'orizzonte.
        var bands = new List<PhaseBand> { new(SkyPhase.Day, At(12, 0), At(12, 0, 1)) };

        Assert.False(TonightTimeline.Build(bands).Available);
    }

    [Fact]
    public void WithSamples_KeepsWindowAndAddsSamples()
    {
        TonightTimeline timeline = TonightTimeline.Build(SyntheticNight());
        var samples = new List<TonightSample> { new(At(18, 0), -0.8, 30.0) };

        TonightTimeline withSamples = timeline.WithSamples(samples);

        Assert.Single(withSamples.Samples);
        Assert.Equal(timeline.Sunset, withSamples.Sunset);
        Assert.Empty(timeline.Samples);
    }
}
