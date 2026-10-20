using Xunit;

namespace AllSky.Timelapse.Tests;

public class EncoderProbeTests
{
    // Output simulato di "ffmpeg -hide_banner -encoders" (stringhe fisse, nessun processo).
    private const string Header =
        "Encoders:\n" +
        " V..... = Video\n" +
        " A..... = Audio\n" +
        " S..... = Subtitle\n" +
        " .F.... = Frame-level multithreading\n" +
        " ------\n";

    private const string Mpeg4 = " V.S... mpeg4                MPEG-4 part 2\n";
    private const string LibX264 = " V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC (codec h264)\n";
    private const string NvEnc = " V....D h264_nvenc           NVIDIA NVENC H.264 encoder (codec h264)\n";
    private const string MediaFoundation = " V..... h264_mf              H264 Encoder MFT (codec h264)\n";
    private const string Aac = " A....D aac                  AAC (Advanced Audio Coding)\n";

    private static readonly EncoderPolicy DefaultPolicy = new();

    [Fact]
    public void Default_PrefersMediaFoundation_OverNvencAndX264()
    {
        string output = Header + LibX264 + NvEnc + MediaFoundation;

        EncoderSelection result = EncoderProbe.Select(output, DefaultPolicy);

        Assert.Equal("h264_mf", result.Encoder);
        Assert.True(result.IsH264);
        Assert.Null(result.Message);
    }

    [Fact]
    public void WithoutMediaFoundation_ChoosesNextPreferred()
    {
        string output = Header + LibX264 + NvEnc;

        EncoderSelection result = EncoderProbe.Select(output, DefaultPolicy);

        Assert.Equal("h264_nvenc", result.Encoder);
        Assert.True(result.IsH264);
        Assert.Null(result.Message);
    }

    [Fact]
    public void OnlyLibx264_WithoutAllowGpl_IsNeverChosen()
    {
        string output = Header + LibX264 + Mpeg4;

        EncoderSelection result = EncoderProbe.Select(output, new EncoderPolicy { AllowGpl = false });

        Assert.Null(result.Encoder);
        Assert.False(result.IsAvailable);
        Assert.False(result.IsH264);
        Assert.NotNull(result.Message);
        Assert.StartsWith("nessun encoder H.264 disponibile", result.Message);
        Assert.Contains("AllowGpl", result.Message);
    }

    [Fact]
    public void OnlyLibx264_WithAllowGpl_IsChosen()
    {
        string output = Header + LibX264;

        EncoderSelection result = EncoderProbe.Select(output, new EncoderPolicy { AllowGpl = true });

        Assert.Equal("libx264", result.Encoder);
        Assert.True(result.IsH264);
        Assert.Null(result.Message);
    }

    [Fact]
    public void NoH264_ReturnsUnavailableWithMessage()
    {
        string output = Header + Mpeg4 + Aac;

        EncoderSelection result = EncoderProbe.Select(output, DefaultPolicy);

        Assert.False(result.IsAvailable);
        Assert.Null(result.Encoder);
        Assert.NotNull(result.Message);
        Assert.StartsWith("nessun encoder H.264 disponibile", result.Message);
        Assert.Contains("Media Feature Pack", result.Message);
    }

    [Fact]
    public void NoH264_WithFallbackMpeg4_ChoosesMpeg4AndReportsIt()
    {
        string output = Header + Mpeg4;

        EncoderSelection result = EncoderProbe.Select(output, new EncoderPolicy { AllowFallbackMpeg4 = true });

        Assert.Equal("mpeg4", result.Encoder);
        Assert.False(result.IsH264);
        Assert.NotNull(result.Message);
        Assert.Contains("mpeg4", result.Message);
    }

    [Fact]
    public void CustomPreferredList_IsRespected()
    {
        string output = Header + NvEnc + MediaFoundation;
        var policy = new EncoderPolicy { Preferred = ["h264_qsv", "h264_nvenc"] };

        EncoderSelection result = EncoderProbe.Select(output, policy);

        Assert.Equal("h264_nvenc", result.Encoder);
    }

    [Fact]
    public void ParseVideoEncoders_IgnoresAudioLegendAndHeaders()
    {
        string output = Header + LibX264 + Aac + Mpeg4;

        IReadOnlySet<string> names = EncoderProbe.ParseVideoEncoders(output);

        Assert.True(names.SetEquals(new[] { "libx264", "mpeg4" }), "encoder video attesi: libx264, mpeg4");
        Assert.DoesNotContain("aac", names);
        Assert.DoesNotContain("=", names);
    }

    [Fact]
    public async Task ProbeAsync_MissingFfmpeg_ReturnsUnavailableWithoutThrowing()
    {
        EncoderSelection result = await EncoderProbe.ProbeAsync(null, DefaultPolicy);

        Assert.False(result.IsAvailable);
        Assert.NotNull(result.Message);
    }
}

public class EncoderProbeTrialTests
{
    [Fact]
    public async Task Trial_SkipsEncoderWhoseTrialFails()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new TempDirectory();
        string ffmpeg = FakeFfmpeg.Create(dir.Path);
        FakeFfmpeg.SetFailing(dir.Path, "fail_trial.txt", "h264_mf");
        var policy = new EncoderPolicy { Preferred = ["h264_mf", "h264_nvenc"] };

        WorkingEncoders working = await EncoderProbe.GetWorkingAsync(ffmpeg, policy);

        Assert.True(working.IsAvailable);
        Assert.Single(working.Encoders);
        Assert.Equal("h264_nvenc", working.Encoders[0].Encoder);
    }

    [Fact]
    public async Task Trial_NoneWorking_ReturnsItalianMessageNamingEncoders()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new TempDirectory();
        string ffmpeg = FakeFfmpeg.Create(dir.Path);
        FakeFfmpeg.SetFailing(dir.Path, "fail_trial.txt", "h264_mf", "h264_nvenc");
        var policy = new EncoderPolicy { Preferred = ["h264_mf", "h264_nvenc"] };

        WorkingEncoders working = await EncoderProbe.GetWorkingAsync(ffmpeg, policy);

        Assert.False(working.IsAvailable);
        Assert.NotNull(working.Message);
        Assert.StartsWith("nessun encoder H.264 disponibile", working.Message);
        Assert.Contains("h264_mf", working.Message);
        Assert.Contains("h264_nvenc", working.Message);
    }

    [Fact]
    public async Task Trial_IsComputedOnce_PerFfmpegAndPolicy()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new TempDirectory();
        string ffmpeg = FakeFfmpeg.Create(dir.Path);
        var policy = new EncoderPolicy { Preferred = ["h264_mf", "h264_nvenc"] };

        WorkingEncoders first = await EncoderProbe.GetWorkingAsync(ffmpeg, policy);
        // Cambiando il comportamento del finto ffmpeg il risultato in cache non deve cambiare.
        FakeFfmpeg.SetFailing(dir.Path, "fail_trial.txt", "h264_mf", "h264_nvenc");
        WorkingEncoders second = await EncoderProbe.GetWorkingAsync(ffmpeg, policy);

        Assert.Same(first, second);
        Assert.Equal("h264_mf", first.Encoders[0].Encoder);
    }

    [Fact]
    public async Task ProbeAsync_ChoosesFromList_WithoutTrial()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var dir = new TempDirectory();
        string ffmpeg = FakeFfmpeg.Create(dir.Path);
        FakeFfmpeg.SetFailing(dir.Path, "fail_trial.txt", "h264_mf");

        EncoderSelection selection = await EncoderProbe.ProbeAsync(ffmpeg, new EncoderPolicy { Preferred = ["h264_mf", "h264_nvenc"] });

        Assert.Equal("h264_mf", selection.Encoder);
    }
}
