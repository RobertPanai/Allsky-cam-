using System.Globalization;
using AllSky.Core.Shell;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class StatusBarFormatterTests
{
    private static StatusBarSnapshot Empty() => new(
        Phase: null,
        SunAltitudeDeg: null,
        MoonAltitudeDeg: null,
        CameraConnected: null,
        CameraName: null,
        LastFrameTimestamp: null,
        DiskFreeGb: null,
        Publication: PublicationStatus.Unknown);

    [Fact]
    public void MissingData_ShowsNotAvailableInItalian()
    {
        StatusBarText text = StatusBarFormatter.Format(Empty(), DesktopTexts.Italian);

        foreach (StatusBarItem item in Items(text))
        {
            Assert.False(item.Available, item.Label);
            Assert.Equal("n/d", item.Value);
            Assert.Equal(DesktopTexts.Italian.NotAvailableTooltip, item.Tooltip);
        }
        Assert.Equal(StatusPhase.Unknown, text.PhaseKind);
    }

    [Fact]
    public void MissingData_ShowsNotAvailableInEnglish()
    {
        StatusBarText text = StatusBarFormatter.Format(Empty(), DesktopTexts.English);

        Assert.All(Items(text), item => Assert.Equal("n/a", item.Value));
    }

    [Fact]
    public void MissingData_NeverShowsZero()
    {
        StatusBarText text = StatusBarFormatter.Format(Empty(), DesktopTexts.Italian);

        Assert.DoesNotContain("0", text.Sun.Value);
        Assert.DoesNotContain("0", text.Disk.Value);
    }

    [Fact]
    public void Altitude_UsesOneDecimalSignAndCultureSeparator()
    {
        CultureInfo culture = DesktopTexts.Italian.Culture;
        StatusBarText text = StatusBarFormatter.Format(
            Empty() with { SunAltitudeDeg = -12.34, MoonAltitudeDeg = 5.0 }, DesktopTexts.Italian);

        Assert.True(text.Sun.Available);
        Assert.Equal((-12.34).ToString("0.0", culture) + "°", text.Sun.Value);
        Assert.Equal("+" + 5.0.ToString("0.0", culture) + "°", text.Moon.Value);
    }

    [Fact]
    public void Altitude_NonFiniteIsTreatedAsMissing()
    {
        StatusBarText text = StatusBarFormatter.Format(
            Empty() with { SunAltitudeDeg = double.NaN }, DesktopTexts.Italian);

        Assert.False(text.Sun.Available);
        Assert.Equal("n/d", text.Sun.Value);
    }

    [Theory]
    [InlineData("day", StatusPhase.Day)]
    [InlineData("twilight", StatusPhase.Twilight)]
    [InlineData("night", StatusPhase.Night)]
    [InlineData("qualcosa", StatusPhase.Unknown)]
    [InlineData(null, StatusPhase.Unknown)]
    public void Phase_ParsesServiceValues(string? raw, StatusPhase expected)
    {
        Assert.Equal(expected, StatusBarFormatter.ParsePhase(raw));
    }

    [Fact]
    public void Phase_KnownValueIsNamedInTheCurrentLanguage()
    {
        StatusBarText text = StatusBarFormatter.Format(Empty() with { Phase = "twilight" }, DesktopTexts.English);

        Assert.True(text.Phase.Available);
        Assert.Equal(StatusPhase.Twilight, text.PhaseKind);
        Assert.Equal(DesktopTexts.English.PhaseTwilight, text.Phase.Value);
    }

    [Fact]
    public void Camera_NotConnected_IsKnownButNotConnected()
    {
        StatusBarText text = StatusBarFormatter.Format(
            Empty() with { CameraConnected = false, CameraName = "Simulatore" }, DesktopTexts.Italian);

        Assert.True(text.Camera.Available);
        Assert.Equal(DesktopTexts.Italian.CameraNotConnected, text.Camera.Value);
    }

    [Fact]
    public void Camera_ConnectedWithoutName_UsesGenericText()
    {
        StatusBarText text = StatusBarFormatter.Format(
            Empty() with { CameraConnected = true, CameraName = "  " }, DesktopTexts.English);

        Assert.Equal(DesktopTexts.English.CameraConnected, text.Camera.Value);
    }

    [Fact]
    public void Camera_ConnectedWithName_ShowsName()
    {
        StatusBarText text = StatusBarFormatter.Format(
            Empty() with { CameraConnected = true, CameraName = "ASI178MM" }, DesktopTexts.Italian);

        Assert.Equal("ASI178MM", text.Camera.Value);
    }

    [Fact]
    public void Disk_ShowsOneDecimalInGigabytes()
    {
        CultureInfo culture = DesktopTexts.Italian.Culture;
        StatusBarText text = StatusBarFormatter.Format(Empty() with { DiskFreeGb = 42.37 }, DesktopTexts.Italian);

        Assert.True(text.Disk.Available);
        Assert.Equal(string.Format(culture, DesktopTexts.Italian.DiskFree, 42.37.ToString("0.0", culture)), text.Disk.Value);
    }

    [Fact]
    public void Disk_NegativeIsMissing()
    {
        StatusBarText text = StatusBarFormatter.Format(Empty() with { DiskFreeGb = -1 }, DesktopTexts.Italian);

        Assert.False(text.Disk.Available);
    }

    [Theory]
    [InlineData(PublicationStatus.Ok)]
    [InlineData(PublicationStatus.Pending)]
    [InlineData(PublicationStatus.Error)]
    public void Publications_KnownStatesAreShown(PublicationStatus status)
    {
        StatusBarText text = StatusBarFormatter.Format(Empty() with { Publication = status }, DesktopTexts.Italian);

        Assert.True(text.Publications.Available);
        Assert.NotEqual("n/d", text.Publications.Value);
    }

    [Fact]
    public void LastFrame_ShowsLocalTimeOnly()
    {
        var timestamp = new DateTimeOffset(2026, 10, 21, 12, 30, 5, TimeSpan.Zero);
        StatusBarText text = StatusBarFormatter.Format(
            Empty() with { LastFrameTimestamp = timestamp }, DesktopTexts.English);

        Assert.True(text.LastFrame.Available);
        Assert.Equal(timestamp.ToLocalTime().ToString("HH:mm:ss", DesktopTexts.English.Culture), text.LastFrame.Value);
    }

    [Fact]
    public void Format_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => StatusBarFormatter.Format(null!, DesktopTexts.Italian));
        Assert.Throws<ArgumentNullException>(() => StatusBarFormatter.Format(Empty(), null!));
    }

    private static IEnumerable<StatusBarItem> Items(StatusBarText text) =>
    [
        text.Camera, text.Phase, text.Sun, text.Moon, text.LastFrame, text.Disk, text.Publications,
    ];
}
