using System.Globalization;
using AllSky.Core.CameraView;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class CameraErrorFormatterTests
{
    // Messaggi reali dell'Engine e dei driver (vedi AlpacaCamera.UnsupportedSensorMessage, AscomComCamera.BuildInfo).
    private const string AlpacaMonochrome =
        "Connessione alla camera 'alpaca:192.168.1.20:11111:0' non riuscita: La camera Alpaca e' monocromatica (SensorType 0): "
        + "questa versione supporta solo sensori Bayer RGGB.. Controllare che la camera sia accesa e collegata, poi riprovare.";

    private const string AscomMonochrome =
        "Connessione alla camera 'ascom-com:ASCOM.Test.Camera' non riuscita: sensore ASCOM di tipo Monochrome non supportato: "
        + "serve un sensore a matrice Bayer (Color o RGGB). Controllare che la camera sia accesa e collegata, poi riprovare.";

    private const string AscomCmyg =
        "Connessione alla camera 'ascom-com:ASCOM.Test.Camera' non riuscita: sensore ASCOM di tipo CMYG non supportato: "
        + "serve un sensore a matrice Bayer (Color o RGGB). Controllare che la camera sia accesa e collegata, poi riprovare.";

    private const string ExposureLimits =
        "Limiti di esposizione del driver non validi (min=0, max=NaN): uso min=0.001, max=30000";

    [Fact]
    public void Classify_AlpacaMonochrome_IsMonochrome()
    {
        Assert.Equal(CameraErrorKind.MonochromeSensor, CameraErrorFormatter.Classify(AlpacaMonochrome));
    }

    [Fact]
    public void Classify_AscomMonochrome_IsMonochrome()
    {
        Assert.Equal(CameraErrorKind.MonochromeSensor, CameraErrorFormatter.Classify(AscomMonochrome));
    }

    [Fact]
    public void Classify_OtherUnsupportedSensor_IsUnsupportedSensor()
    {
        Assert.Equal(CameraErrorKind.UnsupportedSensor, CameraErrorFormatter.Classify(AscomCmyg));
        Assert.Equal(
            CameraErrorKind.UnsupportedSensor,
            CameraErrorFormatter.Classify("Tipo di sensore Alpaca 1 sconosciuto o non supportato: serve una matrice Bayer RGGB."));
    }

    [Theory]
    [InlineData("Il servizio non è avviato: avviarlo prima di collegare la camera.", CameraErrorKind.ServiceNotStarted)]
    [InlineData("Ricerca delle camere non riuscita: porta occupata. Controllare che il driver sia installato.", CameraErrorKind.Discovery)]
    [InlineData("Avvio della cattura non riuscito: eccezione. Controllare la configurazione della camera (guadagno, esposizione massima).", CameraErrorKind.CaptureStart)]
    [InlineData("Connessione alla camera 'sim-1' non riuscita: timeout. Controllare che la camera sia accesa e collegata, poi riprovare.", CameraErrorKind.Connection)]
    [InlineData("La camera 'sim-9' non è disponibile: driver mancante. Controllare l'identificativo scelto.", CameraErrorKind.Connection)]
    [InlineData("Configurazione non valida (camera.gain: Il guadagno non può essere negativo.). Correggere le impostazioni.", CameraErrorKind.ConfigInvalid)]
    [InlineData("Salvataggio della configurazione non riuscito: disco pieno", CameraErrorKind.SaveFailed)]
    [InlineData("Qualcosa di imprevisto", CameraErrorKind.Unknown)]
    public void Classify_KnownEngineMessages(string raw, CameraErrorKind expected)
    {
        Assert.Equal(expected, CameraErrorFormatter.Classify(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_EmptyText_IsUnknown(string? raw)
    {
        Assert.Equal(CameraErrorKind.Unknown, CameraErrorFormatter.Classify(raw));
    }

    [Fact]
    public void ParseExposureLimits_ReadsDriverAndUsedValues()
    {
        ExposureLimitsText? limits = CameraErrorFormatter.ParseExposureLimits(ExposureLimits);

        Assert.NotNull(limits);
        Assert.Equal("0", limits!.DriverMin);
        Assert.Equal("NaN", limits.DriverMax);
        Assert.Equal("0.001", limits.UsedMin);
        Assert.Equal("30000", limits.UsedMax);
    }

    [Fact]
    public void ParseExposureLimits_TextWithoutValues_IsNull()
    {
        Assert.Null(CameraErrorFormatter.ParseExposureLimits("Limiti di esposizione del driver non validi."));
        Assert.Null(CameraErrorFormatter.ParseExposureLimits(null));
    }

    [Fact]
    public void Describe_ExposureLimits_ShowsValuesInEnglish_AndNotAvailableForNaN()
    {
        CameraErrorView view = CameraErrorFormatter.Describe(ExposureLimits, DesktopTexts.English);

        Assert.Equal(CameraErrorKind.DriverExposureLimits, view.Kind);
        Assert.Contains("min 0 ms", view.Message);
        Assert.Contains("max n/a ms", view.Message);
        Assert.Contains("min 0.001 ms", view.Message);
        Assert.Contains("max 30000 ms", view.Message);
        Assert.Null(view.Detail);
    }

    [Fact]
    public void Describe_ExposureLimits_UsesItalianDecimalComma()
    {
        CameraErrorView view = CameraErrorFormatter.Describe(ExposureLimits, DesktopTexts.Italian);

        Assert.Contains("min 0,001 ms", view.Message);
        Assert.Contains("max 30000 ms", view.Message);
        Assert.Contains("n/d", view.Message);
    }

    [Fact]
    public void Describe_ExposureLimitsWithoutValues_UsesSafeGenericText()
    {
        var kind = CameraErrorFormatter.Classify("Limiti di esposizione del driver non validi.");
        Assert.Equal(CameraErrorKind.DriverExposureLimits, kind);

        CameraErrorView view = CameraErrorFormatter.Describe("Limiti di esposizione del driver non validi.", DesktopTexts.English);
        Assert.Equal(DesktopTexts.English.CameraErrorDriverExposureLimitsUnknown, view.Message);
    }

    [Fact]
    public void Describe_Monochrome_ExplainsInPlainLanguage_InBothLanguages()
    {
        CameraErrorView italian = CameraErrorFormatter.Describe(AlpacaMonochrome, DesktopTexts.Italian);
        CameraErrorView english = CameraErrorFormatter.Describe(AlpacaMonochrome, DesktopTexts.English);

        Assert.Equal(DesktopTexts.Italian.CameraErrorMonochrome, italian.Message);
        Assert.Equal(DesktopTexts.English.CameraErrorMonochrome, english.Message);
        Assert.Contains("monocromatica", italian.Message);
        Assert.Contains("monochrome", english.Message);
        Assert.Null(italian.Detail);
    }

    [Fact]
    public void Describe_Null_IsGenericUnknownMessage()
    {
        CameraErrorView view = CameraErrorFormatter.Describe(null, DesktopTexts.Italian);

        Assert.Equal(CameraErrorKind.Unknown, view.Kind);
        Assert.Equal(DesktopTexts.Italian.CameraErrorUnknown, view.Message);
        Assert.Null(view.Detail);
    }

    [Fact]
    public void Describe_NeverShowsStackTraceLines()
    {
        const string withTrace = "Errore nel driver\n   at AllSky.Cameras.Ascom.AscomComCamera.Connect() in AscomComCamera.cs:line 42\n   at System.Threading.Tasks";

        CameraErrorView view = CameraErrorFormatter.Describe(withTrace, DesktopTexts.English);

        Assert.Equal("Errore nel driver", view.Detail);
        Assert.DoesNotContain("   at ", view.Message);
        Assert.DoesNotContain("   at ", view.Detail);
        Assert.DoesNotContain('\n', view.Detail!);
    }

    [Fact]
    public void Summarize_LongText_IsCutAndMarked()
    {
        string longText = new string('a', 1000);

        string? summary = CameraErrorFormatter.Summarize(longText);

        Assert.NotNull(summary);
        Assert.Equal(CameraErrorFormatter.MaxDetailLength + 1, summary!.Length);
        Assert.EndsWith("…", summary);
    }

    [Fact]
    public void Summarize_CollapsesWhitespace_AndDropsBlankText()
    {
        Assert.Equal("uno due", CameraErrorFormatter.Summarize("  uno \t  due  "));
        Assert.Null(CameraErrorFormatter.Summarize("   \r\n  "));
        Assert.Null(CameraErrorFormatter.Summarize(null));
    }

    [Fact]
    public void DescribeValidation_CameraFields_IsConfigInvalidWithPaths()
    {
        var errors = new[]
        {
            new AllSky.Core.ValidationError("camera.gain", "Il guadagno non può essere negativo."),
        };

        CameraErrorView view = CameraErrorFormatter.DescribeValidation(errors, DesktopTexts.English);

        Assert.Equal(CameraErrorKind.ConfigInvalid, view.Kind);
        Assert.Equal(DesktopTexts.English.CameraErrorConfigInvalid, view.Message);
        Assert.Contains("camera.gain", view.Detail);
    }

    [Fact]
    public void DescribeValidation_FilePath_IsSaveFailure()
    {
        var errors = new[]
        {
            new AllSky.Core.ValidationError("file", "Impossibile salvare la configurazione in 'x': accesso negato."),
        };

        CameraErrorView view = CameraErrorFormatter.DescribeValidation(errors, DesktopTexts.Italian);

        Assert.Equal(CameraErrorKind.SaveFailed, view.Kind);
        Assert.Equal(DesktopTexts.Italian.CameraErrorSaveFailed, view.Message);
    }

    [Fact]
    public void Messages_AreNeverEmpty_ForEveryKind()
    {
        string[] samples =
        [
            "Il servizio non è avviato.",
            AlpacaMonochrome,
            AscomCmyg,
            ExposureLimits,
            "Avvio della cattura non riuscito: x.",
            "Ricerca delle camere non riuscita: x.",
            "Connessione alla camera 'a' non riuscita: x.",
            "Configurazione non valida (x).",
            "Impossibile salvare la configurazione.",
            "altro",
        ];

        foreach (string sample in samples)
        {
            Assert.False(string.IsNullOrWhiteSpace(CameraErrorFormatter.Describe(sample, DesktopTexts.Italian).Message));
            Assert.False(string.IsNullOrWhiteSpace(CameraErrorFormatter.Describe(sample, DesktopTexts.English).Message));
        }
    }
}
