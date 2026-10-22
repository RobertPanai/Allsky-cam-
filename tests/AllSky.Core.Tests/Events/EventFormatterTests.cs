using System.Globalization;
using AllSky.Core;
using AllSky.Core.Events;
using Xunit;

namespace AllSky.Core.Tests.Events;

public sealed class EventFormatterTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 19, 21, 4, 33, TimeSpan.Zero);

    private static EventDetail Meteor(SkyPoint? start = null, SkyPoint? end = null) => new(
        "2026-10-19",
        "meteor-1",
        EventCategory.Meteor,
        Instant,
        EventClassKind.Meteor,
        3.25,
        61.5,
        120,
        900.5,
        EventLabelChoice.None,
        start,
        end,
        CropPath: null,
        FramePath: null,
        ClipPath: null,
        CompositePath: null,
        Transit: null);

    private static EventDetail Transit() => Meteor() with
    {
        Category = EventCategory.Transit,
        Class = EventClassKind.Satellite,
        Transit = new TransitInfo(
            "ISS (ZARYA)",
            25544,
            Instant,
            Instant.AddMinutes(3),
            Instant.AddMinutes(6),
            62.5,
            0.8,
            TransitOutcome.Identified,
            12),
    };

    [Fact]
    public void Names_AreFilledAndDifferentInBothLanguages()
    {
        foreach (DesktopTexts texts in new[] { DesktopTexts.Italian, DesktopTexts.English })
        {
            var names = new[]
            {
                EventFormatter.CategoryName(EventCategory.Meteor, texts),
                EventFormatter.CategoryName(EventCategory.Bolide, texts),
                EventFormatter.CategoryName(EventCategory.Transit, texts),
            };
            Assert.Equal(3, names.Distinct().Count());
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        }

        Assert.Equal("Meteora", EventFormatter.CategoryName(EventCategory.Meteor, DesktopTexts.Italian));
        Assert.Equal("Meteor", EventFormatter.CategoryName(EventCategory.Meteor, DesktopTexts.English));
    }

    [Fact]
    public void LabelAndOutcomeNames_CoverEveryValue()
    {
        Assert.Equal(DesktopTexts.Italian.EventsLabelConfirmed, EventFormatter.LabelName(EventLabelChoice.Confirmed, DesktopTexts.Italian));
        Assert.Equal(DesktopTexts.English.EventsLabelFalsePositive, EventFormatter.LabelName(EventLabelChoice.FalsePositive, DesktopTexts.English));
        Assert.Equal(DesktopTexts.Italian.EventsLabelNone, EventFormatter.LabelName(EventLabelChoice.None, DesktopTexts.Italian));

        Assert.Equal(DesktopTexts.Italian.EventsVerdictIdentified, EventFormatter.OutcomeName(TransitOutcome.Identified, DesktopTexts.Italian));
        Assert.Equal(DesktopTexts.English.EventsVerdictNotIdentified, EventFormatter.OutcomeName(TransitOutcome.NotIdentified, DesktopTexts.English));
        Assert.Equal(DesktopTexts.Italian.EventsVerdictPredicted, EventFormatter.OutcomeName(TransitOutcome.PredictedNotVerified, DesktopTexts.Italian));
    }

    [Fact]
    public void Score_UsesTwoDecimalsInTheLanguageCulture()
    {
        Assert.Equal("3.25", EventFormatter.FormatScore(3.25, DesktopTexts.English));
        Assert.Equal(3.25.ToString("0.00", new CultureInfo("it-IT")), EventFormatter.FormatScore(3.25, DesktopTexts.Italian));
        Assert.Equal(EventFormatter.Missing, EventFormatter.FormatScore(double.NaN, DesktopTexts.English));
    }

    [Fact]
    public void Length_HasOneDecimalAndUnit()
    {
        Assert.Equal("61.5 px", EventFormatter.FormatLength(61.5, DesktopTexts.English));
        Assert.Equal(EventFormatter.Missing, EventFormatter.FormatLength(double.PositiveInfinity, DesktopTexts.English));
    }

    [Fact]
    public void SkyPoint_IsAltAndAzWithDegrees_OrMissing()
    {
        Assert.Equal("41.3° / 183.5°", EventFormatter.FormatSkyPoint(new SkyPoint(41.27, 183.46), DesktopTexts.English));
        Assert.Equal(EventFormatter.Missing, EventFormatter.FormatSkyPoint(null, DesktopTexts.English));
        Assert.Equal(EventFormatter.Missing, EventFormatter.FormatSkyPoint(new SkyPoint(double.NaN, 1), DesktopTexts.English));
    }

    [Fact]
    public void ListCaption_ShowsTypeLocalTimeAndScore()
    {
        var item = new EventListItem("2026-10-19", "meteor-1", EventCategory.Meteor, Instant, 3.25, EventLabelChoice.None, null);

        string caption = EventFormatter.ListCaption(item, DesktopTexts.English);

        string time = Instant.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Assert.Equal($"Meteor · {time} · 3.25", caption);
    }

    [Fact]
    public void DataRows_ForMeteorWithoutCalibration_MarksSkyPositionsMissing()
    {
        IReadOnlyList<EventDataRow> rows = EventFormatter.DataRows(Meteor(), DesktopTexts.English);

        Assert.Equal(8, rows.Count);
        Assert.Equal("Meteor", rows.Single(r => r.Label == DesktopTexts.English.EventsDataClass).Value);
        Assert.Equal("61.5 px", rows.Single(r => r.Label == DesktopTexts.English.EventsDataLength).Value);

        EventDataRow start = rows.Single(r => r.Label == DesktopTexts.English.EventsDataStart);
        Assert.Equal(EventFormatter.Missing, start.Value);
        Assert.Equal(DesktopTexts.English.EventsNoSkyPosition, start.Tooltip);
    }

    [Fact]
    public void DataRows_WithCalibration_ShowsSkyPositionsWithoutTooltip()
    {
        IReadOnlyList<EventDataRow> rows = EventFormatter.DataRows(
            Meteor(new SkyPoint(41.25, 183.5), new SkyPoint(38.0, 190.1)),
            DesktopTexts.English);

        EventDataRow start = rows.Single(r => r.Label == DesktopTexts.English.EventsDataStart);
        Assert.NotEqual(EventFormatter.Missing, start.Value);
        Assert.Null(start.Tooltip);
    }

    [Fact]
    public void DataRows_ForTransit_AddsSatelliteRows()
    {
        IReadOnlyList<EventDataRow> rows = EventFormatter.DataRows(Transit(), DesktopTexts.English);

        Assert.Equal(15, rows.Count);
        Assert.Equal("ISS (ZARYA)", rows.Single(r => r.Label == DesktopTexts.English.EventsDataSatellite).Value);
        Assert.Equal("25544", rows.Single(r => r.Label == DesktopTexts.English.EventsDataNorad).Value);
        Assert.Equal("62.5°", rows.Single(r => r.Label == DesktopTexts.English.EventsDataMaxElevation).Value);
        Assert.Equal(DesktopTexts.English.EventsVerdictIdentified, rows.Single(r => r.Label == DesktopTexts.English.EventsDataVerdict).Value);
    }

    [Fact]
    public void DataRows_TimeTooltipIsUtc()
    {
        IReadOnlyList<EventDataRow> rows = EventFormatter.DataRows(Meteor(), DesktopTexts.English);

        EventDataRow time = rows.Single(r => r.Label == DesktopTexts.English.EventsDataTime);
        Assert.Equal("2026-10-19 21:04:33 UTC", time.Tooltip);
    }

    [Fact]
    public void PreferredImage_IsCompositeForTransits_AndCropForMeteors()
    {
        EventDetail meteor = Meteor() with { CropPath = "/d/crop.jpg", FramePath = "/d/frame.jpg" };
        EventDetail transit = Transit() with { CompositePath = "/d/composite.jpg", FramePath = "/d/frame.jpg" };

        Assert.Equal("/d/crop.jpg", EventFormatter.PreferredImagePath(meteor));
        Assert.Equal("/d/composite.jpg", EventFormatter.PreferredImagePath(transit));
    }

    [Fact]
    public void PreferredImage_FallsBackToFrame_AndIsNullWithoutImages()
    {
        EventDetail meteorNoCrop = Meteor() with { FramePath = "/d/frame.jpg" };
        EventDetail transitNoComposite = Transit() with { FramePath = "/d/frame.jpg" };

        Assert.Equal("/d/frame.jpg", EventFormatter.PreferredImagePath(meteorNoCrop));
        Assert.Equal("/d/frame.jpg", EventFormatter.PreferredImagePath(transitNoComposite));
        Assert.Null(EventFormatter.PreferredImagePath(Meteor()));
    }

    [Fact]
    public void Names_RejectNullTexts()
    {
        Assert.Throws<ArgumentNullException>(() => EventFormatter.CategoryName(EventCategory.Meteor, null!));
        Assert.Throws<ArgumentNullException>(() => EventFormatter.DataRows(Meteor(), null!));
    }
}
