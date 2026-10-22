using AllSky.Archive;
using AllSky.Detection;
using AllSky.Events;
using AllSky.Timelapse;
using Xunit;

namespace AllSky.Events.Tests;

// Salvataggio di meteore e bolidi: ritaglio, frame evidenziato, clip finta, metadati, riclassificazione.
public class EventSaverTests : IDisposable
{
    private readonly EventsTempDir _tmp = new();
    private readonly NightArchive _nights;
    private readonly EventArchive _events;
    private readonly RecordingJpegEncoder _encoder = new();
    private readonly FakeClipWriter _clip = new();

    public EventSaverTests()
    {
        _nights = EventsFixtures.NewNights(_tmp.Path);
        _events = new EventArchive(_tmp.Path);
    }

    public void Dispose() => _tmp.Dispose();

    private static DateTimeOffset Utc(int minute) => EventsFixtures.Utc(minute);

    private static string IdOf(DateTimeOffset ts) => EventsFixtures.FrameId(ts);

    // Finestra [N-2, N+2] con N = minuto 2; il minuto 5 non e' archiviato e deve essere saltato.
    private static IReadOnlyList<string> Window() => new[] { IdOf(Utc(0)), IdOf(Utc(1)), IdOf(Utc(2)), IdOf(Utc(3)), IdOf(Utc(4)), IdOf(Utc(5)) };

    private void SaveNeighbourhood()
    {
        EventsFixtures.SaveFrames(_nights, Utc(0), Utc(1), Utc(2), Utc(3), Utc(4));
    }

    private static FrameClassification Single(DateTimeOffset ts, StreakClass cls, Streak streak)
        => EventsFixtures.Frame(ts, new ClassifiedStreak(streak, cls));

    private EventSaver NewSaver(IPixelToSky? sky = null, IClipWriter? clip = null, bool withClip = true)
        => new(_events, _nights, _encoder, withClip ? (clip ?? _clip) : null, sky);

    private static Streak Horizontal() => EventsFixtures.Line(40, 60, 100, 60);

    [Fact]
    public async Task Meteor_SavesCropHighlightedFrameAndClip()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        EventFrame frame = EventsFixtures.MakeFrame(n, Window());
        FrameClassification classification = Single(n, StreakClass.Meteor, Horizontal());

        IReadOnlyList<EventSaveResult> results = await NewSaver().SaveAsync(frame, classification);

        EventSaveResult saved = Assert.Single(results);
        EventRecord e = saved.Event;
        Assert.Null(saved.ClipError);
        Assert.Equal(IdOf(n) + "-000", e.Id);
        Assert.Equal(EventKind.Meteor, e.Kind);
        Assert.Equal(StreakClass.Meteor, e.Class);
        Assert.Equal("2026-10-19", e.NightId);
        Assert.Equal(n, e.TimestampUtc);
        Assert.Equal(IdOf(n), e.SourceFrameId);
        Assert.Equal(new EventFiles { Crop = "crop.jpg", Frame = "frame.jpg", Clip = "clip.mp4" }, e.Files);
        Assert.Equal(40, e.Segment.X1);
        Assert.Equal(100, e.Segment.X2);
        Assert.Equal(60, e.LengthPx);
        Assert.Null(e.StartAltAz);
        Assert.Null(e.EndAltAz);

        // Clip: solo i frame archiviati, nell'ordine dato, a 25 fps; il finto ha scritto il file.
        Assert.Single(_clip.Calls);
        Assert.Equal(25, _clip.LastFps);
        string names = string.Join(",", _clip.Calls[0].Select(Path.GetFileName));
        Assert.Equal(string.Join(",", new[] { IdOf(Utc(0)), IdOf(Utc(1)), IdOf(Utc(2)), IdOf(Utc(3)), IdOf(Utc(4)) }.Select(id => id + ".jpg")), names);
        Assert.True(File.Exists(_events.GetEventFilePath("2026-10-19", e.Id, "clip.mp4")));

        // Record su disco identico a quello restituito.
        Assert.Equal(e, _events.Get("2026-10-19", e.Id));
    }

    [Fact]
    public async Task Meteor_CropIsMagnifiedAroundTheStreakAndFrameIsHighlighted()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        await NewSaver().SaveAsync(EventsFixtures.MakeFrame(n, Window()), Single(n, StreakClass.Meteor, Horizontal()));

        // Due JPEG: prima il ritaglio, poi il frame intero.
        Assert.Equal(2, _encoder.Calls.Count);
        var crop = _encoder.Calls[0];
        var frame = _encoder.Calls[1];

        // Scia da x 80 a x 200 a piena risoluzione: lato 96..168 con margine 24, ingrandimento 4 -> 672 px.
        Assert.Equal(672, crop.Width);
        Assert.Equal(672, crop.Height);
        Assert.Equal(320, frame.Width);
        Assert.Equal(240, frame.Height);

        // Centro del ritaglio = centro della scia a piena risoluzione (x 140, y 120): il pixel e' bianco nel frame sorgente.
        int centre = (336 * 672 + 336) * 3;
        Assert.Equal((byte)255, crop.Rgb[centre]);
        Assert.Equal((byte)255, crop.Rgb[centre + 1]);
        Assert.Equal((byte)255, crop.Rgb[centre + 2]);

        // Il frame intero ha il colore di evidenziazione sulla scia, e non sull'area fuori dalla scia.
        int onStreak = (120 * 320 + 140) * 3;
        Assert.Equal((byte)0x7d, frame.Rgb[onStreak]);
        Assert.Equal((byte)0xa0, frame.Rgb[onStreak + 1]);
        Assert.Equal((byte)0xff, frame.Rgb[onStreak + 2]);
        int farAway = (10 * 320 + 10) * 3;
        Assert.Equal((byte)0, frame.Rgb[farAway]);
    }

    [Fact]
    public async Task Bolide_UsesBolideKindAndSkyPositionsWhenCalibrated()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        var sky = new FakeSky { Map = (x, y) => new AltAz { AltDeg = y, AzDeg = x } };
        FrameClassification cls = Single(n, StreakClass.Bolide, Horizontal());

        EventSaveResult saved = Assert.Single(await NewSaver(sky).SaveAsync(EventsFixtures.MakeFrame(n, Window()), cls));

        Assert.Equal(EventKind.Bolide, saved.Event.Kind);
        Assert.Equal(new AltAz { AltDeg = 60, AzDeg = 40 }, saved.Event.StartAltAz);
        Assert.Equal(new AltAz { AltDeg = 60, AzDeg = 100 }, saved.Event.EndAltAz);
        Assert.Equal(saved.Event, _events.Get("2026-10-19", saved.Event.Id));
    }

    [Fact]
    public async Task OnlyMeteorAndBolide_ProduceEvents_AndIndexStaysStable()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        FrameClassification cls = EventsFixtures.Frame(
            n,
            new ClassifiedStreak(EventsFixtures.Line(10, 10, 20, 10), StreakClass.Satellite),
            new ClassifiedStreak(EventsFixtures.Line(10, 30, 20, 30), StreakClass.AereoOSatellite),
            new ClassifiedStreak(EventsFixtures.Line(10, 80, 90, 80), StreakClass.Meteor));

        IReadOnlyList<EventSaveResult> results = await NewSaver().SaveAsync(EventsFixtures.MakeFrame(n, Window()), cls);

        EventSaveResult only = Assert.Single(results);
        Assert.Equal(IdOf(n) + "-002", only.Event.Id);
        Assert.Single(_events.List(new EventQuery { IncludeDiscarded = true }));
    }

    [Fact]
    public async Task DiscardedFrame_ProducesNothing()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        var cls = new FrameClassification(n, true, Array.Empty<ClassifiedStreak>());

        Assert.Empty(await NewSaver().SaveAsync(EventsFixtures.MakeFrame(n, Window()), cls));
        Assert.Empty(_events.List(new EventQuery { IncludeDiscarded = true }));
    }

    [Fact]
    public async Task ClipFailure_KeepsTheEventAndReportsTheReason()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        var failing = new FakeClipWriter { FailWith = new TimelapseException("ffmpeg assente") };

        EventSaveResult saved = Assert.Single(await NewSaver(clip: failing).SaveAsync(
            EventsFixtures.MakeFrame(n, Window()), Single(n, StreakClass.Meteor, Horizontal())));

        Assert.Null(saved.Event.Files.Clip);
        Assert.NotNull(saved.ClipError);
        Assert.Contains("ffmpeg assente", saved.ClipError!);
        Assert.False(File.Exists(Path.Combine(_events.GetEventFolder("2026-10-19", saved.Event.Id), "clip.mp4")));
        Assert.True(File.Exists(_events.GetEventFilePath("2026-10-19", saved.Event.Id, "crop.jpg")));
    }

    [Fact]
    public async Task NoClipWriter_SavesWithoutClip()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);

        EventSaveResult saved = Assert.Single(await NewSaver(withClip: false).SaveAsync(
            EventsFixtures.MakeFrame(n, Window()), Single(n, StreakClass.Meteor, Horizontal())));

        Assert.Null(saved.Event.Files.Clip);
        Assert.Null(saved.ClipError);
        Assert.Empty(_clip.Calls);
    }

    [Fact]
    public async Task NoNeighbourArchived_ReportsMissingClip()
    {
        DateTimeOffset n = Utc(2);
        EventsFixtures.SaveFrames(_nights, n);

        EventSaveResult saved = Assert.Single(await NewSaver().SaveAsync(
            EventsFixtures.MakeFrame(n, new[] { IdOf(Utc(7)) }), Single(n, StreakClass.Meteor, Horizontal())));

        Assert.Null(saved.Event.Files.Clip);
        Assert.Equal("nessun frame vicino archiviato", saved.ClipError);
        Assert.Empty(_clip.Calls);
    }

    [Fact]
    public async Task NextFrameRepeatingTheStreak_PreventsTheEvent()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        DateTimeOffset next = Utc(3);
        FrameClassification nextCls = Single(next, StreakClass.AereoOSatellite, EventsFixtures.Line(42, 60, 102, 60));

        IReadOnlyList<EventSaveResult> results = await NewSaver().SaveAsync(
            EventsFixtures.MakeFrame(n, Window()), Single(n, StreakClass.Meteor, Horizontal()), nextCls);

        Assert.Empty(results);
        Assert.Empty(_events.List(new EventQuery { IncludeDiscarded = true }));
        Assert.False(Directory.Exists(_events.GetEventFolder("2026-10-19", IdOf(n) + "-000")));
    }

    [Fact]
    public async Task NextFrameRepeatingAnotherStreak_DoesNotPreventTheEvent()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        DateTimeOffset next = Utc(3);
        FrameClassification nextCls = Single(next, StreakClass.AereoOSatellite, EventsFixtures.Line(10, 10, 20, 10));

        IReadOnlyList<EventSaveResult> results = await NewSaver().SaveAsync(
            EventsFixtures.MakeFrame(n, Window()), Single(n, StreakClass.Meteor, Horizontal()), nextCls);

        Assert.Single(results);
    }

    [Fact]
    public async Task LateReclassification_DiscardsTheEventWithoutDeletingFiles()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        EventSaveResult saved = Assert.Single(await NewSaver().SaveAsync(
            EventsFixtures.MakeFrame(n, Window()), Single(n, StreakClass.Meteor, Horizontal())));
        FrameClassification nextCls = Single(Utc(3), StreakClass.AereoOSatellite, EventsFixtures.Line(42, 60, 102, 60));

        // Un frame diverso non c'entra: nessun evento scartato.
        Assert.Empty(new EventSaver(_events, _nights, _encoder).DiscardRepeats("2026-10-19", IdOf(Utc(1)), nextCls));

        IReadOnlyList<EventRecord> discarded = new EventSaver(_events, _nights, _encoder)
            .DiscardRepeats("2026-10-19", IdOf(n), nextCls);

        EventRecord only = Assert.Single(discarded);
        Assert.Equal(saved.Event.Id, only.Id);
        Assert.Equal(EventStatus.Discarded, only.Status);
        Assert.False(string.IsNullOrWhiteSpace(only.StatusReason));

        // I file restano, l'evento esce dalla lista di default e compare con IncludeDiscarded.
        string folder = _events.GetEventFolder("2026-10-19", saved.Event.Id);
        Assert.True(File.Exists(Path.Combine(folder, "crop.jpg")));
        Assert.True(File.Exists(Path.Combine(folder, "frame.jpg")));
        Assert.True(File.Exists(Path.Combine(folder, "clip.mp4")));
        Assert.Empty(_events.List());
        Assert.Single(_events.List(new EventQuery { IncludeDiscarded = true }));
    }

    [Fact]
    public async Task SavingTheSameStreakTwice_Throws_AndKeepsTheFirstEvent()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        EventFrame frame = EventsFixtures.MakeFrame(n, Window());
        FrameClassification cls = Single(n, StreakClass.Meteor, Horizontal());
        EventSaver saver = NewSaver();
        EventSaveResult first = Assert.Single(await saver.SaveAsync(frame, cls));
        EventRecord before = _events.Get("2026-10-19", first.Event.Id)!;
        _events.SetLabel("2026-10-19", first.Event.Id, UserLabel.Confirmed);

        await Assert.ThrowsAsync<InvalidOperationException>(() => saver.SaveAsync(frame, cls));

        EventRecord after = _events.Get("2026-10-19", first.Event.Id)!;
        Assert.Equal(UserLabel.Confirmed, after.Label);
        Assert.Equal(before.Id, after.Id);
    }

    [Fact]
    public async Task InconsistentFrameBuffer_IsRejected()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);
        EventFrame good = EventsFixtures.MakeFrame(n, Window());
        EventFrame bad = good with { Rgb8 = new byte[10] };

        await Assert.ThrowsAsync<ArgumentException>(() => NewSaver().SaveAsync(bad, Single(n, StreakClass.Meteor, Horizontal())));
    }

    [Fact]
    public async Task ClassificationFromAnotherInstant_IsRejected()
    {
        SaveNeighbourhood();
        DateTimeOffset n = Utc(2);

        await Assert.ThrowsAsync<ArgumentException>(() => NewSaver().SaveAsync(
            EventsFixtures.MakeFrame(n, Window()), Single(Utc(3), StreakClass.Meteor, Horizontal())));
    }

    [Fact]
    public void Options_OutOfRange_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventSaver(_events, _nights, _encoder, options: new EventSaverOptions { JpegQuality = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventSaver(_events, _nights, _encoder, options: new EventSaverOptions { ClipFps = 0 }));
    }

    [Fact]
    public void ComputeCrop_StaysInsideTheFrame()
    {
        CropRect corner = FrameRaster.ComputeCrop(320, 240, 2, 2, 10, 2, 96, 24);
        Assert.Equal(0, corner.X0);
        Assert.Equal(0, corner.Y0);
        Assert.Equal(96, corner.Side);

        CropRect wide = FrameRaster.ComputeCrop(100, 80, 0, 0, 500, 10, 96, 24);
        Assert.Equal(80, wide.Side);
        Assert.Equal(0, wide.Y0);
        Assert.InRange(wide.X0, 0, 100 - wide.Side);
    }

    [Fact]
    public async Task TimelapseClipWriter_PassesFpsAndPathsToTheBuilder()
    {
        var builder = new FakeTimelapseBuilder();
        var writer = new TimelapseClipWriter(builder, new TimelapseOptions { Encoder = "libx264" });

        await writer.WriteAsync(new[] { "a.jpg", "b.jpg" }, Path.Combine(_tmp.Path, "clip.mp4"), 12, CancellationToken.None);

        Assert.NotNull(builder.LastOptions);
        Assert.Equal(12, builder.LastOptions!.Fps);
        Assert.Equal("libx264", builder.LastOptions.Encoder);
        Assert.Equal(2, builder.LastPaths!.Count);
    }
}
