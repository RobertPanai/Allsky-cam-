using Xunit;
using AllSky.YouTube;

namespace AllSky.Studio.Tests;

public sealed class ExportValidationTests
{
    [Fact]
    public void Presets_PredefinitiConNomiEValoriAttesi()
    {
        Assert.Equal(4, ExportPresets.All.Count);
        Assert.Equal("Originale", ExportPresets.Originale.Name);
        Assert.Equal(0, ExportPresets.Originale.Width);
        Assert.Equal("Full HD 1920x1080", ExportPresets.FullHd.Name);
        Assert.Equal(1920, ExportPresets.FullHd.Width);
        Assert.Equal(1080, ExportPresets.FullHd.Height);
        Assert.Equal("HD 1280x720", ExportPresets.Hd.Name);
        Assert.Equal("Social 1080x1080", ExportPresets.Social.Name);
        Assert.Equal(20, ExportPresets.Social.Quality);
        foreach (ExportPreset preset in ExportPresets.All)
        {
            Assert.Empty(ExportValidator.ValidatePreset(preset));
        }
    }

    [Fact]
    public void Preset_ValoriFuoriScala_Rifiutati()
    {
        Assert.NotEmpty(ExportValidator.ValidatePreset(ExportPresets.Hd with { Quality = 52 }));
        Assert.NotEmpty(ExportValidator.ValidatePreset(ExportPresets.Hd with { Quality = -1 }));
        Assert.NotEmpty(ExportValidator.ValidatePreset(ExportPresets.Hd with { Width = 15 }));
        Assert.NotEmpty(ExportValidator.ValidatePreset(ExportPresets.Hd with { Width = 1281 }));
        Assert.NotEmpty(ExportValidator.ValidatePreset(ExportPresets.Hd with { Fps = 200 }));
        Assert.NotEmpty(ExportValidator.ValidatePreset(ExportPresets.Hd with { Container = "avi" }));
        Assert.NotEmpty(ExportValidator.ValidatePreset(ExportPresets.Hd with { Name = "" }));
    }

    [Fact]
    public void Preset_ZeroSignificaSorgenteOMontaggio()
    {
        Assert.Empty(ExportValidator.ValidatePreset(new ExportPreset { Name = "x", Width = 0, Height = 0, Fps = 0, Quality = 0 }));
        Assert.Contains(ExportValidator.ValidatePreset(null), e => e.Contains("mancante"));
    }

    [Fact]
    public void CartellaVuota_Errore()
    {
        var errors = ExportValidator.ValidateDestination(new LocalFileDestination { Folder = "  " });
        Assert.Contains(errors, e => e.Contains("cartella"));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a*b")]
    [InlineData("a:b")]
    [InlineData("a|b")]
    [InlineData("a?b")]
    [InlineData("a<b>")]
    [InlineData("a\"b")]
    public void NomeFile_CaratteriVietati_Rifiutati(string template)
    {
        Assert.NotEmpty(ExportFileNames.ValidateTemplate(template));
    }

    [Theory]
    [InlineData("../fuori")]
    [InlineData("a..b")]
    [InlineData("..")]
    public void NomeFile_PercorsiConPuntiPuntiRifiutati(string template)
    {
        Assert.NotEmpty(ExportFileNames.ValidateTemplate(template));
    }

    [Theory]
    [InlineData("{foo}")]
    [InlineData("{project")]
    [InlineData("project}")]
    [InlineData("{project}{project")]
    public void NomeFile_SegnaposteErrati_Rifiutati(string template)
    {
        Assert.NotEmpty(ExportFileNames.ValidateTemplate(template));
    }

    [Fact]
    public void NomeFile_ConSegnaposto_Valido()
    {
        Assert.Empty(ExportFileNames.ValidateTemplate("{project} {date} n{n}"));
    }

    [Fact]
    public void TitoloVuoto_YouTube_Errore()
    {
        var errors = ExportValidator.ValidateDestination(new YouTubeDestination { Title = "   ", Privacy = "private" });
        Assert.Contains(errors, e => e.Contains("titolo"));
    }

    [Fact]
    public void Privacy_Sconosciuta_Errore()
    {
        var errors = ExportValidator.ValidateDestination(new YouTubeDestination { Title = "ok", Privacy = "Friends" });
        Assert.Contains(errors, e => e.Contains("privacy"));
        Assert.True(ExportValidator.TryParsePrivacy("unlisted", out YouTubePrivacy unlisted));
        Assert.Equal(YouTubePrivacy.Unlisted, unlisted);
    }

    [Theory]
    [InlineData("12.5")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("1e3")]
    public void ChatNonIntera_Rifiutata(string chatId)
    {
        var errors = ExportValidator.ValidateDestination(new TelegramDestination { ChatId = chatId });
        Assert.Contains(errors, e => e.Contains("intero"));
    }

    [Fact]
    public void ChatNegativa_Valida()
    {
        Assert.True(ExportValidator.TryParseChatId("-1001234567890", out long chatId));
        Assert.Equal(-1001234567890L, chatId);
        Assert.Empty(ExportValidator.ValidateDestination(new TelegramDestination { ChatId = "-1001234567890" }));
    }

    [Fact]
    public void DidascaliaTroppoLunga_Errore()
    {
        var errors = ExportValidator.ValidateDestination(new TelegramDestination
        {
            ChatId = "42",
            Caption = new string('a', 1025),
        });
        Assert.Contains(errors, e => e.Contains("didascalia"));
    }

    [Fact]
    public void Validazione_NonLanciaMaiNeiCasiNulli()
    {
        Assert.NotEmpty(ExportValidator.ValidateTarget(null));
        Assert.NotEmpty(ExportValidator.ValidateTarget(new ExportTarget { Destination = null }));
        Assert.NotEmpty(ExportValidator.ValidateDestination(null));
        Assert.NotEmpty(ExportValidator.ValidatePreset(null));
    }

    [Fact]
    public void Render_TuttiISegnaposto_NomeAttesoSenzaCaratteriVietati()
    {
        bool ok = ExportFileNames.TryRender("{project}_{date}_{n}", "Notte: 22/10", new DateOnly(2026, 10, 22), 3,
            out string name, out string? error);
        Assert.True(ok, error);
        Assert.Equal("Notte_ 22_10_2026-10-22_3.mp4", name);
    }

    [Fact]
    public void Render_EstensioneGiaPresente_NonRaddoppia()
    {
        Assert.True(ExportFileNames.TryRender("video.MP4", "x", new DateOnly(2026, 1, 1), 1, out string name, out _));
        Assert.Equal("video.MP4", name);
    }

    [Fact]
    public void Render_ProgettoVuotoOSoloPunti_UsaRipiego()
    {
        Assert.True(ExportFileNames.TryRender("{project}", "  ..  ", new DateOnly(2026, 1, 1), 1, out string name, out _));
        Assert.Equal("progetto.mp4", name);
    }

    [Fact]
    public void Render_NomeRiservato_Rifiutato()
    {
        Assert.False(ExportFileNames.TryRender("CON", "x", new DateOnly(2026, 1, 1), 1, out _, out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void ConNumero_AggiungeTraParentesi()
    {
        Assert.Equal("notte (2).mp4", ExportFileNames.WithSuffix("notte.mp4", 2));
    }
}
