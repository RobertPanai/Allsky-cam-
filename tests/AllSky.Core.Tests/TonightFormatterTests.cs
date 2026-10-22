using System.Globalization;
using AllSky.Astro;
using AllSky.Core;
using AllSky.Core.Tonight;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class TonightFormatterTests
{
    [Fact]
    public void FormatDuration_UsesHoursAndMinutes_InBothLanguages()
    {
        var span = TimeSpan.FromMinutes(134);

        Assert.Equal("2 h 14 min", TonightFormatter.FormatDuration(span, DesktopTexts.Italian));
        Assert.Equal("2 h 14 min", TonightFormatter.FormatDuration(span, DesktopTexts.English));
    }

    [Fact]
    public void FormatDuration_UnderAnHour_UsesMinutes()
    {
        Assert.Equal("14 min", TonightFormatter.FormatDuration(TimeSpan.FromMinutes(14.5), DesktopTexts.Italian));
    }

    [Fact]
    public void FormatDuration_UnderAMinute_AndNonPositive()
    {
        Assert.Equal(DesktopTexts.Italian.TonightCountdownLessThanMinute,
            TonightFormatter.FormatDuration(TimeSpan.FromSeconds(30), DesktopTexts.Italian));
        Assert.Equal(DesktopTexts.Italian.TonightNow,
            TonightFormatter.FormatDuration(TimeSpan.Zero, DesktopTexts.Italian));
        Assert.Equal(DesktopTexts.Italian.TonightNow,
            TonightFormatter.FormatDuration(TimeSpan.FromMinutes(-3), DesktopTexts.Italian));
    }

    [Fact]
    public void FormatCountdown_PrefixesTheDuration()
    {
        var now = new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

        string text = TonightFormatter.FormatCountdown(now, now.AddMinutes(134), DesktopTexts.Italian);

        Assert.Equal("tra 2 h 14 min", text);
    }

    [Fact]
    public void FormatAltitude_HasExplicitSignAndOneDecimal()
    {
        Assert.Equal("+12.3°", TonightFormatter.FormatAltitude(12.34, CultureInfo.InvariantCulture));
        Assert.Equal("-0.8°", TonightFormatter.FormatAltitude(-0.8, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatClock_ShowsLocalTimeWithoutSeconds()
    {
        TimeZoneInfo rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        var utc = new DateTimeOffset(2025, 5, 21, 19, 58, 40, TimeSpan.Zero);

        Assert.Equal("21:58", TonightFormatter.FormatClock(utc, rome, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatIllumination_IsWholePercent()
    {
        Assert.Equal("47%", TonightFormatter.FormatIllumination(0.466, CultureInfo.InvariantCulture));
        Assert.Equal("100%", TonightFormatter.FormatIllumination(1.0, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PhaseAndMoonNames_AreNeverEmpty()
    {
        foreach (SkyPhase phase in Enum.GetValues<SkyPhase>())
        {
            Assert.False(string.IsNullOrWhiteSpace(TonightFormatter.PhaseName(phase, DesktopTexts.Italian)));
            Assert.False(string.IsNullOrWhiteSpace(TonightFormatter.PhaseName(phase, DesktopTexts.English)));
        }

        foreach (MoonPhaseName name in Enum.GetValues<MoonPhaseName>())
        {
            Assert.False(string.IsNullOrWhiteSpace(TonightFormatter.MoonName(name, DesktopTexts.Italian)));
            Assert.False(string.IsNullOrWhiteSpace(TonightFormatter.MoonName(name, DesktopTexts.English)));
        }
    }

    [Fact]
    public void CaptureLines_WithoutFrameOrCamera_ShowsProblemsWithText()
    {
        var input = new TonightCaptureInput("idle", false, string.Empty, null, null, null);
        var now = new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

        IReadOnlyList<TonightStatusLine> lines = TonightFormatter.CaptureLines(input, now, DesktopTexts.Italian);

        TonightStatusLine camera = Assert.Single(lines, l => l.Label == DesktopTexts.Italian.CameraLabel);
        Assert.Equal(DesktopTexts.Italian.CameraNotConnected, camera.Value);
        Assert.True(camera.Problem);
        TonightStatusLine frame = Assert.Single(lines, l => l.Label == DesktopTexts.Italian.LastFrameLabel);
        Assert.Equal(DesktopTexts.Italian.NoFrameYet, frame.Value);
        Assert.DoesNotContain(lines, l => l.Label == DesktopTexts.Italian.ErrorLabel);
    }

    [Fact]
    public void CaptureLines_WithError_AddsErrorLine()
    {
        var now = new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);
        var input = new TonightCaptureInput(
            "error", true, "Simulatore", "20261022T195000Z", now.AddMinutes(-10), "Camera non risponde.");

        IReadOnlyList<TonightStatusLine> lines = TonightFormatter.CaptureLines(input, now, DesktopTexts.English);

        TonightStatusLine error = Assert.Single(lines, l => l.Label == DesktopTexts.English.ErrorLabel);
        Assert.Equal("Camera non risponde.", error.Value);
        Assert.True(error.Problem);
        TonightStatusLine frame = Assert.Single(lines, l => l.Label == DesktopTexts.English.LastFrameLabel);
        Assert.StartsWith("10 min ago", frame.Value);
    }
}
