using System.Text.Json;
using Xunit;

namespace AllSky.ColorMetrics.Tests;

public sealed class EncoderColorMetadataTests
{
    // Output reale di ffprobe sul timelapse.mp4 della notte simulata 2026-10-22 (tag di matrice bt470bg, nessun primario né curva).
    private const string RealOutput = """
        {
            "programs": [

            ],
            "streams": [
                {
                    "codec_name": "h264",
                    "width": 1280,
                    "height": 960,
                    "pix_fmt": "yuv420p",
                    "color_range": "tv",
                    "color_space": "bt470bg",
                    "r_frame_rate": "25/1"
                }
            ]
        }
        """;

    // Esempio completo: tutti i campi dichiarati.
    private const string FullOutput = """
        {"streams":[{"codec_name":"h264","pix_fmt":"yuv420p","color_range":"tv","color_space":"bt709","color_transfer":"bt709","color_primaries":"bt709","width":1920,"height":1080,"r_frame_rate":"25/1"}]}
        """;

    [Fact]
    public void OutputReale_CampiLettiEAssentiNull()
    {
        EncoderColorInfo e = EncoderColorMetadata.Parse(RealOutput);

        Assert.True(e.HasVideoStream);
        Assert.Equal("h264", e.CodecName);
        Assert.Equal("yuv420p", e.PixFmt);
        Assert.Equal("tv", e.ColorRange);
        Assert.Equal("bt470bg", e.ColorSpace);
        Assert.Null(e.ColorTransfer);
        Assert.Null(e.ColorPrimaries);
        Assert.Equal(1280, e.Width);
        Assert.Equal(960, e.Height);
        Assert.Equal("25/1", e.FrameRate);
        Assert.Contains("color_space dichiarato: bt470bg", e.Observations);
        Assert.Contains(e.Observations, o => o.StartsWith("color_transfer non dichiarato", StringComparison.Ordinal));
        Assert.Contains(e.Observations, o => o.StartsWith("color_primaries non dichiarato", StringComparison.Ordinal));
        Assert.DoesNotContain(e.Observations, o => o.StartsWith("color_space non dichiarato", StringComparison.Ordinal));
    }

    [Fact]
    public void EsempioCompleto_NessunaOsservazioneDiCampoMancante()
    {
        EncoderColorInfo e = EncoderColorMetadata.Parse(FullOutput);

        Assert.Equal("bt709", e.ColorSpace);
        Assert.Equal("bt709", e.ColorTransfer);
        Assert.Equal("bt709", e.ColorPrimaries);
        Assert.Equal(1920, e.Width);
        Assert.DoesNotContain(e.Observations, o => o.Contains("non dichiarato", StringComparison.Ordinal));
    }

    [Fact]
    public void CampiMancanti_TuttiNullEOsservati()
    {
        const string json = """{"streams":[{"codec_name":"h264","width":640,"height":480}]}""";
        EncoderColorInfo e = EncoderColorMetadata.Parse(json);

        Assert.Null(e.PixFmt);
        Assert.Null(e.ColorRange);
        Assert.Null(e.ColorSpace);
        Assert.Null(e.ColorTransfer);
        Assert.Null(e.ColorPrimaries);
        Assert.Null(e.FrameRate);
        Assert.Contains("color_space non dichiarato: il lettore deve indovinare la matrice", e.Observations);
        Assert.Contains(e.Observations, o => o.StartsWith("color_range non dichiarato", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatoPixelDiverso_Osservato()
    {
        EncoderColorInfo e = EncoderColorMetadata.Parse("""{"streams":[{"pix_fmt":"yuv444p","color_space":"bt709"}]}""");
        Assert.Contains("formato dei pixel yuv444p, diverso da yuv420p", e.Observations);
    }

    [Fact]
    public void NessunoStreamVideo_HasVideoFalse()
    {
        EncoderColorInfo e = EncoderColorMetadata.Parse("""{"programs":[],"streams":[]}""");
        Assert.False(e.HasVideoStream);
        Assert.Contains("nessuno stream video nel file", e.Observations);
    }

    [Fact]
    public void JsonSenzaStreams_Eccezione()
    {
        Assert.Throws<JsonException>(() => EncoderColorMetadata.Parse("""{"foo":1}"""));
    }

    [Fact]
    public void TestoNonJson_Eccezione()
    {
        Assert.ThrowsAny<Exception>(() => EncoderColorMetadata.Parse("non e' json"));
    }
}
