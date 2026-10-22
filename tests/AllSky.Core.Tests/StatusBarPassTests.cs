using AllSky.Core.Shell;
using AllSky.Core.Tonight;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class StatusBarPassTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    private static StatusBarSnapshot Snapshot(string? target = null, DateTimeOffset? start = null, bool inProgress = false) => new(
        Phase: null,
        SunAltitudeDeg: null,
        MoonAltitudeDeg: null,
        CameraConnected: null,
        CameraName: null,
        LastFrameTimestamp: null,
        DiskFreeGb: null,
        Publication: PublicationStatus.Unknown,
        PassTarget: target,
        PassStartUtc: start,
        PassInProgress: inProgress);

    [Fact]
    public void NoPass_ShowsNotAvailable()
    {
        StatusBarText text = StatusBarFormatter.Format(Snapshot(), DesktopTexts.Italian, Now);

        Assert.NotNull(text.NextPass);
        Assert.False(text.NextPass!.Available);
        Assert.Equal("n/d", text.NextPass.Value);
        Assert.Equal(DesktopTexts.Italian.PassStatusLabel, text.NextPass.Label);
    }

    [Fact]
    public void FuturePass_ShowsTargetAndCountdown()
    {
        DateTimeOffset start = Now + TimeSpan.FromHours(2) + TimeSpan.FromMinutes(14);

        StatusBarText text = StatusBarFormatter.Format(Snapshot("ISS (ZARYA)", start), DesktopTexts.Italian, Now);

        Assert.True(text.NextPass!.Available);
        string countdown = TonightFormatter.FormatCountdown(Now, start, DesktopTexts.Italian);
        Assert.Equal(string.Format(DesktopTexts.Italian.Culture, DesktopTexts.Italian.PassNextValue, "ISS (ZARYA)", countdown), text.NextPass.Value);
        Assert.StartsWith("ISS (ZARYA) ", text.NextPass.Value);
    }

    [Fact]
    public void PassInProgress_ShowsInProgress()
    {
        StatusBarText text = StatusBarFormatter.Format(
            Snapshot("ISS (ZARYA)", Now - TimeSpan.FromMinutes(2), inProgress: true), DesktopTexts.English, Now);

        Assert.Equal("ISS (ZARYA) in progress", text.NextPass!.Value);
    }

    [Fact]
    public void Format_WithoutNowStillWorks()
    {
        StatusBarText text = StatusBarFormatter.Format(Snapshot("ISS (ZARYA)", DateTimeOffset.UtcNow.AddHours(3)), DesktopTexts.Italian);

        Assert.True(text.NextPass!.Available);
    }
}
