using Xunit;
using static AllSky.Studio.Tests.MontageFixtures;

namespace AllSky.Studio.Tests;

public sealed class OperationParserTests
{
    private static MontageOperation Valid(string type, string parametersJson = "{}")
    {
        OperationParseResult result = OperationParser.Parse(Op(type, parametersJson));
        Assert.True(result.IsValid, result.Error);
        return result.Operation!;
    }

    private static string ErrorOf(string type, string parametersJson = "{}")
    {
        OperationParseResult result = OperationParser.Parse(Op(type, parametersJson));
        Assert.False(result.IsValid, "l'operazione doveva essere rifiutata");
        Assert.Null(result.Operation);
        return result.Error!;
    }

    [Fact]
    public void Intervallo_SenzaLimiti_EValido()
    {
        var interval = Assert.IsType<IntervalOperation>(Valid("interval"));

        Assert.Null(interval.From);
        Assert.Null(interval.To);
    }

    [Fact]
    public void Intervallo_DateUtcConZEOffsetZero_VengonoParsate()
    {
        var interval = Assert.IsType<IntervalOperation>(
            Valid("interval", """{ "from": "2026-10-22T20:00:00Z", "to": "2026-10-22T21:30:00.5+00:00" }"""));

        Assert.Equal(new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero), interval.From!.Value);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 21, 30, 0, 500, TimeSpan.Zero), interval.To!.Value);
    }

    [Theory]
    [InlineData("{\"from\":\"2026-10-22T20:00:00\"}", "data ISO 8601 in UTC")]
    [InlineData("{\"to\":\"2026-10-22T20:00:00+02:00\"}", "data ISO 8601 in UTC")]
    [InlineData("{\"from\":\"ieri\"}", "data ISO 8601 in UTC")]
    [InlineData("{\"from\":\"2026-13-40T20:00:00Z\"}", "data ISO 8601 in UTC")]
    [InlineData("{\"from\":5}", "deve essere un testo")]
    [InlineData("{\"from\":\"2026-10-23T00:00:00Z\",\"to\":\"2026-10-22T00:00:00Z\"}", "precedente o uguale a «to»")]
    public void Intervallo_InputErrati_MessaggioInItaliano(string parameters, string expected)
    {
        Assert.Contains(expected, ErrorOf("interval", parameters));
    }

    [Fact]
    public void RimozioneManuale_NomiSenzaDuplicatiEIgnoraMaiuscole()
    {
        var remove = Assert.IsType<RemoveFramesOperation>(
            Valid("removeFrames", """{ "fileNames": ["a.jpg", "A.JPG", "b.jpg"] }"""));

        Assert.Equal("a.jpg|b.jpg", string.Join("|", remove.FileNames));
    }

    [Theory]
    [InlineData("{}", "manca il parametro «fileNames»")]
    [InlineData("{\"fileNames\":\"a.jpg\"}", "deve essere un elenco di nomi di file")]
    [InlineData("{\"fileNames\":[]}", "è vuoto")]
    [InlineData("{\"fileNames\":[\"../x.jpg\"]}", "senza cartelle")]
    [InlineData("{\"fileNames\":[\"C:\\\\dati\\\\x.jpg\"]}", "senza cartelle")]
    [InlineData("{\"fileNames\":[\"  \"]}", "contiene un nome vuoto")]
    [InlineData("{\"fileNames\":[1]}", "deve contenere solo testi")]
    public void RimozioneManuale_InputErrati_MessaggioInItaliano(string parameters, string expected)
    {
        Assert.Contains(expected, ErrorOf("removeFrames", parameters));
    }

    [Fact]
    public void RimozioneAutomatica_SoglieDiDefault()
    {
        var auto = Assert.IsType<AutoRemoveOperation>(Valid("autoRemove", """{ "blankFrames": true }"""));

        Assert.True(auto.BlankFrames);
        Assert.False(auto.ExposureOutliers);
        Assert.False(auto.Clouds);
        Assert.Equal(2.0, auto.BlankStdDevMax);
        Assert.Equal(4.0, auto.MadFactor);
    }

    [Fact]
    public void RimozioneAutomatica_NuvoleAccettateMaNonEseguite()
    {
        var auto = Assert.IsType<AutoRemoveOperation>(Valid("autoRemove", """{ "clouds": true }"""));

        Assert.True(auto.Clouds);
    }

    [Theory]
    [InlineData("{}", "almeno una regola attiva")]
    [InlineData("{\"blankFrames\":true,\"blankStdDevMax\":-1}", "«blankStdDevMax» è fuori dall'intervallo")]
    [InlineData("{\"blankFrames\":true,\"blankStdDevMax\":256}", "«blankStdDevMax» è fuori dall'intervallo")]
    [InlineData("{\"exposureOutliers\":true,\"madFactor\":0}", "«madFactor» è fuori dall'intervallo")]
    [InlineData("{\"exposureOutliers\":true,\"madFactor\":-3}", "«madFactor» è fuori dall'intervallo")]
    [InlineData("{\"exposureOutliers\":true,\"madFactor\":\"4\"}", "«madFactor» deve essere un numero")]
    [InlineData("{\"blankFrames\":\"sì\"}", "«blankFrames» deve essere vero o falso")]
    public void RimozioneAutomatica_InputErrati_MessaggioInItaliano(string parameters, string expected)
    {
        Assert.Contains(expected, ErrorOf("autoRemove", parameters));
    }

    [Fact]
    public void Velocita_FpsDiDefaultVenticinque()
    {
        var speed = Assert.IsType<SpeedOperation>(Valid("speed", """{ "factor": 2 }"""));

        Assert.Equal(2.0, speed.Factor);
        Assert.Equal(25, speed.Fps);
    }

    [Theory]
    [InlineData("{}", "manca il parametro «factor»")]
    [InlineData("{\"factor\":0}", "«factor» è fuori dall'intervallo")]
    [InlineData("{\"factor\":-2}", "«factor» è fuori dall'intervallo")]
    [InlineData("{\"factor\":1000}", "«factor» è fuori dall'intervallo")]
    [InlineData("{\"factor\":\"due\"}", "«factor» deve essere un numero")]
    [InlineData("{\"factor\":2,\"fps\":0}", "«fps» è fuori dall'intervallo")]
    [InlineData("{\"factor\":2,\"fps\":121}", "«fps» è fuori dall'intervallo")]
    [InlineData("{\"factor\":2,\"fps\":25.5}", "«fps» deve essere un numero intero")]
    public void Velocita_InputErrati_MessaggioInItaliano(string parameters, string expected)
    {
        Assert.Contains(expected, ErrorOf("speed", parameters));
    }

    [Fact]
    public void Ritaglio_Valido()
    {
        var crop = Assert.IsType<CropOperation>(Valid("crop", """{ "x": 10, "y": 20, "width": 100, "height": 50 }"""));

        Assert.Equal(new CropOperation(10, 20, 100, 50), crop);
    }

    [Theory]
    [InlineData("{\"x\":0,\"y\":0,\"width\":0,\"height\":10}", "«width» è fuori dall'intervallo")]
    [InlineData("{\"x\":0,\"y\":0,\"width\":10,\"height\":-1}", "«height» è fuori dall'intervallo")]
    [InlineData("{\"x\":-1,\"y\":0,\"width\":10,\"height\":10}", "«x» è fuori dall'intervallo")]
    [InlineData("{\"x\":0,\"y\":0,\"width\":10}", "manca il parametro «height»")]
    [InlineData("{\"x\":0.5,\"y\":0,\"width\":10,\"height\":10}", "«x» deve essere un numero intero")]
    public void Ritaglio_InputErrati_MessaggioInItaliano(string parameters, string expected)
    {
        Assert.Contains(expected, ErrorOf("crop", parameters));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("90")]
    [InlineData("180")]
    [InlineData("270")]
    public void Rotazione_GradiAmmessi(string degrees)
    {
        var rotate = Assert.IsType<RotateOperation>(Valid("rotate", "{ \"degrees\": " + degrees + " }"));

        Assert.Equal(int.Parse(degrees), rotate.Degrees);
    }

    [Theory]
    [InlineData("{\"degrees\":45}", "«degrees» deve essere 0, 90, 180 o 270")]
    [InlineData("{\"degrees\":-90}", "«degrees» è fuori dall'intervallo")]
    [InlineData("{}", "manca il parametro «degrees»")]
    public void Rotazione_InputErrati_MessaggioInItaliano(string parameters, string expected)
    {
        Assert.Contains(expected, ErrorOf("rotate", parameters));
    }

    [Fact]
    public void Specchio_UnAsseBastaEdEValido()
    {
        var mirror = Assert.IsType<MirrorOperation>(Valid("mirror", """{ "horizontal": true }"""));

        Assert.Equal(new MirrorOperation(true, false), mirror);
    }

    [Fact]
    public void Specchio_SenzaAssi_Errore()
    {
        Assert.Contains("almeno un asse", ErrorOf("mirror", """{ "horizontal": false, "vertical": false }"""));
    }

    [Fact]
    public void SchedaTitolo_Valida_TestoRipulito()
    {
        var title = Assert.IsType<TitleCardOperation>(
            Valid("titleCard", """{ "text": "  Notte del 22 ottobre  ", "durationSeconds": 2, "position": "end" }"""));

        Assert.Equal("Notte del 22 ottobre", title.Text);
        Assert.Equal(2.0, title.DurationSeconds);
        Assert.Equal(TitleCardPosition.End, title.Position);
    }

    [Theory]
    [InlineData("{\"text\":\"  \",\"durationSeconds\":2,\"position\":\"start\"}", "«text» non può essere vuoto")]
    [InlineData("{\"text\":\"Notte\",\"durationSeconds\":0.4,\"position\":\"start\"}", "«durationSeconds» è fuori dall'intervallo")]
    [InlineData("{\"text\":\"Notte\",\"durationSeconds\":31,\"position\":\"start\"}", "«durationSeconds» è fuori dall'intervallo")]
    [InlineData("{\"text\":\"Notte\",\"durationSeconds\":2,\"position\":\"middle\"}", "uno di: start, end")]
    [InlineData("{\"durationSeconds\":2,\"position\":\"start\"}", "manca il parametro «text»")]
    [InlineData("{\"text\":\"Notte\",\"durationSeconds\":2}", "manca il parametro «position»")]
    public void SchedaTitolo_InputErrati_MessaggioInItaliano(string parameters, string expected)
    {
        Assert.Contains(expected, ErrorOf("titleCard", parameters));
    }

    [Theory]
    [InlineData("in", "In")]
    [InlineData("out", "Out")]
    [InlineData("crossfade", "Crossfade")]
    public void Dissolvenza_TipiAmmessi(string kind, string expectedKind)
    {
        var fade = Assert.IsType<FadeOperation>(Valid("fade", "{ \"kind\": \"" + kind + "\", \"durationSeconds\": 1 }"));

        Assert.Equal(expectedKind, fade.Kind.ToString());
        Assert.Equal(1.0, fade.DurationSeconds);
    }

    [Theory]
    [InlineData("{\"kind\":\"sfumatura\",\"durationSeconds\":1}", "uno di: in, out, crossfade")]
    [InlineData("{\"kind\":\"in\",\"durationSeconds\":0.05}", "«durationSeconds» è fuori dall'intervallo")]
    [InlineData("{\"kind\":\"in\",\"durationSeconds\":11}", "«durationSeconds» è fuori dall'intervallo")]
    [InlineData("{\"durationSeconds\":1}", "manca il parametro «kind»")]
    public void Dissolvenza_InputErrati_MessaggioInItaliano(string parameters, string expected)
    {
        Assert.Contains(expected, ErrorOf("fade", parameters));
    }

    [Fact]
    public void MaschereEColore_SoloParser_ParametriConservatiTaliQuali()
    {
        var mask = Assert.IsType<InfoMaskOperation>(Valid("infoMask", """{ "sole": true, "soglia": 3 }"""));
        var color = Assert.IsType<ColorCorrectionOperation>(Valid("colorCorrection", """{ "tinta": "fredda" }"""));

        Assert.True(mask.Parameters["sole"].GetBoolean());
        Assert.Equal(3, mask.Parameters["soglia"].GetInt32());
        Assert.Equal("fredda", color.Parameters["tinta"].GetString());
    }

    [Fact]
    public void ParametriSconosciuti_Ignorati()
    {
        var interval = Assert.IsType<IntervalOperation>(Valid("interval", """{ "from": "2026-10-22T20:00:00Z", "futuro": 1 }"""));

        Assert.NotNull(interval.From);
    }

    [Theory]
    [InlineData("sconosciuta", "tipo di operazione non riconosciuto: «sconosciuta»")]
    [InlineData("Interval", "tipo di operazione non riconosciuto: «Interval»")]
    public void TipoSconosciuto_Errore(string type, string expected)
    {
        Assert.Contains(expected, ErrorOf(type));
    }

    [Fact]
    public void TipoVuoto_Errore()
    {
        Assert.Contains("tipo mancante", ErrorOf(" "));
    }

    [Fact]
    public void VoceNulla_Errore()
    {
        OperationParseResult result = OperationParser.Parse(null);

        Assert.False(result.IsValid);
        Assert.Contains("voce vuota", result.Error);
    }
}
