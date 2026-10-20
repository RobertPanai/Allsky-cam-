using System.Diagnostics;
using System.Text;
using AllSky.Archive;
using AllSky.Core;
using Xunit;

namespace AllSky.Archive.Tests;

// Fuso di riferimento: Europe/Rome (CEST, UTC+2, in vigore il 19 ottobre 2026).
// Tutti i file vivono in una cartella temporanea creata per ogni test e rimossa alla fine.
public class NightArchiveTests : IDisposable
{
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
    private static readonly byte[] Thumb = [0xFF, 0xD8, 0x01, 0xFF, 0xD9];

    private readonly string _tmp;
    private readonly FakeClock _clock;

    public NightArchiveTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "allsky-archive-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
        // Ora di riferimento: 19 ottobre 2026 alle 23:00 locali, quindi notte in corso "2026-10-19".
        _clock = new FakeClock(new DateTimeOffset(2026, 10, 19, 23, 0, 0, Cest));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmp))
        {
            Directory.Delete(_tmp, recursive: true);
        }
    }

    private static TimeZoneInfo Rome() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    private NightArchive NewArchive(string? root = null) => new(root ?? _tmp, _clock, Rome());

    private static FrameMeta Meta() => new(ExposureMs: 8000, Gain: 250, MedianAdu: 4210);

    private static DateTimeOffset Local(int day, int hour, int minute, int second) =>
        new(2026, 10, day, hour, minute, second, Cest);

    private string NightsDir => Path.Combine(_tmp, "nights");

    private static string ListAllTmpFiles(string root)
    {
        return string.Join(",", Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories));
    }

    // ---- Salvataggio e lista ----

    [Fact]
    public void SaveFrame_WritesJpegThumbAndMetaLine_AndIndexesIt()
    {
        NightArchive archive = NewArchive();
        DateTimeOffset ts = Local(19, 23, 41, 7);

        archive.SaveFrame(ts, "night", Jpeg, Thumb, Meta());

        string frameId = "20261019T214107Z";
        string nightDir = Path.Combine(NightsDir, "2026-10-19");
        Assert.True(File.Exists(Path.Combine(nightDir, "frames", frameId + ".jpg")));
        Assert.True(File.Exists(Path.Combine(nightDir, "thumbs", frameId + ".jpg")));
        string meta = File.ReadAllText(Path.Combine(nightDir, "meta.jsonl"));
        Assert.Single(meta.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("\"medianAdu\":4210", meta);
        Assert.Contains("\"exposureMs\":8000", meta);

        IReadOnlyList<FrameEntry> frames = archive.ListFrames("2026-10-19", 0, 10);
        FrameEntry e = Assert.Single(frames);
        Assert.Equal(frameId, e.Id);
        Assert.Equal(ts.ToUniversalTime(), e.Timestamp);
        Assert.Equal("night", e.Phase);
        Assert.Equal(8000.0, e.ExposureMs);
        Assert.Equal(250.0, e.Gain);
        Assert.Equal(4210, e.MedianAdu);
    }

    [Fact]
    public void SaveFrame_UsesObservingNightOfTheFrame()
    {
        NightArchive archive = NewArchive();

        // 19/10 alle 03:00 locali appartiene alla notte iniziata il 18/10.
        archive.SaveFrame(Local(19, 3, 0, 0), "night", Jpeg, Thumb, Meta());

        Assert.Equal(new[] {"2026-10-18"}, archive.ListNights().Select(n => n.Id).ToArray());
        Assert.Single(archive.ListFrames("2026-10-18", 0, 10));
    }

    [Fact]
    public void SaveFrame_SameSecond_ReplacesEntryWithoutDuplicate()
    {
        NightArchive archive = NewArchive();
        DateTimeOffset ts = Local(19, 23, 0, 0);

        archive.SaveFrame(ts, "night", Jpeg, Thumb, new FrameMeta(1, 1, 1));
        archive.SaveFrame(ts, "night", Jpeg, Thumb, new FrameMeta(2, 2, 2));

        FrameEntry e = Assert.Single(archive.ListFrames("2026-10-19", 0, 10));
        Assert.Equal(2, e.MedianAdu);
    }

    [Fact]
    public void SaveFrame_InvalidPhase_Throws()
    {
        NightArchive archive = NewArchive();

        Assert.Throws<ArgumentException>(() => archive.SaveFrame(Local(19, 23, 0, 0), "dusk", Jpeg, Thumb, Meta()));
    }

    [Fact]
    public void SaveFrame_EmptyBody_Throws()
    {
        NightArchive archive = NewArchive();

        Assert.Throws<ArgumentException>(() => archive.SaveFrame(Local(19, 23, 0, 0), "night", [], Thumb, Meta()));
        Assert.Throws<ArgumentException>(() => archive.SaveFrame(Local(19, 23, 0, 0), "night", Jpeg, [], Meta()));
    }

    [Fact]
    public void SaveFrame_IsAtomic_NoTmpLeftBehind()
    {
        NightArchive archive = NewArchive();

        for (int s = 0; s < 5; s++)
        {
            archive.SaveFrame(Local(19, 23, 0, s), "night", Jpeg, Thumb, Meta());
        }

        Assert.Equal(string.Empty, ListAllTmpFiles(_tmp));
    }

    [Fact]
    public void ListFrames_PagesWithOffsetAndLimit()
    {
        NightArchive archive = NewArchive();
        for (int s = 0; s < 7; s++)
        {
            archive.SaveFrame(Local(19, 23, 0, s), "night", Jpeg, Thumb, Meta());
        }

        IReadOnlyList<FrameEntry> page = archive.ListFrames("2026-10-19", 2, 3);

        Assert.Equal(3, page.Count);
        Assert.Equal("20261019T210002Z", page[0].Id);
        Assert.Equal("20261019T210004Z", page[2].Id);
        Assert.Empty(archive.ListFrames("2026-10-19", 50, 3));
        Assert.Empty(archive.ListFrames("2020-01-01", 0, 3));
    }

    [Fact]
    public void ListNights_ReportsCountsFlagsAndSizeNewestFirst()
    {
        NightArchive archive = NewArchive();
        archive.SaveFrame(Local(18, 23, 0, 0), "night", Jpeg, Thumb, Meta());
        archive.SaveFrame(Local(18, 23, 0, 30), "night", Jpeg, Thumb, Meta());
        archive.SaveFrame(Local(19, 23, 0, 0), "night", Jpeg, Thumb, Meta());
        string nightDir = Path.Combine(NightsDir, "2026-10-18");
        File.WriteAllBytes(Path.Combine(nightDir, "keogram.jpg"), Jpeg);
        archive.SetTimelapseState("2026-10-18", TimelapseState.Failed);

        IReadOnlyList<NightInfo> nights = archive.ListNights();

        Assert.Equal(new[] {"2026-10-19", "2026-10-18"}, nights.Select(n => n.Id).ToArray());
        NightInfo newer = nights[0];
        Assert.Equal(1, newer.Frames);
        Assert.False(newer.HasKeogram);
        Assert.Equal(TimelapseState.None, newer.TimelapseState);
        NightInfo older = nights[1];
        Assert.Equal(2, older.Frames);
        Assert.True(older.HasKeogram);
        Assert.False(older.HasStartrails);
        Assert.Equal(TimelapseState.Failed, older.TimelapseState);
        Assert.Equal((DateTimeOffset?)Local(18, 23, 0, 0).ToUniversalTime(), older.FirstFrame);
        Assert.Equal((DateTimeOffset?)Local(18, 23, 0, 30).ToUniversalTime(), older.LastFrame);
        Assert.True(older.SizeBytes > 0);
    }

    [Fact]
    public void ListNights_EmptyRootOrMissingNightsFolder_ReturnsEmpty()
    {
        NightArchive archive = NewArchive();

        Assert.Empty(archive.ListNights());
    }

    // ---- Stato timelapse e prodotti ----

    [Fact]
    public void TimelapseState_FollowsMarkerAndMp4()
    {
        NightArchive archive = NewArchive();
        archive.SaveFrame(Local(19, 23, 0, 0), "night", Jpeg, Thumb, Meta());
        string night = "2026-10-19";
        Assert.Equal(TimelapseState.None, State(archive, night));

        archive.SetTimelapseState(night, TimelapseState.Generating);
        Assert.Equal(TimelapseState.Generating, State(archive, night));

        archive.SetTimelapseState(night, TimelapseState.Failed);
        Assert.Equal(TimelapseState.Failed, State(archive, night));

        archive.SetTimelapseState(night, TimelapseState.None);
        Assert.Equal(TimelapseState.None, State(archive, night));

        File.WriteAllBytes(archive.GetProductPath(night, NightProduct.Timelapse), Jpeg);
        Assert.Equal(TimelapseState.Ready, State(archive, night));
    }

    [Fact]
    public void SetTimelapseState_Ready_Throws()
    {
        NightArchive archive = NewArchive();

        Assert.Throws<ArgumentException>(() => archive.SetTimelapseState("2026-10-19", TimelapseState.Ready));
    }

    private static TimelapseState State(NightArchive archive, string night)
    {
        return archive.ListNights().Single(n => n.Id == night).TimelapseState;
    }

    [Fact]
    public void GetProductPath_UsesFixedNames()
    {
        NightArchive archive = NewArchive();

        string dir = Path.Combine(NightsDir, "2026-10-19");
        Assert.Equal(Path.Combine(dir, "keogram.jpg"), archive.GetProductPath("2026-10-19", NightProduct.Keogram));
        Assert.Equal(Path.Combine(dir, "startrails.jpg"), archive.GetProductPath("2026-10-19", NightProduct.Startrails));
        Assert.Equal(Path.Combine(dir, "timelapse.mp4"), archive.GetProductPath("2026-10-19", NightProduct.Timelapse));
    }

    [Fact]
    public void GetFramePathAndThumbPath_PointUnderNightFolder()
    {
        NightArchive archive = NewArchive();

        string dir = Path.Combine(NightsDir, "2026-10-19");
        Assert.Equal(Path.Combine(dir, "frames", "20261019T210000Z.jpg"),
            archive.GetFramePath("2026-10-19", "20261019T210000Z"));
        Assert.Equal(Path.Combine(dir, "thumbs", "20261019T210000Z.jpg"),
            archive.GetThumbPath("2026-10-19", "20261019T210000Z"));
    }

    // ---- Validazione e path traversal ----

    [Theory]
    [InlineData("2026-10-19")]
    [InlineData("1999-01-31")]
    public void IsValidNightId_AcceptsRealDates(string id)
    {
        Assert.True(NightArchive.IsValidNightId(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("2026-10-19/..")]
    [InlineData("../2026-10-19")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows")]
    [InlineData("2026\\10\\19")]
    [InlineData("2026-10-19.")]
    [InlineData("2026-10-19\n")]
    [InlineData("2026-10-19 ")]
    [InlineData("2026-13-01")]
    [InlineData("2026-02-30")]
    [InlineData("２０２６-10-19")]
    [InlineData("2026-10-1")]
    public void IsValidNightId_RejectsTraversalAndMalformed(string id)
    {
        Assert.False(NightArchive.IsValidNightId(id));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("/etc/passwd")]
    [InlineData("..\\..\\x")]
    [InlineData("2026-10-19/../../../etc")]
    [InlineData("")]
    public void GetPaths_RejectInvalidNightId(string nightId)
    {
        NightArchive archive = NewArchive();

        Assert.Throws<ArgumentException>(() => archive.GetProductPath(nightId, NightProduct.Keogram));
        Assert.Throws<ArgumentException>(() => archive.ListFrames(nightId, 0, 1));
        Assert.Throws<ArgumentException>(() => archive.GetFramePath(nightId, "20261019T210000Z"));
        Assert.Throws<ArgumentException>(() => archive.GetThumbPath(nightId, "20261019T210000Z"));
        Assert.Throws<ArgumentException>(() => archive.DeleteNight(nightId));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("/etc/passwd")]
    [InlineData("..\\..\\x")]
    [InlineData("20261019T210000Z/../../x")]
    [InlineData("20261019T210000Z.jpg")]
    [InlineData("20261019T210000Z\n")]
    [InlineData("20261019t210000z")]
    [InlineData("20261019T210000")]
    [InlineData("20261319T210000Z")]
    [InlineData("")]
    public void GetFramePath_RejectsInvalidFrameId(string frameId)
    {
        NightArchive archive = NewArchive();

        Assert.Throws<ArgumentException>(() => archive.GetFramePath("2026-10-19", frameId));
        Assert.Throws<ArgumentException>(() => archive.GetThumbPath("2026-10-19", frameId));
    }

    [Fact]
    public void ValidatePaths_DoNotAcceptNightIdWithSeparatorsInsideRoot()
    {
        NightArchive archive = NewArchive();

        Assert.Throws<ArgumentException>(() => archive.GetProductPath("2026-10-19\\..\\..", NightProduct.Keogram));
    }

    // ---- Retention ----

    [Fact]
    public void ApplyRetention_KeepsNewestAndNeverDeletesCurrentNight()
    {
        NightArchive archive = NewArchive();
        foreach (string day in new[] { "2026-10-15", "2026-10-16", "2026-10-17", "2026-10-18", "2026-10-19" })
        {
            Directory.CreateDirectory(Path.Combine(NightsDir, day, "frames"));
        }
        // Il clock e' il 19 ottobre: la notte in corso e' "2026-10-19". Con keepNights=0 deve sopravvivere comunque.

        IReadOnlyList<string> deleted = archive.ApplyRetention(0);

        Assert.Equal(new[] {"2026-10-18", "2026-10-17", "2026-10-16", "2026-10-15"}, deleted.ToArray());
        Assert.True(Directory.Exists(Path.Combine(NightsDir, "2026-10-19")));
        Assert.False(Directory.Exists(Path.Combine(NightsDir, "2026-10-18")));
    }

    [Fact]
    public void ApplyRetention_KeepsRequestedNumberOfNights()
    {
        NightArchive archive = NewArchive();
        foreach (string day in new[] { "2026-10-17", "2026-10-18", "2026-10-19" })
        {
            Directory.CreateDirectory(Path.Combine(NightsDir, day));
        }

        IReadOnlyList<string> deleted = archive.ApplyRetention(2);

        Assert.Equal(new[] {"2026-10-17"}, deleted.ToArray());
        Assert.False(Directory.Exists(Path.Combine(NightsDir, "2026-10-17")));
        Assert.True(Directory.Exists(Path.Combine(NightsDir, "2026-10-18")));
    }

    [Fact]
    public void ApplyRetention_CurrentNightIsNeverDeleted_EvenIfOutsideKeptRange()
    {
        // Il clock e' il 1 settembre: la notte in corso e' "2026-09-01", che con keepNights=1 non e' tra le piu' recenti.
        _clock.UtcNow = new DateTimeOffset(2026, 9, 1, 23, 0, 0, Cest);
        NightArchive archive = NewArchive();
        foreach (string day in new[] { "2026-08-01", "2026-09-01", "2026-10-19" })
        {
            Directory.CreateDirectory(Path.Combine(NightsDir, day));
        }

        IReadOnlyList<string> deleted = archive.ApplyRetention(1);

        Assert.Equal(new[] { "2026-08-01" }, deleted.ToArray());
        Assert.True(Directory.Exists(Path.Combine(NightsDir, "2026-09-01")));
        Assert.False(Directory.Exists(Path.Combine(NightsDir, "2026-08-01")));
    }

    [Fact]
    public void ApplyRetention_LeavesForeignEntriesAlone()
    {
        NightArchive archive = NewArchive();
        Directory.CreateDirectory(Path.Combine(NightsDir, "2026-10-01"));
        Directory.CreateDirectory(Path.Combine(NightsDir, "backup-old"));
        File.WriteAllText(Path.Combine(NightsDir, "2026-10-02.txt"), "non e' una notte");
        File.WriteAllText(Path.Combine(NightsDir, "2026-10-03.tmp"), "residuo");
        Directory.CreateDirectory(Path.Combine(NightsDir, "2025-01-01x"));
        File.WriteAllText(Path.Combine(_tmp, "config.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_tmp, "2026-01-01"));

        IReadOnlyList<string> deleted = archive.ApplyRetention(0);

        Assert.Equal(new[] {"2026-10-01"}, deleted.ToArray());
        Assert.True(Directory.Exists(Path.Combine(NightsDir, "backup-old")));
        Assert.True(File.Exists(Path.Combine(NightsDir, "2026-10-02.txt")));
        Assert.True(File.Exists(Path.Combine(NightsDir, "2026-10-03.tmp")));
        Assert.True(Directory.Exists(Path.Combine(NightsDir, "2025-01-01x")));
        Assert.True(File.Exists(Path.Combine(_tmp, "config.json")));
        Assert.True(Directory.Exists(Path.Combine(_tmp, "2026-01-01")));
    }

    [Fact]
    public void ApplyRetention_SkipsSymlinkedNight_AndNeverFollowsIt()
    {
        string outside = Path.Combine(_tmp, "outside");
        Directory.CreateDirectory(Path.Combine(outside, "keep-me"));
        File.WriteAllText(Path.Combine(outside, "keep-me", "precious.txt"), "non toccare");
        Directory.CreateDirectory(NightsDir);
        string link = Path.Combine(NightsDir, "2026-10-01");
        try
        {
            File.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Il sistema non consente i symlink senza privilegi: il test non e' applicabile qui.
            return;
        }
        NightArchive archive = NewArchive();

        IReadOnlyList<string> deleted = archive.ApplyRetention(0);

        Assert.Empty(deleted);
        Assert.True(File.Exists(Path.Combine(outside, "keep-me", "precious.txt")));
        Assert.Empty(archive.ListNights());
    }

    [Fact]
    public void DeleteNight_RemovesFolderAndIndex()
    {
        NightArchive archive = NewArchive();
        archive.SaveFrame(Local(18, 23, 0, 0), "night", Jpeg, Thumb, Meta());
        archive.SaveFrame(Local(19, 23, 0, 0), "night", Jpeg, Thumb, Meta());

        Assert.True(archive.DeleteNight("2026-10-18"));

        Assert.False(Directory.Exists(Path.Combine(NightsDir, "2026-10-18")));
        Assert.Empty(archive.ListFrames("2026-10-18", 0, 10));
        Assert.Equal(new[] {"2026-10-19"}, archive.ListNights().Select(n => n.Id).ToArray());
        Assert.False(archive.DeleteNight("2026-10-18"));
    }

    [Fact]
    public void DeleteNight_CurrentNight_Throws()
    {
        NightArchive archive = NewArchive();
        archive.SaveFrame(Local(19, 23, 0, 0), "night", Jpeg, Thumb, Meta());

        Assert.Throws<InvalidOperationException>(() => archive.DeleteNight("2026-10-19"));
        Assert.True(Directory.Exists(Path.Combine(NightsDir, "2026-10-19")));
    }

    // ---- Indice su disco ----

    [Fact]
    public void LoadIndex_IgnoresTruncatedLastLine_AndNextSaveStaysReadable()
    {
        Directory.CreateDirectory(Path.Combine(NightsDir, "2026-10-19"));
        string meta = Path.Combine(NightsDir, "2026-10-19", "meta.jsonl");
        File.WriteAllText(meta,
            "{\"id\":\"20261019T210000Z\",\"timestamp\":\"2026-10-19T21:00:00+00:00\",\"phase\":\"night\",\"exposureMs\":8000,\"gain\":250,\"medianAdu\":4000}\n"
            + "{\"id\":\"20261019T210010Z\",\"tim");
        NightArchive archive = NewArchive();

        Assert.Single(archive.ListFrames("2026-10-19", 0, 10));

        archive.SaveFrame(Local(19, 23, 0, 20), "night", Jpeg, Thumb, Meta());

        IReadOnlyList<FrameEntry> frames = archive.ListFrames("2026-10-19", 0, 10);
        Assert.Equal(2, frames.Count);
        Assert.Equal("20261019T210020Z", frames[1].Id);
        Assert.Equal(2, NewArchive().ListFrames("2026-10-19", 0, 10).Count);
    }

    [Fact]
    public void LoadIndex_ReadsTenThousandLinesQuickly()
    {
        string nightDir = Path.Combine(NightsDir, "2026-10-19");
        Directory.CreateDirectory(nightDir);
        DateTimeOffset start = new(2026, 10, 19, 21, 0, 0, TimeSpan.Zero);
        var sb = new StringBuilder();
        for (int i = 0; i < 10_000; i++)
        {
            DateTimeOffset t = start.AddSeconds(i);
            sb.Append("{\"id\":\"").Append(t.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture))
              .Append("\",\"timestamp\":\"").Append(t.ToString("o", System.Globalization.CultureInfo.InvariantCulture))
              .Append("\",\"phase\":\"night\",\"exposureMs\":8000,\"gain\":250,\"medianAdu\":4000}\n");
        }
        File.WriteAllText(Path.Combine(nightDir, "meta.jsonl"), sb.ToString());
        NightArchive archive = NewArchive();

        var watch = Stopwatch.StartNew();
        IReadOnlyList<FrameEntry> frames = archive.ListFrames("2026-10-19", 0, 20_000);
        watch.Stop();

        Assert.Equal(10_000, frames.Count);
        Assert.True(watch.ElapsedMilliseconds < 1000, $"Lettura di 10k righe lenta: {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void SaveFrame_ConcurrentCalls_AllIndexed()
    {
        NightArchive archive = NewArchive();

        Parallel.For(0, 50, s => archive.SaveFrame(Local(19, 23, 0, s), "night", Jpeg, Thumb, Meta()));

        Assert.Equal(50, archive.ListFrames("2026-10-19", 0, 100).Count);
        Assert.Equal(string.Empty, ListAllTmpFiles(_tmp));
    }

    // ---- Disco ----

    [Fact]
    public void GetDiskUsage_ReportsArchiveSize()
    {
        NightArchive archive = NewArchive();
        archive.SaveFrame(Local(19, 23, 0, 0), "night", Jpeg, Thumb, Meta());

        DiskUsage usage = archive.GetDiskUsage();

        Assert.True(usage.UsedBytes >= Jpeg.Length + Thumb.Length);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset now)
        {
            UtcNow = now;
        }

        public DateTimeOffset UtcNow { get; set; }
    }
}
