using AllSky.Telegram;
using Xunit;

namespace AllSky.Telegram.Tests;

public sealed class LimitsAndPlannerTests
{
    private const OversizeVideoPolicy Stills = OversizeVideoPolicy.SendStillsWithLink;
    private const OversizeVideoPolicy Reencode = OversizeVideoPolicy.Reencode;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData((string?)null)]
    public void Testo_VuotoOSoloSpazi_InvalidInput(string? text)
    {
        TelegramResult? r = TelegramLimits.CheckText(text);
        Assert.NotNull(r);
        Assert.Equal(TelegramErrorKind.InvalidInput, r!.ErrorKind);
    }

    [Fact]
    public void Testo_4096Caratteri_Accettato_4097_Rifiutato()
    {
        Assert.Null(TelegramLimits.CheckText(new string('a', 4096)));
        TelegramResult? r = TelegramLimits.CheckText(new string('a', 4097));
        Assert.Equal(TelegramErrorKind.InvalidInput, r!.ErrorKind);
    }

    [Fact]
    public void Didascalia_0e1024_Accettate_1025_Rifiutata()
    {
        Assert.Null(TelegramLimits.CheckCaption(null));
        Assert.Null(TelegramLimits.CheckCaption(""));
        Assert.Null(TelegramLimits.CheckCaption(new string('b', 1024)));
        Assert.Equal(TelegramErrorKind.InvalidInput, TelegramLimits.CheckCaption(new string('b', 1025))!.ErrorKind);
    }

    [Theory]
    [InlineData("../../segreto.jpg", "segreto.jpg")]
    [InlineData("C:\\dati\\keogram.jpg", "keogram.jpg")]
    [InlineData("notte 2026.mp4", "notte 2026.mp4")]
    public void NomeFile_SenzaPercorso(string input, string expected)
        => Assert.Equal(expected, TelegramLimits.NormalizeFileName(input));

    [Theory]
    [InlineData("")]
    [InlineData("a\"b.jpg")]
    [InlineData("nota\nfile.jpg")]
    [InlineData("cartella/")]
    public void NomeFile_NonValido_Rifiutato(string input)
        => Assert.Null(TelegramLimits.NormalizeFileName(input));

    [Fact]
    public void Foto_SottoSoglia_VaComeFoto()
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Photo, 2_000_000, 4000, 3000, Stills);
        Assert.Equal(DeliveryMethod.Photo, p.Method);
        Assert.Null(p.Refusal);
    }

    [Theory]
    [InlineData(10_000_000L, "Photo")]
    [InlineData(10_000_001L, "Document")]
    [InlineData(50_000_000L, "Document")]
    [InlineData(50_000_001L, "Refused")]
    public void Foto_Peso_LimiteDi10MBPoiDocumentoFinoA50(long bytes, string expected)
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Photo, bytes, null, null, Stills);
        Assert.Equal(expected, p.Method.ToString());
    }

    [Theory]
    [InlineData(6000, 300, "Photo")]      // rapporto 20: entra
    [InlineData(6300, 300, "Document")]   // rapporto 21: documento
    [InlineData(5000, 5000, "Photo")]     // somma 10000: entra
    [InlineData(5001, 5000, "Document")]  // somma 10001: documento
    public void Foto_RapportoESomma(int w, int h, string expected)
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Photo, 1_000_000, w, h, Stills);
        Assert.Equal(expected, p.Method.ToString());
    }

    [Fact]
    public void Foto_DimensioniNonNote_ContaSoloIlPeso()
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Photo, 1_000_000, null, 300, Stills);
        Assert.Equal(DeliveryMethod.Photo, p.Method);
    }

    [Fact]
    public void Foto_DimensioniNonValide_InvalidInput()
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Photo, 1_000_000, 0, 300, Stills);
        Assert.Equal(DeliveryMethod.Refused, p.Method);
        Assert.Equal(TelegramErrorKind.InvalidInput, p.Refusal!.ErrorKind);
    }

    [Fact]
    public void Foto_Oltre50MB_TooLarge()
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Photo, 60_000_000, 4000, 3000, Stills);
        Assert.Equal(TelegramErrorKind.TooLarge, p.Refusal!.ErrorKind);
    }

    [Fact]
    public void Video_Entro50MB_VaComeVideo()
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Video, 49_999_999, null, null, Reencode);
        Assert.Equal(DeliveryMethod.Video, p.Method);
    }

    [Fact]
    public void Video_60MB_PoliticaRicodifica_SuggerisceRicodifica()
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Video, 60_000_000, null, null, Reencode);
        Assert.Equal(TelegramErrorKind.TooLarge, p.Refusal!.ErrorKind);
        Assert.Contains("ricodifica", p.Refusal.Message);
        Assert.DoesNotContain("keogram", p.Refusal.Message);
    }

    [Fact]
    public void Video_60MB_PoliticaStills_SuggerisceKeogramELink()
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Video, 60_000_000, null, null, Stills);
        Assert.Equal(TelegramErrorKind.TooLarge, p.Refusal!.ErrorKind);
        Assert.Contains("keogram", p.Refusal.Message);
        Assert.Contains("link", p.Refusal.Message);
    }

    [Theory]
    [InlineData(50_000_000L, "Document")]
    [InlineData(50_000_001L, "Refused")]
    public void Documento_LimiteDi50MB(long bytes, string expected)
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Document, bytes, null, null, Stills);
        Assert.Equal(expected, p.Method.ToString());
    }

    [Fact]
    public void FileVuoto_InvalidInput()
    {
        DeliveryPlan p = DeliveryPlanner.Plan(MediaKind.Document, 0, null, null, Stills);
        Assert.Equal(TelegramErrorKind.InvalidInput, p.Refusal!.ErrorKind);
    }
}
