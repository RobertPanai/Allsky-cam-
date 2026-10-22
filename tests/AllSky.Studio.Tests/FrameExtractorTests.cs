using Xunit;
using AllSky.Archive;

namespace AllSky.Studio.Tests;

public sealed class FrameExtractorTests : IDisposable
{
    private readonly TempFolder _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string Video()
    {
        string path = _tmp.FilePath("cielo.mp4");
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });
        }

        return path;
    }

    private string Output => _tmp.FilePath("estratti");

    /// <summary>Runner finto che "produce" n frame nella cartella indicata dal modello di output.</summary>
    private static FakeProcessRunner Producing(int frames)
    {
        return new FakeProcessRunner
        {
            Behaviour = (args, _) =>
            {
                string pattern = args[args.Count - 1];
                string folder = Path.GetDirectoryName(pattern)!;
                for (int i = 1; i <= frames; i++)
                {
                    File.WriteAllBytes(Path.Combine(folder, $"frame_{i:000000}.jpg"), new byte[] { 0xFF, 0xD8, 0xFF });
                }

                return Task.FromResult(new ProcessRunResult(0, string.Empty));
            },
        };
    }

    [Fact]
    public void Argomenti_FpsConIntervalloEListaSicura()
    {
        IReadOnlyList<string> args = FfmpegFrameExtractor.BuildArguments("/video/cielo.mp4", "/out/frame_%06d.jpg", 10);
        var list = args.ToList();

        Assert.Equal("fps=1/10", list[list.IndexOf("-vf") + 1]);
        Assert.Equal("/video/cielo.mp4", list[list.IndexOf("-i") + 1]);
        Assert.Contains("-nostdin", list);
        Assert.Equal("/out/frame_%06d.jpg", list[list.Count - 1]);
    }

    [Fact]
    public void Argomenti_IntervalloFrazionario()
    {
        var list = FfmpegFrameExtractor.BuildArguments("/v.mp4", "/o/frame_%06d.jpg", 0.5).ToList();

        Assert.Equal("fps=1/0.5", list[list.IndexOf("-vf") + 1]);
    }

    [Fact]
    public void Argomenti_IntervalloFuoriRange_Rifiutato()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            FfmpegFrameExtractor.BuildArguments("/v.mp4", "/o/frame_%06d.jpg", 0);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            FfmpegFrameExtractor.BuildArguments("/v.mp4", "/o/frame_%06d.jpg", double.NaN);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            FfmpegFrameExtractor.BuildArguments("/v.mp4", "/o/frame_%06d.jpg", 5000);
        });
    }

    [Fact]
    public async Task Estrazione_OffsetDerivatiDallIntervallo()
    {
        FakeProcessRunner runner = Producing(3);
        var extractor = new FfmpegFrameExtractor("/usr/bin/ffmpeg", runner);

        ExtractResult result = await extractor.ExtractAsync(Video(), Output, 10, CancellationToken.None);

        Assert.Equal(3, result.Frames.Count);
        Assert.Equal(TimeSpan.Zero, result.Frames[0].OffsetFromStart);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Frames[1].OffsetFromStart);
        Assert.Equal(TimeSpan.FromSeconds(20), result.Frames[2].OffsetFromStart);
        Assert.Equal("/usr/bin/ffmpeg", runner.LastFileName);
        Assert.Equal(1, runner.Calls);
        Assert.True(result.Frames[0].FilePath.EndsWith("frame_000001.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Estrazione_RimuoveSoloIFrameVecchi()
    {
        Directory.CreateDirectory(Output);
        File.WriteAllBytes(_tmp.FilePath("estratti/frame_000009.jpg"), new byte[] { 1 });
        File.WriteAllBytes(_tmp.FilePath("estratti/nota.txt"), new byte[] { 1 });
        var extractor = new FfmpegFrameExtractor("/usr/bin/ffmpeg", Producing(1));

        ExtractResult result = await extractor.ExtractAsync(Video(), Output, 5, CancellationToken.None);

        Assert.Single(result.Frames);
        Assert.False(File.Exists(_tmp.FilePath("estratti/frame_000009.jpg")));
        Assert.True(File.Exists(_tmp.FilePath("estratti/nota.txt")));
    }

    [Fact]
    public async Task ErroreDelProcesso_EccezioneSenzaStderrNelMessaggio()
    {
        var runner = new FakeProcessRunner
        {
            Behaviour = (_, _) => Task.FromResult(new ProcessRunResult(1, "boom in " + "/percorso/segreto")),
        };
        var extractor = new FfmpegFrameExtractor("/usr/bin/ffmpeg", runner);

        StudioException error = await Assert.ThrowsAnyAsync<StudioException>(
            () => extractor.ExtractAsync(Video(), Output, 10, CancellationToken.None));

        Assert.Contains("codice 1", error.Message);
        Assert.DoesNotContain("boom", error.Message);
        Assert.Contains("boom", error.StderrTail);
    }

    [Fact]
    public async Task ProcessoSenzaFrame_Errore()
    {
        var extractor = new FfmpegFrameExtractor("/usr/bin/ffmpeg", new FakeProcessRunner());

        await Assert.ThrowsAnyAsync<StudioException>(
            () => extractor.ExtractAsync(Video(), Output, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Annullamento_TerminaIlProcesso()
    {
        var started = new TaskCompletionSource<bool>();
        bool observedCancel = false;
        var runner = new FakeProcessRunner
        {
            Behaviour = async (_, ct) =>
            {
                started.SetResult(true);
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    observedCancel = true;
                    throw;
                }

                return new ProcessRunResult(0, string.Empty);
            },
        };
        var extractor = new FfmpegFrameExtractor("/usr/bin/ffmpeg", runner);
        using var cts = new CancellationTokenSource();

        Task<ExtractResult> running = extractor.ExtractAsync(Video(), Output, 10, cts.Token);
        await started.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(observedCancel);
    }

    [Fact]
    public async Task FfmpegAssente_ErroreSenzaAvviareNulla()
    {
        var runner = new FakeProcessRunner();
        var extractor = new FfmpegFrameExtractor(null, runner);

        StudioException error = await Assert.ThrowsAnyAsync<StudioException>(
            () => extractor.ExtractAsync(Video(), Output, 10, CancellationToken.None));

        Assert.Contains("ffmpeg non trovato", error.Message);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task VideoInesistente_ErroreSenzaAvviareNulla()
    {
        var runner = new FakeProcessRunner();
        var extractor = new FfmpegFrameExtractor("/usr/bin/ffmpeg", runner);

        StudioException error = await Assert.ThrowsAnyAsync<StudioException>(
            () => extractor.ExtractAsync(_tmp.FilePath("manca.mp4"), Output, 10, CancellationToken.None));

        Assert.Contains("manca.mp4", error.Message);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task CartellaConPercentuale_Rifiutata()
    {
        var runner = new FakeProcessRunner();
        var extractor = new FfmpegFrameExtractor("/usr/bin/ffmpeg", runner);

        await Assert.ThrowsAnyAsync<StudioException>(
            () => extractor.ExtractAsync(Video(), _tmp.FilePath("100%"), 10, CancellationToken.None));

        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task IntervalloNonValido_Rifiutato()
    {
        var extractor = new FfmpegFrameExtractor("/usr/bin/ffmpeg", new FakeProcessRunner());

        await Assert.ThrowsAnyAsync<ArgumentOutOfRangeException>(
            () => extractor.ExtractAsync(Video(), Output, -1, CancellationToken.None));
    }

    [Fact]
    public void ProcessoReale_EseguibileInesistente_EccezioneControllata()
    {
        var runner = new SystemProcessRunner();

        Assert.ThrowsAny<StudioException>(() =>
        {
            runner.RunAsync(_tmp.FilePath("ffmpeg-inesistente"), [], CancellationToken.None).GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void ProcessoReale_CodiceDiUscita_LettoDaStderr()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // il test usa /bin/sh
        }

        var runner = new SystemProcessRunner();
        ProcessRunResult result = runner.RunAsync("/bin/sh", ["-c", "echo errore-prova 1>&2; exit 3"], CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("errore-prova", result.StderrTail);
    }
}

public sealed class ArchiveNightSourceTests : IDisposable
{
    private readonly TempFolder _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string SharedFile()
    {
        string path = _tmp.FilePath("frame.jpg");
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, JpegBuilder.Plain());
        }

        return path;
    }

    private static IReadOnlyList<ArchiveFrame> Frames(int count)
    {
        var start = new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);
        return Enumerable.Range(0, count)
            .Select(i => new ArchiveFrame(start.AddSeconds(i * 30).ToString("yyyyMMdd'T'HHmmss'Z'"), start.AddSeconds(i * 30)))
            .ToList();
    }

    [Fact]
    public void Paginazione_LeggeTuttiIFrameInOrdine()
    {
        var reader = new FakeArchiveReader(SharedFile(), Frames(1200));
        var source = new ArchiveNightSource(reader);

        ArchiveNightResult result = source.ListFrames("2026-10-22");

        Assert.Equal(1200, result.Frames.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero), result.Frames[0].TimestampUtc);
        Assert.Equal(1199.0 * 30, (result.Frames[1199].TimestampUtc - result.Frames[0].TimestampUtc).TotalSeconds);
        Assert.Equal(3, reader.ListCalls);
        Assert.Equal("0|500|1000", string.Join("|", reader.PageOffsets));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void FrameSenzaFile_SonoSaltatiEAvvisati()
    {
        var frames = Frames(3);
        var reader = new FakeArchiveReader(SharedFile(), frames);
        reader.MissingIds.Add(frames[1].FrameId);

        ArchiveNightResult result = new ArchiveNightSource(reader).ListFrames("2026-10-22");

        Assert.Equal(2, result.Frames.Count);
        Assert.Contains(result.Warnings, w => w.Contains("senza file JPEG"));
        Assert.DoesNotContain(_tmp.Root, string.Join(" ", result.Warnings));
    }

    [Fact]
    public void NottePrive_Avviso()
    {
        var reader = new FakeArchiveReader(SharedFile(), []);

        ArchiveNightResult result = new ArchiveNightSource(reader).ListFrames("2026-10-22");

        Assert.Empty(result.Frames);
        Assert.Contains(result.Warnings, w => w.Contains("non ha frame"));
    }

    [Fact]
    public void IdentificatoreNonValido_RifiutatoSenzaLeggere()
    {
        var reader = new FakeArchiveReader(SharedFile(), Frames(2));

        Assert.Throws<StudioException>(() =>
        {
            new ArchiveNightSource(reader).ListFrames("../2026-10-22");
        });
        Assert.Equal(0, reader.ListCalls);
    }

    [Fact]
    public void ArchivioVero_LeggeLeNotteSalvate()
    {
        string root = _tmp.FilePath("archivio");
        var archive = new NightArchive(root, new FixedClock(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero)), TimeZoneInfo.Utc);
        byte[] jpeg = JpegBuilder.Plain();
        for (int minute = 0; minute < 3; minute++)
        {
            archive.SaveFrame(
                new DateTimeOffset(2026, 10, 22, 22, minute, 0, TimeSpan.Zero),
                "night",
                jpeg,
                jpeg,
                new FrameMeta(1.0, 100, 500));
        }

        ArchiveNightResult result = new ArchiveNightSource(new NightArchiveReader(archive)).ListFrames("2026-10-22");

        Assert.Equal(3, result.Frames.Count);
        Assert.EndsWith(".jpg", result.Frames[0].FileName);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 22, 0, 0, TimeSpan.Zero), result.Frames[0].TimestampUtc);
        Assert.True(File.Exists(result.Frames[0].FilePath));
    }
}
