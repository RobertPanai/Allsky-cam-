using AllSky.Core.Passes;
using AllSky.Core.Tonight;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class PassFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    private static PassForecastItem Pass(
        DateTimeOffset start,
        double startAz = 315,
        double endAz = 135,
        double lit = 0.5,
        bool visible = false,
        string target = "ISS (ZARYA)") => new(
            target,
            25544,
            start,
            start + TimeSpan.FromMinutes(4),
            start + TimeSpan.FromMinutes(8),
            52.4,
            startAz,
            endAz,
            lit,
            visible,
            false,
            false);

    [Theory]
    [InlineData(0, CompassPoint.N)]
    [InlineData(22.4, CompassPoint.N)]
    [InlineData(22.5, CompassPoint.NE)]
    [InlineData(90, CompassPoint.E)]
    [InlineData(180, CompassPoint.S)]
    [InlineData(270, CompassPoint.W)]
    [InlineData(315, CompassPoint.NW)]
    [InlineData(359.9, CompassPoint.N)]
    [InlineData(-10, CompassPoint.N)]
    [InlineData(95, CompassPoint.E)]
    public void Compass_MapsAzimuthToEightSectors(double azimuth, CompassPoint expected)
    {
        Assert.Equal(expected, PassFormatter.Compass(azimuth));
    }

    [Fact]
    public void Compass_RejectsNonFiniteAzimuth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PassFormatter.Compass(double.NaN));
    }

    [Fact]
    public void Direction_UsesLanguageNames()
    {
        PassForecastItem pass = Pass(Now);

        Assert.Equal("da NO a SE", PassFormatter.Direction(pass, DesktopTexts.Italian));
        Assert.Equal("from NW to SE", PassFormatter.Direction(pass, DesktopTexts.English));
    }

    [Fact]
    public void Light_DistinguishesVisibleSunlitAndShadow()
    {
        Assert.Equal(DesktopTexts.Italian.PassLightVisible, PassFormatter.Light(Pass(Now, visible: true), DesktopTexts.Italian));
        Assert.Equal(DesktopTexts.Italian.PassLightLit, PassFormatter.Light(Pass(Now, lit: 0.3), DesktopTexts.Italian));
        Assert.Equal(DesktopTexts.Italian.PassLightShadow, PassFormatter.Light(Pass(Now, lit: 0.0), DesktopTexts.Italian));
    }

    [Fact]
    public void Times_ShowsStartCulminationAndEnd()
    {
        PassForecastItem pass = Pass(new DateTimeOffset(2026, 10, 22, 20, 3, 0, TimeSpan.Zero));

        string text = PassFormatter.Times(pass, TimeZoneInfo.Utc, DesktopTexts.Italian);

        Assert.Equal("inizio 20:03 · culmine 20:07 · fine 20:11", text);
    }

    [Fact]
    public void Row_FutureAndInProgress()
    {
        DateTimeOffset start = Now + TimeSpan.FromHours(2) + TimeSpan.FromMinutes(14);
        PassRowData future = PassFormatter.Row(Pass(start), Now, TimeZoneInfo.Utc, DesktopTexts.Italian);
        PassRowData current = PassFormatter.Row(Pass(Now - TimeSpan.FromMinutes(1)), Now, TimeZoneInfo.Utc, DesktopTexts.Italian);

        Assert.False(future.InProgress);
        Assert.Equal(TonightFormatter.FormatCountdown(Now, start, DesktopTexts.Italian), future.When);
        Assert.True(current.InProgress);
        Assert.Equal(DesktopTexts.Italian.PassInProgress, current.When);
        Assert.Contains("alt. max 52°", current.Details);
    }

    [Fact]
    public void Panel_DisabledShowsHintAndNoRows()
    {
        var snapshot = new PassForecastSnapshot(PassForecastStatus.Disabled, null, null, Array.Empty<PassForecastItem>(), null, null);

        PassPanelData panel = PassFormatter.Panel(snapshot, Now, TimeZoneInfo.Utc, DesktopTexts.Italian);

        Assert.Equal(DesktopTexts.Italian.PassStatusDisabled, panel.Hint);
        Assert.Empty(panel.Rows);
    }

    [Fact]
    public void Panel_ReadyWithoutPassesSaysSo()
    {
        var snapshot = new PassForecastSnapshot(PassForecastStatus.Ready, null, Now, Array.Empty<PassForecastItem>(), null, null);

        PassPanelData panel = PassFormatter.Panel(snapshot, Now, TimeZoneInfo.Utc, DesktopTexts.English);

        Assert.Equal(DesktopTexts.English.PassNoneInWindow, panel.Hint);
    }

    [Fact]
    public void Panel_BlockedKeepsCachedRowsAndShowsMessage()
    {
        var passes = Enumerable.Range(1, 7).Select(i => Pass(Now + TimeSpan.FromHours(i))).ToList();
        var snapshot = new PassForecastSnapshot(PassForecastStatus.Blocked, "HTTP 503", Now, passes, null, passes[0]);

        PassPanelData panel = PassFormatter.Panel(snapshot, Now, TimeZoneInfo.Utc, DesktopTexts.Italian);

        Assert.Contains("HTTP 503", panel.Hint);
        Assert.Equal(PassFormatter.DefaultMaxRows, panel.Rows.Count);
    }

    [Fact]
    public void Panel_FailedShowsTechnicalMessage()
    {
        var snapshot = new PassForecastSnapshot(PassForecastStatus.Failed, "TLE non validi", Now, Array.Empty<PassForecastItem>(), null, null);

        PassPanelData panel = PassFormatter.Panel(snapshot, Now, TimeZoneInfo.Utc, DesktopTexts.Italian);

        Assert.Contains("TLE non validi", panel.Hint);
    }
}
