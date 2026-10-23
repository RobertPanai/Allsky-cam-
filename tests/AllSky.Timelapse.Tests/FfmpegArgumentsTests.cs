using Xunit;

namespace AllSky.Timelapse.Tests;

public class FfmpegArgumentsTests
{
    private const string Output = "/dati/timelapse/notte.tmp.mp4";

    [Fact]
    public void OutputSize_KeepsSizeWhenBelowMaxWidth()
    {
        Assert.Equal((640, 480), FfmpegArguments.OutputSize(640, 480, 1920));
        Assert.Equal((1920, 1080), FfmpegArguments.OutputSize(1920, 1080, null));
    }

    [Fact]
    public void OutputSize_RoundsDownToEven()
    {
        Assert.Equal((1920, 1080), FfmpegArguments.OutputSize(1921, 1081, null));
    }

    [Fact]
    public void OutputSize_ScalesDownKeepingAspectRatio()
    {
        // 3000x2000 con massimo 1920: altezza 2000 * 1920 / 3000 = 1280.
        Assert.Equal((1920, 1280), FfmpegArguments.OutputSize(3000, 2000, 1920));
    }

    [Fact]
    public void BitrateFor_IsTenthBitPerPixelPerFrame()
    {
        Assert.Equal(5_184_000, FfmpegArguments.BitrateFor(1920, 1080, 25));
    }

    [Fact]
    public void BitrateFor_IsClampedToLimits()
    {
        Assert.Equal(FfmpegArguments.MinBitrate, FfmpegArguments.BitrateFor(64, 48, 25));
        Assert.Equal(FfmpegArguments.MaxBitrate, FfmpegArguments.BitrateFor(7680, 4320, 120));
    }

    [Fact]
    public void Build_H264Mf_UsesBitrateAndStreamingInput()
    {
        var options = new TimelapseOptions { Fps = 25, Encoder = "h264_mf", FfmpegPath = "ffmpeg" };

        IReadOnlyList<string> args = FfmpegArguments.Build(options, Output, 1920, 1080);

        Assert.Equal("-f", args[Index(args, "image2pipe") - 1]);
        Assert.Equal("25", ValueAfter(args, "-framerate"));
        Assert.Equal("mjpeg", ValueAfter(args, "-c:v"));
        Assert.Equal("-", ValueAfter(args, "-i"));
        Assert.Equal("scale=1920:1080:out_color_matrix=bt709:out_range=tv:flags=accurate_rnd+full_chroma_int", ValueAfter(args, "-vf"));
        Assert.Equal("25", ValueAfter(args, "-r"));
        Assert.Equal("h264_mf", ValueAfter(args, "-c:v", skip: 1));
        Assert.Equal("yuv420p", ValueAfter(args, "-pix_fmt"));
        Assert.Equal("5184000", ValueAfter(args, "-b:v"));
        Assert.DoesNotContain("-crf", args);
        Assert.Equal("+faststart", ValueAfter(args, "-movflags"));
        Assert.Equal(Output, args[^1]);
    }

    [Fact]
    public void Build_Libx264_UsesCrf()
    {
        var options = new TimelapseOptions { Fps = 25, Encoder = "libx264", FfmpegPath = "ffmpeg" };

        IReadOnlyList<string> args = FfmpegArguments.Build(options, Output, 64, 48);

        Assert.Equal("23", ValueAfter(args, "-crf"));
        Assert.DoesNotContain("-b:v", args);
    }

    [Fact]
    public void Build_DeclaresBt709ColourAfterPixelFormat()
    {
        // Ordine atteso: encoder, pixel format, poi matrice, primari, trasferimento e range (tutti BT.709, tv).
        var options = new TimelapseOptions { Fps = 25, Encoder = "libx264", FfmpegPath = "ffmpeg" };

        IReadOnlyList<string> args = FfmpegArguments.Build(options, Output, 64, 48);

        int encoderAt = Index(args, "libx264");
        int pixFmtAt = Index(args, "-pix_fmt");
        Assert.True(encoderAt < pixFmtAt, "l'encoder va prima di -pix_fmt");
        Assert.Equal("bt709", ValueAfter(args, "-colorspace"));
        Assert.Equal("bt709", ValueAfter(args, "-color_primaries"));
        Assert.Equal("bt709", ValueAfter(args, "-color_trc"));
        Assert.Equal("tv", ValueAfter(args, "-color_range"));

        int colourAt = Index(args, "-colorspace");
        Assert.True(pixFmtAt < colourAt, "i tag colore seguono -pix_fmt");
        Assert.True(colourAt < Index(args, "-color_primaries"), "ordine: -colorspace, -color_primaries");
        Assert.True(Index(args, "-color_primaries") < Index(args, "-color_trc"), "ordine: -color_primaries, -color_trc");
        Assert.True(Index(args, "-color_trc") < Index(args, "-color_range"), "ordine: -color_trc, -color_range");
        Assert.True(Index(args, "-color_range") < Index(args, "-crf"), "i tag precedono i parametri di qualità");
    }

    [Fact]
    public void Build_ConvertsMatrixInScaleFilterToBt709LimitedRange()
    {
        var options = new TimelapseOptions { Fps = 25, Encoder = "libx264", FfmpegPath = "ffmpeg" };

        IReadOnlyList<string> args = FfmpegArguments.Build(options, Output, 640, 480);

        string filter = ValueAfter(args, "-vf");
        Assert.StartsWith("scale=640:480:", filter);
        Assert.Contains("out_color_matrix=bt709", filter);
        Assert.Contains("out_range=tv", filter);
        Assert.Contains("flags=accurate_rnd+full_chroma_int", filter);
    }

    [Theory]
    [InlineData("libx264")]
    [InlineData("h264_mf")]
    [InlineData("h264_nvenc")]
    public void Build_H264Encoders_AddH264MetadataBitstreamFilter(string encoder)
    {
        var options = new TimelapseOptions { Fps = 25, Encoder = encoder, FfmpegPath = "ffmpeg" };

        IReadOnlyList<string> args = FfmpegArguments.Build(options, Output, 64, 48);

        Assert.Equal(FfmpegArguments.H264MetadataFilter, ValueAfter(args, "-bsf:v"));
        Assert.Equal("h264_metadata=colour_primaries=1:transfer_characteristics=1:matrix_coefficients=1:video_full_range_flag=0",
            FfmpegArguments.H264MetadataFilter);
        Assert.True(Index(args, "-bsf:v") < Index(args, "-movflags"), "il bitstream filter precede -movflags");
    }

    [Fact]
    public void Build_Mpeg4Fallback_HasColourTagsButNoH264Filter()
    {
        // mpeg4 (fallback) non e' H.264: h264_metadata non si applica, i tag colore restano.
        var options = new TimelapseOptions { Fps = 25, Encoder = "mpeg4", FfmpegPath = "ffmpeg" };

        IReadOnlyList<string> args = FfmpegArguments.Build(options, Output, 64, 48);

        Assert.DoesNotContain("-bsf:v", args);
        Assert.Equal("bt709", ValueAfter(args, "-colorspace"));
        Assert.Equal("tv", ValueAfter(args, "-color_range"));
    }

    [Fact]
    public void Build_KeepsPathAsSingleArgument()
    {
        const string spaced = "C:\\Foto notturne\\; rm -rf\\out.tmp.mp4";
        var options = new TimelapseOptions { Encoder = "h264_mf", FfmpegPath = "ffmpeg" };

        IReadOnlyList<string> args = FfmpegArguments.Build(options, spaced, 64, 48);

        Assert.Equal(spaced, args[^1]);
    }

    [Fact]
    public void Build_WithoutEncoder_Throws()
    {
        var options = new TimelapseOptions { Encoder = null, FfmpegPath = "ffmpeg" };

        Assert.Throws<TimelapseException>(() => FfmpegArguments.Build(options, Output, 64, 48));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Build_InvalidFps_Throws(int fps)
    {
        var options = new TimelapseOptions { Fps = fps, Encoder = "h264_mf" };

        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegArguments.Build(options, Output, 64, 48));
    }

    private static int Index(IReadOnlyList<string> args, string value)
    {
        int index = args.ToList().IndexOf(value);
        Assert.True(index >= 0, $"argomento '{value}' assente");
        return index;
    }

    /// <summary>Valore che segue la prima occorrenza di <paramref name="flag"/>; con skip salta le occorrenze precedenti.</summary>
    private static string ValueAfter(IReadOnlyList<string> args, string flag, int skip = 0)
    {
        int seen = 0;
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == flag)
            {
                if (seen == skip)
                {
                    return args[i + 1];
                }

                seen++;
            }
        }

        Assert.Fail($"flag '{flag}' non trovato");
        return string.Empty;
    }
}
