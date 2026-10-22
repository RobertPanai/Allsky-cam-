using AllSky.Archive;
using AllSky.Core;
using AllSky.Detection;
using AllSky.Events;
using AllSky.Satellites;
using AllSky.Timelapse;
using AllSky.Transits;
using Xunit;

namespace AllSky.Events.Tests;

// Salvataggio dei transiti: clip, composito, metadati, regole per verdetto, idempotenza, errori e maschera.
public class TransitEventSaverTests : IDisposable
{
    private const int W = 4;
    private const int H = 3;
    private const string Night = "2026-10-19";
    private const int Norad = 25544;

    // Passaggio dal minuto 2 al minuto 5: finestra [1, 6] in frame (il frame prima dell'inizio e quello dopo la fine).
    private const string ExpectedId = "transit-25544-20261019T210200Z";

    private static readonly byte[] Marker = [0x4D, 0x41, 0x53, 0x4B];

    private readonly EventsTempDir _tmp = new();
    private readonly EventArchive _events;
    private readonly RecordingJpegEncoder _encoder = new();
    private readonly FakeClipWriter _clip = new();

    public TransitEventSaverTests()
    {
        _events = new EventArchive(_tmp.Path);
    }

    public void Dispose() => _tmp.Dispose();

    private static DateTimeOffset Utc(int minute) => EventsFixtures.Utc(minute);

    private static byte[] Pixels(int minute)
    {
        var rgb = new byte[W * H * 3];
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)((i * 7 + minute * 31) % 251);
        }

        return rgb;
    }

    // Frame del minuto indicato: JPEG archiviato su disco, pixel e sidecar in memoria.
    private TransitFrame Frame(int minute)
    {
        DateTimeOffset ts = Utc(minute);
        string id = EventsFixtures.FrameId(ts);
        Directory.CreateDirectory(Path.Combine(_tmp.Path, "archive"));
        string jpeg = Path.Combine(_tmp.Path, "archive", id + ".jpg");
        File.WriteAllBytes(jpeg, EventsFixtures.Jpeg);
        return new TransitFrame
        {
            TimestampUtc = ts,
            FrameId = id,
            JpegPath = jpeg,
            Width = W,
            Height = H,
            Rgb8 = Pixels(minute),
            Sidecar = new FrameSidecar { TimestampUtc = ts, TimestampLocal = ts },
        };
    }

    private IReadOnlyList<TransitFrame> AllFrames() => Enumerable.Range(0, 8).Select(Frame).ToList();

    private static SatellitePass Pass(int startMinute, int endMinute) => new(
        Start: new PassPoint(Utc(startMinute), 300.0, 15.0),
        Culmination: new PassPoint(Utc((startMinute + endMinute) / 2), 90.0, 70.0),
        End: new PassPoint(Utc(endMinute), 120.0, 12.0),
        MaxElevationDeg: 70.0,
        IlluminatedFraction: 1.0,
        IsVisibleToEye: true,
        StartTruncated: false,
        EndTruncated: false);

    private static TransitConfirmation Confirmation(TransitVerdict verdict) => new(
        verdict,
        verdict == TransitVerdict.Identificato ? 0.8 : 0.1,
        verdict == TransitVerdict.PrevistoNonVerificato ? null : Utc(3),
        verdict == TransitVerdict.PrevistoNonVerificato ? 0 : 4,
        120.0);

    private static TransitSaveRequest Request(TransitVerdict verdict, IReadOnlyList<TransitFrame> frames, bool saveAll = false) => new()
    {
        NightId = Night,
        Pass = Pass(2, 5),
        SatelliteName = "ISS (ZARYA)",
        NoradId = Norad,
        Confirmation = Confirmation(verdict),
        Frames = frames,
        SaveAllVisible = saveAll,
    };

    private TransitEventSaver NewSaver(IClipWriter? clip = null, IFrameOverlay? overlay = null, TransitSaveOptions? options = null)
        => new(_events, _encoder, clip ?? _clip, overlay, options);

    private bool EventFolderExists(string id) => Directory.Exists(_events.GetEventFolder(Night, id));

    private static byte[] MaxOfWindow()
    {
        var expected = new byte[W * H * 3];
        for (int m = 1; m <= 6; m++)
        {
            byte[] p = Pixels(m);
            for (int i = 0; i < expected.Length; i++)
            {
                expected[i] = Math.Max(expected[i], p[i]);
            }
        }

        return expected;
    }

    [Fact]
    public async Task Identificato_SalvaEventoCompletoSuDisco()
    {
        IReadOnlyList<TransitFrame> frames = AllFrames();

        TransitSaveResult result = await NewSaver().SaveAsync(Request(TransitVerdict.Identificato, frames));

        Assert.Equal(TransitSaveOutcome.Saved, result.Outcome);
        Assert.Null(result.ClipError);
        Assert.Null(result.OverlayError);
        EventRecord e = result.Event!;
        Assert.Equal(ExpectedId, e.Id);
        Assert.Equal(EventKind.Transit, e.Kind);
        Assert.Equal(StreakClass.Satellite, e.Class);
        Assert.Equal(Night, e.NightId);
        Assert.Equal(Utc(2), e.TimestampUtc);
        Assert.Equal(EventsFixtures.FrameId(Utc(3)), e.SourceFrameId);
        Assert.Equal(0.8, e.Score);
        Assert.Equal(120.0, e.LengthPx);
        Assert.Equal(new EventFiles { Clip = "clip.mp4", Composite = "composite.jpg" }, e.Files);
        Assert.Equal(new AltAz { AltDeg = 15.0, AzDeg = 300.0 }, e.StartAltAz);
        Assert.Equal(new AltAz { AltDeg = 12.0, AzDeg = 120.0 }, e.EndAltAz);

        Assert.NotNull(e.TransitInfo);
        Assert.Equal("ISS (ZARYA)", e.TransitInfo!.SatelliteName);
        Assert.Equal(Norad, e.TransitInfo.NoradId);
        Assert.Equal(Utc(2), e.TransitInfo.StartUtc);
        Assert.Equal(Utc(3), e.TransitInfo.CulminationUtc);
        Assert.Equal(Utc(5), e.TransitInfo.EndUtc);
        Assert.Equal(70.0, e.TransitInfo.MaxElevationDeg);
        Assert.Equal(0.8, e.TransitInfo.Score);
        Assert.Equal(TransitVerdict.Identificato, e.TransitInfo.Verdict);
        Assert.Equal(4, e.TransitInfo.FramesEvaluated);
        Assert.Equal(120.0, e.TransitInfo.VisibleTrackLengthPx);

        // Clip: solo i frame della finestra [1, 6], in ordine, a 25 fps; il finto ha scritto il file.
        Assert.Single(_clip.Calls);
        Assert.Equal(25, _clip.LastFps);
        string[] expectedNames = Enumerable.Range(1, 6).Select(m => EventsFixtures.FrameId(Utc(m)) + ".jpg").ToArray();
        Assert.Equal(expectedNames, _clip.Calls[0].Select(Path.GetFileName).ToArray());
        Assert.True(File.Exists(_events.GetEventFilePath(Night, ExpectedId, "clip.mp4")));

        // Composito su disco, record su disco identico a quello restituito.
        Assert.True(File.Exists(_events.GetEventFilePath(Night, ExpectedId, "composite.jpg")));
        Assert.Equal(e, _events.Get(Night, ExpectedId));
    }

    [Fact]
    public async Task Composito_EIlMassimoPerPixelDeiFrameDellaFinestra()
    {
        await NewSaver().SaveAsync(Request(TransitVerdict.Identificato, AllFrames()));

        // Un solo JPEG passa dall'encoder: il composito. Il frame 0 e il frame 7 sono fuori dalla finestra e non entrano.
        Assert.Single(_encoder.Calls);
        Assert.Equal(W, _encoder.Calls[0].Width);
        Assert.Equal(H, _encoder.Calls[0].Height);
        Assert.Equal(MaxOfWindow(), _encoder.Calls[0].Rgb);
    }

    [Fact]
    public void MaxPerPixel_PrendeIlMassimoDiOgniCanaleEScartaGliInput()
    {
        byte[] a = [10, 200, 3, 0, 0, 0];
        byte[] b = [5, 100, 30, 0, 9, 0];
        byte[] c = [7, 150, 1, 255, 0, 2];

        byte[] result = FrameRaster.MaxPerPixel(new[] { a, b, c }, 2, 1);

        Assert.Equal(new byte[] { 10, 200, 30, 255, 9, 2 }, result);
        Assert.Equal(new byte[] { 10, 200, 3, 0, 0, 0 }, a);
    }

    [Fact]
    public void MaxPerPixel_RifiutaFrameDimensioniDiverseOSenzaFrame()
    {
        Assert.Throws<ArgumentException>(() => FrameRaster.MaxPerPixel(new[] { new byte[6] }, 2, 2));
        Assert.Throws<ArgumentException>(() => FrameRaster.MaxPerPixel(Array.Empty<byte[]>(), 2, 1));
    }

    [Fact]
    public async Task NonIdentificato_NonSalvaSenzaOpzione()
    {
        TransitSaveResult result = await NewSaver().SaveAsync(Request(TransitVerdict.NonIdentificato, AllFrames()));

        Assert.Equal(TransitSaveOutcome.NotSaved, result.Outcome);
        Assert.NotNull(result.Reason);
        Assert.Null(result.Event);
        Assert.Empty(_clip.Calls);
        Assert.False(EventFolderExists(ExpectedId));
        Assert.Empty(_events.List(new EventQuery { IncludeDiscarded = true }));
    }

    [Fact]
    public async Task PrevistoNonVerificato_NonSalvaSenzaOpzione()
    {
        TransitSaveResult result = await NewSaver().SaveAsync(Request(TransitVerdict.PrevistoNonVerificato, AllFrames()));

        Assert.Equal(TransitSaveOutcome.NotSaved, result.Outcome);
        Assert.False(EventFolderExists(ExpectedId));
    }

    [Fact]
    public async Task NonIdentificato_SalvaConOpzioneTuttiIPassaggi_ConVerdettoRegistrato()
    {
        TransitSaveResult result = await NewSaver().SaveAsync(Request(TransitVerdict.NonIdentificato, AllFrames(), saveAll: true));

        Assert.Equal(TransitSaveOutcome.Saved, result.Outcome);
        Assert.Equal(TransitVerdict.NonIdentificato, result.Event!.TransitInfo!.Verdict);
        Assert.Equal("composite.jpg", result.Event.Files.Composite);
        Assert.Equal(EventKind.Transit, result.Event.Kind);
    }

    [Fact]
    public async Task PrevistoNonVerificato_SalvaConOpzione_ConFrameDellaFinestra()
    {
        TransitSaveResult result = await NewSaver().SaveAsync(Request(TransitVerdict.PrevistoNonVerificato, AllFrames(), saveAll: true));

        Assert.Equal(TransitSaveOutcome.Saved, result.Outcome);
        Assert.Equal(TransitVerdict.PrevistoNonVerificato, result.Event!.TransitInfo!.Verdict);
        Assert.Equal(new EventFiles { Clip = "clip.mp4", Composite = "composite.jpg" }, result.Event.Files);
    }

    [Fact]
    public async Task PrevistoNonVerificato_SalvaConOpzione_SenzaFrameNellaFinestra()
    {
        // Solo frame fuori dal passaggio: l'evento c'è, ma senza immagini e senza clip.
        var outside = new[] { Frame(0), Frame(7) };

        TransitSaveResult result = await NewSaver().SaveAsync(Request(TransitVerdict.PrevistoNonVerificato, outside, saveAll: true));

        Assert.Equal(TransitSaveOutcome.Saved, result.Outcome);
        Assert.Equal(new EventFiles(), result.Event!.Files);
        Assert.NotNull(result.ClipError);
        Assert.Empty(_clip.Calls);
        Assert.Equal(string.Empty, result.Event.SourceFrameId);
    }

    [Fact]
    public async Task StessoPassaggio_NonCreaDueEventi()
    {
        TransitEventSaver saver = NewSaver();
        TransitSaveResult first = await saver.SaveAsync(Request(TransitVerdict.Identificato, AllFrames()));
        TransitSaveResult second = await saver.SaveAsync(Request(TransitVerdict.Identificato, AllFrames()));

        // Anche una seconda istanza sullo stesso archivio non crea un duplicato.
        TransitSaveResult third = await NewSaver().SaveAsync(Request(TransitVerdict.Identificato, AllFrames(), saveAll: true));

        Assert.Equal(TransitSaveOutcome.Saved, first.Outcome);
        Assert.Equal(TransitSaveOutcome.AlreadyExists, second.Outcome);
        Assert.Equal(TransitSaveOutcome.AlreadyExists, third.Outcome);
        Assert.Equal(first.Event, second.Event);
        Assert.Single(_events.List(new EventQuery { Kind = EventKind.Transit }));
        Assert.Single(_clip.Calls);
    }

    [Fact]
    public async Task PassaggiDiversiNelLoStessoNumeroCatalogo_SonoEventiDistinti()
    {
        TransitSaveRequest second = Request(TransitVerdict.Identificato, AllFrames()) with { Pass = Pass(1, 4) };

        await NewSaver().SaveAsync(Request(TransitVerdict.Identificato, AllFrames()));
        TransitSaveResult result = await NewSaver().SaveAsync(second);

        Assert.Equal(TransitSaveOutcome.Saved, result.Outcome);
        Assert.Equal(2, _events.List(new EventQuery { Kind = EventKind.Transit }).Count);
    }

    [Fact]
    public async Task ErroreDellaClip_SalvaComunqueEventoConClipError()
    {
        _clip.FailWith = new IOException("ffmpeg non disponibile");

        TransitSaveResult result = await NewSaver().SaveAsync(Request(TransitVerdict.Identificato, AllFrames()));

        Assert.Equal(TransitSaveOutcome.Saved, result.Outcome);
        Assert.Equal("ffmpeg non disponibile", result.ClipError);
        Assert.Null(result.Event!.Files.Clip);
        Assert.Equal("composite.jpg", result.Event.Files.Composite);
        Assert.False(File.Exists(_events.GetEventFilePath(Night, ExpectedId, "clip.mp4")));
        Assert.Equal(result.Event, _events.Get(Night, ExpectedId));
    }

    [Fact]
    public async Task ErroreDiScritturaDelComposito_RimuoveLaCartellaParziale()
    {
        var saver = new TransitEventSaver(_events, new ThrowingJpegEncoder(), _clip);

        await Assert.ThrowsAsync<InvalidOperationException>(() => saver.SaveAsync(Request(TransitVerdict.Identificato, AllFrames())));

        Assert.False(EventFolderExists(ExpectedId));
        Assert.Empty(_events.List(new EventQuery { IncludeDiscarded = true }));
    }

    [Fact]
    public async Task Maschera_LaClipUsaIFrameMascherati_IlCompositoRestaPulito()
    {
        var overlay = new MarkingOverlay();
        var clip = new CapturingClipWriter();

        TransitSaveResult result = await NewSaver(clip, overlay).SaveAsync(Request(TransitVerdict.Identificato, AllFrames()));

        Assert.Equal(TransitSaveOutcome.Saved, result.Outcome);
        Assert.Null(result.OverlayError);
        Assert.Equal(6, clip.Captured.Count);
        foreach (byte[] content in clip.Captured)
        {
            Assert.Equal(Marker, content.Take(Marker.Length));
            Assert.Equal(EventsFixtures.Jpeg, content.Skip(Marker.Length));
        }

        // Ogni frame passa dalla maschera con il proprio sidecar, nell'ordine della finestra.
        Assert.Equal(Enumerable.Range(1, 6).Select(m => (DateTimeOffset?)Utc(m)).ToArray(), overlay.SeenUtc.ToArray());

        // I file mascherati sono temporanei: dopo la clip non restano nella cartella dell'evento.
        string folder = _events.GetEventFolder(Night, ExpectedId);
        Assert.Empty(Directory.GetFiles(folder, "clip-*.jpg"));

        // Il composito usa i pixel puliti.
        Assert.Equal(MaxOfWindow(), _encoder.Calls[0].Rgb);
    }

    [Fact]
    public async Task Maschera_ErroreSuUnFrame_LoSegnalaELaClipRestaCompleta()
    {
        var overlay = new MarkingOverlay { FailAt = Utc(3) };
        var clip = new CapturingClipWriter();

        TransitSaveResult result = await NewSaver(clip, overlay).SaveAsync(Request(TransitVerdict.Identificato, AllFrames()));

        Assert.Equal(TransitSaveOutcome.Saved, result.Outcome);
        Assert.Equal("maschera guasta", result.OverlayError);
        Assert.Equal(6, clip.Captured.Count);
        // Il frame del minuto 3 (terzo della finestra) resta senza maschera.
        Assert.Equal(EventsFixtures.Jpeg, clip.Captured[2]);
        Assert.Equal(Marker, clip.Captured[0].Take(Marker.Length));
        Assert.Equal("clip.mp4", result.Event!.Files.Clip);
    }

    [Fact]
    public async Task Maschera_DisattivataConOpzione_LaClipUsaIJpegArchiviati()
    {
        var overlay = new MarkingOverlay();
        var clip = new CapturingClipWriter();
        var options = new TransitSaveOptions { ApplyOverlayToClip = false };
        IReadOnlyList<TransitFrame> frames = AllFrames();

        await NewSaver(clip, overlay, options).SaveAsync(Request(TransitVerdict.Identificato, frames));

        Assert.Empty(overlay.SeenUtc);
        Assert.Equal(6, clip.Paths.Count);
        Assert.Equal(frames.Skip(1).Take(6).Select(f => f.JpegPath).ToArray(), clip.Paths.ToArray());
    }

    [Fact]
    public async Task Richiesta_FrameConDimensioniIncoerenti_Rifiutata()
    {
        TransitFrame good = Frame(2);
        TransitFrame bad = Frame(3) with { Width = W + 1 };

        await Assert.ThrowsAsync<ArgumentException>(() => NewSaver().SaveAsync(Request(TransitVerdict.Identificato, new[] { good, bad })));
        Assert.False(EventFolderExists(ExpectedId));
    }

    [Fact]
    public void ForTransit_EStabileERiconosciutoDaIdEvento()
    {
        string first = EventIds.ForTransit(Norad, Utc(2));
        string again = EventIds.ForTransit(Norad, Utc(2).ToOffset(TimeSpan.FromHours(2)));

        Assert.Equal(ExpectedId, first);
        Assert.Equal(first, again);
        Assert.NotEqual(first, EventIds.ForTransit(20580, Utc(2)));
        Assert.NotEqual(first, EventIds.ForTransit(Norad, Utc(3)));
        Assert.True(EventIds.IsValid(first));
    }

    [Fact]
    public void Options_ValoriFuoriIntervalloRifiutati()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransitEventSaver(_events, _encoder, null, null, new TransitSaveOptions { ClipFps = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransitEventSaver(_events, _encoder, null, null, new TransitSaveOptions { JpegQuality = 101 }));
    }

    // Maschera finta: antepone un marcatore al JPEG e registra il sidecar ricevuto. Puo' fallire su un istante.
    private sealed class MarkingOverlay : IFrameOverlay
    {
        public List<DateTimeOffset?> SeenUtc { get; } = new();

        public DateTimeOffset? FailAt { get; init; }

        public Task<byte[]> ApplyAsync(byte[] jpeg, FrameSidecar? sidecar, CancellationToken ct)
        {
            SeenUtc.Add(sidecar?.TimestampUtc);
            if (FailAt is { } fail && sidecar?.TimestampUtc == fail)
            {
                throw new InvalidOperationException("maschera guasta");
            }

            return Task.FromResult(Marker.Concat(jpeg).ToArray());
        }
    }

    // Scrittore di clip che legge i file al momento della chiamata: i file mascherati sono temporanei.
    private sealed class CapturingClipWriter : IClipWriter
    {
        public List<byte[]> Captured { get; } = new();

        public List<string> Paths { get; } = new();

        public Task WriteAsync(IReadOnlyList<string> jpegPaths, string outputMp4, int fps, CancellationToken ct)
        {
            foreach (string path in jpegPaths)
            {
                Paths.Add(path);
                Captured.Add(File.ReadAllBytes(path));
            }

            File.WriteAllBytes(outputMp4, [0x00, 0x00, 0x00, 0x18]);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingJpegEncoder : IJpegEncoder
    {
        public byte[] Encode(ReadOnlySpan<byte> rgb8, int width, int height, int quality)
        {
            throw new InvalidOperationException("encoder guasto");
        }
    }
}
