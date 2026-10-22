using AllSky.Core;
using AllSky.Publish.Monitor;
using Xunit;

namespace AllSky.Publish.Tests.Monitor;

public sealed class MonitorSnapshotBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 21, 30, 0, TimeSpan.FromHours(2));

    /// <summary>Configurazione accesa con le sole voci indicate (nomi come nelle proprietà di MonitorVoices).</summary>
    private static AppConfig.MonitorConfig Config(params string[] on)
    {
        var set = on.ToHashSet(StringComparer.Ordinal);
        return new AppConfig.MonitorConfig
        {
            Enabled = true,
            Voices = new AppConfig.MonitorConfig.MonitorVoices
            {
                AllSkyImage = set.Contains("AllSkyImage"),
                NightPhase = set.Contains("NightPhase"),
                SunMoonAltitude = set.Contains("SunMoonAltitude"),
                NinaTarget = set.Contains("NinaTarget"),
                NinaParkState = set.Contains("NinaParkState"),
                TargetAltitudeChart = set.Contains("TargetAltitudeChart"),
                MoonDistance = set.Contains("MoonDistance"),
                LatestTimelapse = set.Contains("LatestTimelapse"),
                Keogram = set.Contains("Keogram"),
                StartTrails = set.Contains("StartTrails"),
            },
        };
    }

    private static MonitorInputs FullInputs() => new()
    {
        NightPhase = "night",
        SunAltitudeDeg = -31.26,
        MoonAltitudeDeg = 12.04,
        TargetName = "M 42 - Nebulosa di Orione",
        ParkState = "Parked",
        MoonSeparationDeg = 88.87,
        TargetAltitudeChart = [new MonitorAltitudePoint(Now, 40.04)],
        AllSkyImagePath = @"D:\allsky\2026\img\latest.jpg",
        LatestTimelapsePath = "/data/timelapse/2026-10-22.mp4",
        KeogramPath = "/data/keogram.png",
        StartTrailsPath = @"C:\trails\start.jpg",
    };

    /// <summary>Nomi delle proprietà valorizzate, esclusi SchemaVersion e UpdatedUtc (sempre presenti).</summary>
    private static HashSet<string> Populated(MonitorSnapshot s)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (s.NightPhase is not null) names.Add(nameof(s.NightPhase));
        if (s.SunAltitudeDeg is not null) names.Add(nameof(s.SunAltitudeDeg));
        if (s.MoonAltitudeDeg is not null) names.Add(nameof(s.MoonAltitudeDeg));
        if (s.TargetName is not null) names.Add(nameof(s.TargetName));
        if (s.ParkState is not null) names.Add(nameof(s.ParkState));
        if (s.MoonSeparationDeg is not null) names.Add(nameof(s.MoonSeparationDeg));
        if (s.TargetAltitudeChart is not null) names.Add(nameof(s.TargetAltitudeChart));
        if (s.Assets.Count > 0) names.Add(nameof(s.Assets));
        return names;
    }

    [Fact]
    public void DefaultConfig_BuildReturnsNull()
    {
        var result = MonitorSnapshotBuilder.Build(new AppConfig().Monitor, FullInputs(), Now);

        Assert.Null(result);
    }

    [Fact]
    public void DisabledWithVoicesOn_BuildReturnsNull()
    {
        var config = Config("NightPhase") with { Enabled = false };

        Assert.Null(MonitorSnapshotBuilder.Build(config, FullInputs(), Now));
    }

    [Fact]
    public void AllSkyImageVoice_OnlyPublishesItsAsset()
    {
        MonitorSnapshot s = Build("AllSkyImage");

        Assert.Equal(new[] { "Assets" }, Populated(s).ToArray());
        Assert.Equal(new[] { new MonitorAsset(MonitorAssetKind.AllSkyImage, "latest.jpg") }, s.Assets);
    }

    [Fact]
    public void NightPhaseVoice_OnlyPublishesPhase()
    {
        MonitorSnapshot s = Build("NightPhase");

        Assert.Equal(new[] { "NightPhase" }, Populated(s).ToArray());
        Assert.Equal("night", s.NightPhase);
    }

    [Fact]
    public void SunMoonAltitudeVoice_OnlyPublishesAltitudes()
    {
        MonitorSnapshot s = Build("SunMoonAltitude");

        Assert.Equal(new[] { "SunAltitudeDeg", "MoonAltitudeDeg" }.OrderBy(x => x).ToArray(), Populated(s).OrderBy(x => x).ToArray());
        Assert.Equal(-31.3, s.SunAltitudeDeg);
        Assert.Equal(12.0, s.MoonAltitudeDeg);
    }

    [Fact]
    public void NinaTargetVoice_OnlyPublishesTargetName()
    {
        MonitorSnapshot s = Build("NinaTarget");

        Assert.Equal(new[] { "TargetName" }, Populated(s).ToArray());
        Assert.Equal("M 42 - Nebulosa di Orione", s.TargetName);
    }

    [Fact]
    public void NinaParkStateVoice_OnlyPublishesParkState()
    {
        MonitorSnapshot s = Build("NinaParkState");

        Assert.Equal(new[] { "ParkState" }, Populated(s).ToArray());
        Assert.Equal("parked", s.ParkState);
    }

    [Fact]
    public void TargetAltitudeChartVoice_OnlyPublishesChart()
    {
        MonitorSnapshot s = Build("TargetAltitudeChart");

        Assert.Equal(new[] { "TargetAltitudeChart" }, Populated(s).ToArray());
        Assert.Equal(40.0, Assert.Single(s.TargetAltitudeChart!).AltitudeDeg);
    }

    [Fact]
    public void MoonDistanceVoice_OnlyPublishesSeparation()
    {
        MonitorSnapshot s = Build("MoonDistance");

        Assert.Equal(new[] { "MoonSeparationDeg" }, Populated(s).ToArray());
        Assert.Equal(88.9, s.MoonSeparationDeg);
    }

    [Fact]
    public void LatestTimelapseVoice_OnlyPublishesItsAsset()
    {
        MonitorSnapshot s = Build("LatestTimelapse");

        Assert.Equal(new[] { "Assets" }, Populated(s).ToArray());
        Assert.Equal(new[] { new MonitorAsset(MonitorAssetKind.LatestTimelapse, "2026-10-22.mp4") }, s.Assets);
    }

    [Fact]
    public void KeogramVoice_OnlyPublishesItsAsset()
    {
        MonitorSnapshot s = Build("Keogram");

        Assert.Equal(new[] { "Assets" }, Populated(s).ToArray());
        Assert.Equal(new[] { new MonitorAsset(MonitorAssetKind.Keogram, "keogram.png") }, s.Assets);
    }

    [Fact]
    public void StartTrailsVoice_OnlyPublishesItsAsset()
    {
        MonitorSnapshot s = Build("StartTrails");

        Assert.Equal(new[] { "Assets" }, Populated(s).ToArray());
        Assert.Equal(new[] { new MonitorAsset(MonitorAssetKind.StartTrails, "start.jpg") }, s.Assets);
    }

    [Fact]
    public void AllVoicesOn_PublishesEverythingInOrder()
    {
        MonitorSnapshot s = Build(
            "AllSkyImage", "NightPhase", "SunMoonAltitude", "NinaTarget", "NinaParkState",
            "TargetAltitudeChart", "MoonDistance", "LatestTimelapse", "Keogram", "StartTrails");

        Assert.Equal(1, s.SchemaVersion);
        Assert.Equal(Now.ToUniversalTime(), s.UpdatedUtc);
        Assert.Equal(
            new[] { MonitorAssetKind.AllSkyImage, MonitorAssetKind.LatestTimelapse, MonitorAssetKind.Keogram, MonitorAssetKind.StartTrails },
            s.Assets.Select(a => a.Kind).ToArray());
    }

    [Fact]
    public void NoVoices_ProducesEmptySnapshotWithoutData()
    {
        MonitorSnapshot s = Build();

        Assert.Empty(Populated(s));
        Assert.Empty(s.Assets);
    }

    [Fact]
    public void Altitudes_AreRoundedToTenthOfDegree()
    {
        var inputs = new MonitorInputs { SunAltitudeDeg = -12.345, MoonAltitudeDeg = 45.06, MoonSeparationDeg = 0.04 };

        MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("SunMoonAltitude", "MoonDistance"), inputs, Now)!;

        Assert.Equal(-12.3, s.SunAltitudeDeg);
        Assert.Equal(45.1, s.MoonAltitudeDeg);
        Assert.Equal(0.0, s.MoonSeparationDeg);
    }

    [Fact]
    public void NonFiniteValues_BecomeNull()
    {
        var inputs = new MonitorInputs { SunAltitudeDeg = double.NaN, MoonAltitudeDeg = double.PositiveInfinity };

        MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("SunMoonAltitude"), inputs, Now)!;

        Assert.Null(s.SunAltitudeDeg);
        Assert.Null(s.MoonAltitudeDeg);
    }

    [Fact]
    public void Chart_UpToLimit_KeepsEveryPointRounded()
    {
        var points = Enumerable.Range(0, 96)
            .Select(i => new MonitorAltitudePoint(Now.AddMinutes(i), i * 0.13))
            .ToArray();

        MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("TargetAltitudeChart"), new MonitorInputs { TargetAltitudeChart = points }, Now)!;

        Assert.Equal(96, s.TargetAltitudeChart!.Count);
        Assert.Equal(0.1, s.TargetAltitudeChart[1].AltitudeDeg);
    }

    [Fact]
    public void Chart_LongerThanLimit_IsDecimatedToAtMost96Points()
    {
        foreach (int count in new[] { 97, 200, 1000, 5000 })
        {
            var points = Enumerable.Range(0, count)
                .Select(i => new MonitorAltitudePoint(Now.AddMinutes(i), i * 0.01))
                .ToArray();

            MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("TargetAltitudeChart"), new MonitorInputs { TargetAltitudeChart = points }, Now)!;

            Assert.InRange(s.TargetAltitudeChart!.Count, 1, 96);
            Assert.Equal(points[0].TimeUtc.ToUniversalTime(), s.TargetAltitudeChart[0].TimeUtc);
        }
    }

    [Fact]
    public void Chart_DropsNonFinitePoints()
    {
        var points = new[]
        {
            new MonitorAltitudePoint(Now, 10.04),
            new MonitorAltitudePoint(Now.AddMinutes(1), double.NaN),
            new MonitorAltitudePoint(Now.AddMinutes(2), 11.0),
        };

        MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("TargetAltitudeChart"), new MonitorInputs { TargetAltitudeChart = points }, Now)!;

        Assert.Equal(new[] { 10.0, 11.0 }, s.TargetAltitudeChart!.Select(p => p.AltitudeDeg).ToArray());
    }

    [Fact]
    public void TargetName_RemovesControlCharacters_AndTrims()
    {
        var inputs = new MonitorInputs { TargetName = "  M\t31\r\nAndromeda  " };

        MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("NinaTarget"), inputs, Now)!;

        Assert.Equal("M31Andromeda", s.TargetName);
    }

    [Fact]
    public void TargetName_IsCutTo80Characters()
    {
        var inputs = new MonitorInputs { TargetName = new string('x', 200) };

        MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("NinaTarget"), inputs, Now)!;

        Assert.Equal(80, s.TargetName!.Length);
    }

    [Fact]
    public void TargetName_OnlyControlCharacters_BecomesNull()
    {
        var inputs = new MonitorInputs { TargetName = "\t\n\u0007" };

        MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("NinaTarget"), inputs, Now)!;

        Assert.Null(s.TargetName);
    }

    [Fact]
    public void NightPhase_OutsideClosedList_IsDropped()
    {
        var inputs = new MonitorInputs { NightPhase = "civilTwilight; DROP TABLE" };

        MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("NightPhase"), inputs, Now)!;

        Assert.Null(s.NightPhase);
    }

    [Fact]
    public void ParkState_OutsideClosedList_IsDropped_AndKnownValueIsNormalized()
    {
        MonitorSnapshot unknown = MonitorSnapshotBuilder.Build(Config("NinaParkState"), new MonitorInputs { ParkState = "Lost" }, Now)!;
        MonitorSnapshot known = MonitorSnapshotBuilder.Build(Config("NinaParkState"), new MonitorInputs { ParkState = " Tracking " }, Now)!;

        Assert.Null(unknown.ParkState);
        Assert.Equal("tracking", known.ParkState);
    }

    [Theory]
    [InlineData(@"C:\Users\rob\img\a.jpg", "a.jpg")]
    [InlineData(@"D:\allsky\2026\deep\final.png", "final.png")]
    [InlineData("/home/rob/img/a.jpg", "a.jpg")]
    [InlineData("D:/mixed\\path/to/b.mp4", "b.mp4")]
    [InlineData("a.jpg", "a.jpg")]
    [InlineData("/home/rob/img/", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("/home/rob/..", null)]
    public void FileNameOnly_NeverKeepsFolders(string? path, string? expected)
    {
        Assert.Equal(expected, MonitorSnapshotBuilder.FileNameOnly(path));
    }

    [Fact]
    public void Assets_CarryOnlyFileName_ForFullWindowsAndUnixPaths()
    {
        MonitorSnapshot s = Build("AllSkyImage", "LatestTimelapse", "Keogram", "StartTrails");

        Assert.Equal(
            new[] { "latest.jpg", "2026-10-22.mp4", "keogram.png", "start.jpg" },
            s.Assets.Select(a => a.FileName).ToArray());
        Assert.All(s.Assets, a => Assert.DoesNotContain('/', a.FileName));
        Assert.All(s.Assets, a => Assert.DoesNotContain('\\', a.FileName));
    }

    [Fact]
    public void MissingAssetPath_IsSkipped()
    {
        MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("Keogram"), new MonitorInputs(), Now)!;

        Assert.Empty(s.Assets);
    }

    [Fact]
    public void UpdatedUtc_IsNowConvertedToUtc()
    {
        MonitorSnapshot s = MonitorSnapshotBuilder.Build(Config("NightPhase"), new MonitorInputs { NightPhase = "day" }, Now)!;

        Assert.Equal(TimeSpan.Zero, s.UpdatedUtc.Offset);
        Assert.Equal(Now.UtcDateTime, s.UpdatedUtc.UtcDateTime);
    }

    private static MonitorSnapshot Build(params string[] on) =>
        MonitorSnapshotBuilder.Build(Config(on), FullInputs(), Now)!;
}
