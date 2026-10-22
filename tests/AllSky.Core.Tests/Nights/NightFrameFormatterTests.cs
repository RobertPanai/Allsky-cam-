using AllSky.Core;
using AllSky.Core.Nights;
using Xunit;

namespace AllSky.Core.Tests.Nights;

public sealed class NightFrameFormatterTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 21, 21, 14, 3, TimeSpan.Zero);

    [Fact]
    public void Position_IsOneBasedAndLocalised()
    {
        NightFrameText italian = NightFrameFormatter.Format(0, 480, Instant, "night", DesktopTexts.Italian);
        NightFrameText english = NightFrameFormatter.Format(479, 480, Instant, "night", DesktopTexts.English);

        Assert.Equal("Frame 1 di 480", italian.Position);
        Assert.Equal("Frame 480 of 480", english.Position);
    }

    [Fact]
    public void Time_IsLocalClock_AndTooltipIsUtc()
    {
        NightFrameText text = NightFrameFormatter.Format(3, 10, Instant, "night", DesktopTexts.Italian);

        Assert.Equal(Instant.ToLocalTime().ToString("HH:mm:ss"), text.Time);
        Assert.Equal("Istante UTC: 2026-10-21 21:14:03", text.TimeTooltip);
    }

    [Theory]
    [InlineData("day", "giorno")]
    [InlineData("twilight", "crepuscolo")]
    [InlineData("night", "notte")]
    public void Phase_IsNamedInTheChosenLanguage(string phase, string italian)
    {
        Assert.Equal(italian, NightFrameFormatter.Format(0, 1, Instant, phase, DesktopTexts.Italian).Phase);
    }

    [Fact]
    public void UnknownPhase_ShowsDash_NotAnEmptyString()
    {
        Assert.Equal("—", NightFrameFormatter.Format(0, 1, Instant, "boh", DesktopTexts.English).Phase);
    }
}
