using AllSky.Archive;
using Xunit;

namespace AllSky.Timelapse.Tests;

/// <summary>Maschera del timelapse: sidecar per indice, errori per frame e passo di composizione.</summary>
public class FrameOverlayTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ComposeAsync_FrameN_RiceveIlSidecarDelFrameN()
    {
        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 4);
        var overlay = new RecordingOverlay();
        var plan = PlanFor(overlay, SidecarsFor(4));

        for (int i = 0; i < jpegs.Count; i++)
        {
            byte[] composed = await FrameOverlayStep.ComposeAsync(jpegs[i], i, plan, CancellationToken.None);

            Assert.Equal(new byte[] { 0xEE, (byte)i }, composed);
        }

        Assert.Equal(4, overlay.Calls.Count);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(Start.AddMinutes(i), overlay.Calls[i].Sidecar?.TimestampUtc);
        }
    }

    [Fact]
    public async Task ComposeAsync_ErroreSuUnFrame_RestituisceOriginale_EConta()
    {
        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 3);
        var overlay = new RecordingOverlay { FailAt = 1 };
        var errors = new List<int>();
        var plan = new FrameOverlayPlan(overlay, SidecarsAt(3), (index, _) => errors.Add(index));

        byte[] first = await FrameOverlayStep.ComposeAsync(jpegs[0], 0, plan, CancellationToken.None);
        byte[] second = await FrameOverlayStep.ComposeAsync(jpegs[1], 1, plan, CancellationToken.None);
        byte[] third = await FrameOverlayStep.ComposeAsync(jpegs[2], 2, plan, CancellationToken.None);

        Assert.Equal(new byte[] { 0xEE, 0 }, first);
        Assert.Equal(await File.ReadAllBytesAsync(jpegs[1]), second);
        Assert.Equal(new byte[] { 0xEE, 2 }, third);
        Assert.Equal(new[] { 1 }, errors);
    }

    [Fact]
    public async Task ComposeAsync_SidecarMancante_ContaErroreEMontaIlFrame()
    {
        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 1);
        var errors = new List<Exception>();
        var plan = new FrameOverlayPlan(
            new RecordingOverlay(),
            _ => throw new InvalidOperationException("sidecar illeggibile"),
            (_, ex) => errors.Add(ex));

        byte[] composed = await FrameOverlayStep.ComposeAsync(jpegs[0], 0, plan, CancellationToken.None);

        Assert.Equal(await File.ReadAllBytesAsync(jpegs[0]), composed);
        Assert.Single(errors);
    }

    [Fact]
    public async Task ComposeAsync_Cancellazione_SiPropaga()
    {
        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var plan = PlanFor(new RecordingOverlay(), SidecarsFor(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            FrameOverlayStep.ComposeAsync(jpegs[0], 0, plan, cts.Token));
    }

    [Fact]
    public async Task NullFrameOverlay_RestituisceIlJpegInvariato()
    {
        byte[] jpeg = [0xFF, 0xD8, 1, 2, 3, 0xFF, 0xD9];

        byte[] result = await NullFrameOverlay.Instance.ApplyAsync(jpeg, null, CancellationToken.None);

        Assert.Equal(jpeg, result);
    }

    [Fact]
    public async Task Builder_ConMaschera_OgniFrameArrivaConIlSuoSidecar_EIlFileEProdotto()
    {
        (string ffmpeg, string encoder)? found = await FindEncoderAsync();
        if (found is null)
        {
            return;
        }

        (string ffmpeg, string encoder) = found.Value;

        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 5);
        var overlay = new RecordingOverlay { PassThrough = true };
        var options = new TimelapseOptions
        {
            Fps = 25,
            Encoder = encoder,
            FfmpegPath = ffmpeg,
            Overlay = PlanFor(overlay, SidecarsFor(5)),
        };
        string output = Path.Combine(dir.Path, "notte.mp4");

        await new TimelapseBuilder().BuildAsync(jpegs, output, options, null, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.Equal(5, overlay.Calls.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(Start.AddMinutes(i), overlay.Calls[i].Sidecar?.TimestampUtc);
        }
    }

    [Fact]
    public async Task Builder_ErroreSuUnFrame_NonBloccaIlTimelapse()
    {
        (string ffmpeg, string encoder)? found = await FindEncoderAsync();
        if (found is null)
        {
            return;
        }

        (string ffmpeg, string encoder) = found.Value;

        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 5);
        var errors = new List<int>();
        var plan = new FrameOverlayPlan(
            new RecordingOverlay { FailAt = 2, PassThrough = true },
            SidecarsAt(5),
            (index, _) => errors.Add(index));
        var options = new TimelapseOptions { Fps = 25, Encoder = encoder, FfmpegPath = ffmpeg, Overlay = plan };
        string output = Path.Combine(dir.Path, "notte.mp4");

        await new TimelapseBuilder().BuildAsync(jpegs, output, options, null, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.Equal(new[] { 2 }, errors);
    }

    /// <summary>ffmpeg e libx264 se disponibili, altrimenti null (il test ritorna senza fallire).</summary>
    private static async Task<(string Ffmpeg, string Encoder)?> FindEncoderAsync()
    {
        string? ffmpeg = FfmpegLocator.Locate();
        if (ffmpeg is null)
        {
            return null;
        }

        // Solo libx264, come gli altri test d'integrazione del progetto: senza probe non serve altro.
        var policy = new EncoderPolicy { Preferred = ["libx264"], AllowGpl = true };
        EncoderSelection selection = await EncoderProbe.ProbeAsync(ffmpeg, policy);
        return selection.IsAvailable ? (ffmpeg, selection.Encoder!) : null;
    }

    private static FrameOverlayPlan PlanFor(IFrameOverlay overlay, IReadOnlyList<FrameSidecar?> sidecars) =>
        new(overlay, index => sidecars[index], (_, _) => { });

    private static Func<int, FrameSidecar?> SidecarsAt(int count)
    {
        IReadOnlyList<FrameSidecar?> sidecars = SidecarsFor(count);
        return index => index < sidecars.Count ? sidecars[index] : null;
    }

    /// <summary>Un sidecar per frame, a un minuto di distanza: il frame N ha l'orario Start + N minuti.</summary>
    private static IReadOnlyList<FrameSidecar?> SidecarsFor(int count) =>
        Enumerable.Range(0, count)
            .Select(i => (FrameSidecar?)FrameSidecars.FromTimestamp(Start.AddMinutes(i), null, TimeZoneInfo.Utc))
            .ToList();

    /// <summary>
    /// Maschera di prova: registra ogni chiamata. Di norma restituisce un marcatore a due byte con l'indice;
    /// con <see cref="PassThrough"/> restituisce il JPEG invariato, cosi' ffmpeg riceve immagini valide.
    /// </summary>
    private sealed class RecordingOverlay : IFrameOverlay
    {
        public List<(byte[] Jpeg, FrameSidecar? Sidecar)> Calls { get; } = new();

        /// <summary>Indice della chiamata che deve fallire (-1 = nessuno).</summary>
        public int FailAt { get; init; } = -1;

        /// <summary>Se vero, restituisce il JPEG ricevuto senza modifiche.</summary>
        public bool PassThrough { get; init; }

        public Task<byte[]> ApplyAsync(byte[] jpeg, FrameSidecar? sidecar, CancellationToken ct)
        {
            int index = Calls.Count;
            Calls.Add((jpeg, sidecar));
            if (index == FailAt)
            {
                throw new InvalidOperationException("errore di prova");
            }

            return Task.FromResult(PassThrough ? jpeg : new byte[] { 0xEE, (byte)index });
        }
    }
}
