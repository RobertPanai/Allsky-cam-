using AllSky.Core;
using AllSky.Core.Nights;
using Xunit;

namespace AllSky.Core.Tests.Nights;

public sealed class NightRowFormatterTests
{
    private static NightSummary Night(int frames, NightTimelapseState timelapse = NightTimelapseState.None) =>
        new("2026-10-21", frames, null, null, true, true, timelapse);

    [Fact]
    public void Date_IsLongFormInTheChosenLanguage()
    {
        Assert.Equal("21 ottobre 2026", NightRowFormatter.FormatDate("2026-10-21", DesktopTexts.Italian.Culture));
        Assert.Equal("21 October 2026", NightRowFormatter.FormatDate("2026-10-21", DesktopTexts.English.Culture));
    }

    [Fact]
    public void Date_InvalidIdentifier_IsReturnedUnchanged()
    {
        Assert.Equal("cartella-strana", NightRowFormatter.FormatDate("cartella-strana", DesktopTexts.Italian.Culture));
        Assert.Equal("2026-13-40", NightRowFormatter.FormatDate("2026-13-40", DesktopTexts.Italian.Culture));
    }

    [Fact]
    public void Row_WithoutFrames_ShowsZeroFramesAndNoSpan()
    {
        NightRow row = NightRowFormatter.Format(Night(0), DesktopTexts.Italian);

        Assert.Equal("2026-10-21", row.Id);
        Assert.Equal("21 ottobre 2026", row.Title);
        Assert.Equal("0 frame", row.Subtitle);
    }

    [Fact]
    public void Row_WithFrames_ShowsCountAndLocalSpan()
    {
        var first = new DateTimeOffset(2026, 10, 21, 20, 30, 0, TimeSpan.Zero);
        var last = new DateTimeOffset(2026, 10, 22, 5, 45, 0, TimeSpan.Zero);
        var night = new NightSummary("2026-10-21", 120, first, last, true, true, NightTimelapseState.Ready);

        NightRow row = NightRowFormatter.Format(night, DesktopTexts.English);

        string expectedSpan = first.ToLocalTime().ToString("HH:mm") + "–" + last.ToLocalTime().ToString("HH:mm");
        Assert.Equal("120 frames · " + expectedSpan, row.Subtitle);
        Assert.Equal(NightTimelapseState.Ready, row.Timelapse);
        Assert.Equal(DesktopTexts.English.NightsTimelapseReady, row.TimelapseLabel);
    }

    [Fact]
    public void Row_WithOnlyOneEnd_OmitsSpan()
    {
        var night = new NightSummary("2026-10-21", 3, DateTimeOffset.UtcNow, null, false, false, NightTimelapseState.None);

        Assert.Equal("3 frame", NightRowFormatter.Format(night, DesktopTexts.Italian).Subtitle);
    }

    [Theory]
    [InlineData(NightTimelapseState.None)]
    [InlineData(NightTimelapseState.Ready)]
    [InlineData(NightTimelapseState.Generating)]
    [InlineData(NightTimelapseState.Failed)]
    [InlineData(NightTimelapseState.Unknown)]
    public void TimelapseLabel_IsNeverEmpty_InBothLanguages(NightTimelapseState state)
    {
        Assert.False(string.IsNullOrWhiteSpace(NightRowFormatter.TimelapseLabel(state, DesktopTexts.Italian)));
        Assert.False(string.IsNullOrWhiteSpace(NightRowFormatter.TimelapseLabel(state, DesktopTexts.English)));
    }

    [Fact]
    public void TimelapseLabel_DistinguishesEveryState()
    {
        string[] labels =
        [
            NightRowFormatter.TimelapseLabel(NightTimelapseState.None, DesktopTexts.Italian),
            NightRowFormatter.TimelapseLabel(NightTimelapseState.Ready, DesktopTexts.Italian),
            NightRowFormatter.TimelapseLabel(NightTimelapseState.Generating, DesktopTexts.Italian),
            NightRowFormatter.TimelapseLabel(NightTimelapseState.Failed, DesktopTexts.Italian),
            NightRowFormatter.TimelapseLabel(NightTimelapseState.Unknown, DesktopTexts.Italian),
        ];

        Assert.Equal(labels.Length, labels.Distinct().Count());
    }
}

public sealed class NightTimelapseStatesTests
{
    [Theory]
    [InlineData("none", NightTimelapseState.None)]
    [InlineData("ready", NightTimelapseState.Ready)]
    [InlineData("generating", NightTimelapseState.Generating)]
    [InlineData("failed", NightTimelapseState.Failed)]
    [InlineData("qualcosa", NightTimelapseState.Unknown)]
    [InlineData("", NightTimelapseState.Unknown)]
    public void FromWire_MapsKnownValuesAndKeepsUnknownAsUnknown(string wire, NightTimelapseState expected)
    {
        Assert.Equal(expected, NightTimelapseStates.FromWire(wire));
    }

    [Fact]
    public void FromWire_Null_IsUnknown_NotNone()
    {
        Assert.Equal(NightTimelapseState.Unknown, NightTimelapseStates.FromWire(null));
    }
}
