using AllSky.Archive;
using AllSky.Core;
using AllSky.YouTube;
using AllSky.Host;
using Xunit;

namespace AllSky.Host.Tests.YouTube;

public sealed class NightArchiveLatestTimelapseProviderTests
{
    /// <summary>Ora UTC di una notte in corso: 23 ottobre 2026 alle 08:00 UTC. Fuso UTC: la notte in corso e' quella del 22.</summary>
    private static readonly DateTimeOffset MorningOf23 = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    private static AppConfig ConfigFor(string folder) => new()
    {
        Site = new AppConfig.SiteConfig { Timezone = "UTC" },
        Storage = new AppConfig.StorageConfig { Folder = folder },
    };

    [Fact]
    public void SceglieLUltimaNottePronta_Esclude_LaNottaInCorso()
    {
        using var folder = new TempFolder();
        var clock = new FixedCoreClock(MorningOf23);
        var archive = new NightArchive(folder.Path, clock, TimeZoneInfo.Utc);
        NightFixtures.AddNight(archive, "2026-10-20", withTimelapse: true);
        NightFixtures.AddNight(archive, "2026-10-21", withTimelapse: true);
        NightFixtures.AddNight(archive, "2026-10-22", withTimelapse: true); // in corso: non deve essere scelta

        var provider = new NightArchiveLatestTimelapseProvider(() => ConfigFor(folder.Path), clock);
        LatestTimelapse? latest = provider.GetLatestCompleted();

        Assert.NotNull(latest);
        Assert.Equal("2026-10-21", latest!.NightId);
        Assert.Equal(2, latest.Frames);
        Assert.EndsWith("timelapse.mp4", latest.FilePath);
        Assert.Equal(0, latest.Meteors);
    }

    [Fact]
    public void SaltaLeNottiSenzaTimelapseProntoOInGenerazione()
    {
        using var folder = new TempFolder();
        var clock = new FixedCoreClock(MorningOf23);
        var archive = new NightArchive(folder.Path, clock, TimeZoneInfo.Utc);
        NightFixtures.AddNight(archive, "2026-10-19", withTimelapse: true);
        NightFixtures.AddNight(archive, "2026-10-20", withTimelapse: true, state: TimelapseState.Generating);
        NightFixtures.AddNight(archive, "2026-10-21", withTimelapse: false);

        var provider = new NightArchiveLatestTimelapseProvider(() => ConfigFor(folder.Path), clock);
        LatestTimelapse? latest = provider.GetLatestCompleted();

        Assert.NotNull(latest);
        Assert.Equal("2026-10-19", latest!.NightId);
    }

    [Fact]
    public void ArchivioVuoto_Null()
    {
        using var folder = new TempFolder();
        var clock = new FixedCoreClock(MorningOf23);
        var provider = new NightArchiveLatestTimelapseProvider(() => ConfigFor(folder.Path), clock);

        Assert.Null(provider.GetLatestCompleted());
    }

    [Fact]
    public void DurataDerivaDaFrameEFps()
    {
        using var folder = new TempFolder();
        var clock = new FixedCoreClock(MorningOf23);
        var archive = new NightArchive(folder.Path, clock, TimeZoneInfo.Utc);
        NightFixtures.AddNight(archive, "2026-10-21", withTimelapse: true);

        AppConfig config = ConfigFor(folder.Path) with { Products = new AppConfig.ProductsConfig { TimelapseFps = 1 } };
        LatestTimelapse? latest = new NightArchiveLatestTimelapseProvider(() => config, clock).GetLatestCompleted();

        Assert.Equal(TimeSpan.FromSeconds(2), latest!.VideoDuration);
        Assert.Equal(new DateTime(2026, 10, 21), latest.NightStartLocal);
    }
}
