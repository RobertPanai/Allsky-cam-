using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace AllSky.Timelapse.Tests;

/// <summary>
/// Test d'integrazione con ffmpeg reale. Se ffmpeg (o ffprobe) non si trova, il test ritorna senza fallire.
/// Usano solo libx264 con AllowGpl=true: nell'ambiente di prova è l'unico encoder funzionante.
/// </summary>
public class TimelapseBuilderTests
{
    private const int FrameCount = 30;
    private const int Fps = 25;

    [Fact]
    public async Task BuildsMp4_WithH264AndExpectedDuration()
    {
        string? ffmpeg = FfmpegLocator.Locate();
        if (ffmpeg is null)
        {
            return;
        }

        // Solo libx264: nell'ambiente di prova h264_nvenc compare in -encoders ma non ha driver.
        var policy = new EncoderPolicy { Preferred = ["libx264"], AllowGpl = true };
        EncoderSelection encoder = await EncoderProbe.ProbeAsync(ffmpeg, policy);
        if (!encoder.IsAvailable)
        {
            return;
        }

        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, FrameCount);
        string output = Path.Combine(dir.Path, "notte.mp4");
        var options = new TimelapseOptions { Fps = Fps, Encoder = encoder.Encoder, FfmpegPath = ffmpeg };
        var reports = new List<double>();

        await new TimelapseBuilder().BuildAsync(jpegs, output, options, new SyncProgress(reports.Add), CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.False(File.Exists(output + ".tmp.mp4"));
        Assert.Equal(FrameCount, reports.Count);
        Assert.Equal(1.0, reports[^1], precision: 9);

        string? ffprobe = TestFiles.FfprobeNextTo(ffmpeg);
        if (ffprobe is null)
        {
            return;
        }

        FileProbe probe = Probe(ffprobe, output);
        Assert.Equal("h264", probe.Codec);
        double expected = FrameCount / (double)Fps;
        Assert.InRange(probe.DurationSeconds, expected - 0.2, expected + 0.2);
    }

    [Fact]
    public async Task Cancellation_KillsFfmpegAndLeavesNoFiles()
    {
        string? ffmpeg = FfmpegLocator.Locate();
        if (ffmpeg is null)
        {
            return;
        }

        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, FrameCount);
        string output = Path.Combine(dir.Path, "annullato.mp4");
        var options = new TimelapseOptions { Fps = Fps, Encoder = "libx264", FfmpegPath = ffmpeg };
        using var cts = new CancellationTokenSource();

        // Annulla dopo il primo frame inviato: il frame successivo non viene piu' letto.
        var progress = new SyncProgress(_ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TimelapseBuilder().BuildAsync(jpegs, output, options, progress, cts.Token));

        Assert.False(File.Exists(output));
        Assert.False(File.Exists(output + ".tmp.mp4"));
    }

    [Fact]
    public async Task MissingJpeg_ThrowsAndRemovesTemp()
    {
        string? ffmpeg = FfmpegLocator.Locate();
        if (ffmpeg is null)
        {
            return;
        }

        using var dir = new TempDirectory();
        List<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 5).ToList();
        jpegs.Insert(3, Path.Combine(dir.Path, "manca.jpg"));
        string output = Path.Combine(dir.Path, "errore.mp4");
        var options = new TimelapseOptions { Fps = Fps, Encoder = "libx264", FfmpegPath = ffmpeg };

        await Assert.ThrowsAnyAsync<IOException>(() =>
            new TimelapseBuilder().BuildAsync(jpegs, output, options, null, CancellationToken.None));

        Assert.False(File.Exists(output));
        Assert.False(File.Exists(output + ".tmp.mp4"));
    }

    [Fact]
    public async Task MissingEncoder_ThrowsTimelapseException()
    {
        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 2);
        var options = new TimelapseOptions { Encoder = null, FfmpegPath = "/non/esiste/ffmpeg" };

        await Assert.ThrowsAsync<TimelapseException>(() =>
            new TimelapseBuilder().BuildAsync(jpegs, Path.Combine(dir.Path, "x.mp4"), options, null, CancellationToken.None));
    }

    private sealed record FileProbe(string? Codec, double DurationSeconds);

    /// <summary>Legge codec del primo stream video e durata del contenitore con ffprobe.</summary>
    private static FileProbe Probe(string ffprobe, string file)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
                 {
                     "-v", "error", "-select_streams", "v:0",
                     "-show_entries", "stream=codec_name:format=duration",
                     "-of", "default=noprint_wrappers=1", file,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string stdout = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);

        string? codec = null;
        double duration = double.NaN;
        foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Trim().Split('=', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            if (parts[0] == "codec_name")
            {
                codec = parts[1];
            }
            else if (parts[0] == "duration")
            {
                duration = double.Parse(parts[1], CultureInfo.InvariantCulture);
            }
        }

        return new FileProbe(codec, duration);
    }
}

public class TimelapseBuilderFallbackTests
{
    private static readonly EncoderPolicy Policy = new() { Preferred = ["h264_mf", "h264_nvenc"] };

    [Fact]
    public async Task AutoPick_RetriesOnceWithNextCandidateWhenRealEncodeFails()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new TempDirectory();
        string ffmpeg = FakeFfmpeg.Create(dir.Path);
        FakeFfmpeg.SetFailing(dir.Path, "fail_real.txt", "h264_mf");
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 3);
        string output = Path.Combine(dir.Path, "retry.mp4");
        var options = new TimelapseOptions { Fps = 25, Encoder = null, FfmpegPath = ffmpeg };

        await new TimelapseBuilder(Policy).BuildAsync(jpegs, output, options, null, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.False(File.Exists(output + ".tmp.mp4"));
        Assert.Equal(new[] { "h264_mf", "h264_nvenc" }, FakeFfmpeg.Used(dir.Path));
    }

    [Fact]
    public async Task AutoPick_AllFail_ThrowsNamingEveryTriedEncoder()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new TempDirectory();
        string ffmpeg = FakeFfmpeg.Create(dir.Path);
        FakeFfmpeg.SetFailing(dir.Path, "fail_real.txt", "h264_mf", "h264_nvenc");
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 3);
        string output = Path.Combine(dir.Path, "fallito.mp4");
        var options = new TimelapseOptions { Fps = 25, Encoder = null, FfmpegPath = ffmpeg };

        TimelapseException ex = await Assert.ThrowsAsync<TimelapseException>(() =>
            new TimelapseBuilder(Policy).BuildAsync(jpegs, output, options, null, CancellationToken.None));

        Assert.Contains("h264_mf", ex.Message);
        Assert.Contains("h264_nvenc", ex.Message);
        Assert.Equal(2, FakeFfmpeg.Used(dir.Path).Length);
        Assert.False(File.Exists(output));
        Assert.False(File.Exists(output + ".tmp.mp4"));
    }

    [Fact]
    public async Task ExplicitEncoder_IsUsedWithoutProbeAndWithoutRetry()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new TempDirectory();
        string ffmpeg = FakeFfmpeg.Create(dir.Path);
        FakeFfmpeg.SetFailing(dir.Path, "fail_real.txt", "h264_mf");
        FakeFfmpeg.SetFailing(dir.Path, "fail_trial.txt", "h264_nvenc");
        IReadOnlyList<string> jpegs = TestFiles.WriteJpegSequence(dir.Path, 3);
        string output = Path.Combine(dir.Path, "esplicito.mp4");
        var options = new TimelapseOptions { Fps = 25, Encoder = "h264_mf", FfmpegPath = ffmpeg };

        TimelapseException ex = await Assert.ThrowsAsync<TimelapseException>(() =>
            new TimelapseBuilder(Policy).BuildAsync(jpegs, output, options, null, CancellationToken.None));

        Assert.Contains("h264_mf", ex.Message);
        Assert.Equal(new[] { "h264_mf" }, FakeFfmpeg.Used(dir.Path));
        Assert.False(File.Exists(output + ".tmp.mp4"));
    }
}
