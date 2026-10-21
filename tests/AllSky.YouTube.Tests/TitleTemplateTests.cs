using System.Text;
using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class TitleTemplateTests
{
    [Fact]
    public void Parse_TuttiISegnaposti_SonoValidi()
    {
        TemplateParseResult<TitleTemplate> parsed = TitleTemplate.Parse("{data} {notte} {ora} {luogo} {fotogrammi} {durata} {meteore} {transiti}");

        Assert.True(parsed.IsValid);
        Assert.Empty(parsed.Errors);
    }

    [Fact]
    public void Render_TuttiISegnaposti_UsaiFormatiDiDefault()
    {
        TitleTemplate template = TitleTemplate.Parse("{data} {notte} {ora} {luogo} {fotogrammi} {durata} {meteore} {transiti}").Template!;

        Assert.Equal("2026-10-21 2026-10-21 22:15 Osservatorio 120 01:05 3 1", template.Render(Fixtures.Sample()));
    }

    [Fact]
    public void Render_FormatiPersonalizzati_SonoApplicati()
    {
        TitleTemplate template = TitleTemplate.Parse("{data:dd/MM/yyyy} {ora:HH'h'mm}").Template!;

        Assert.Equal("21/10/2026 22h15", template.Render(Fixtures.Sample()));
    }

    [Fact]
    public void Render_NottePartitaIlGiornoPrima_AMezzanotte()
    {
        // Notte iniziata il 21 ottobre al tramonto; la ripresa è scattata alle 00:40 del 22.
        var ctx = new TitleContext(
            new DateTimeOffset(2026, 10, 22, 0, 40, 0, TimeSpan.FromHours(2)),
            new DateTime(2026, 10, 21),
            "Osservatorio",
            10,
            TimeSpan.FromSeconds(30),
            0,
            0);
        TitleTemplate template = TitleTemplate.Parse("Notte del {notte} - {data} {ora}").Template!;

        Assert.Equal("Notte del 2026-10-21 - 2026-10-22 00:40", template.Render(ctx));
    }

    [Fact]
    public void Render_CambioOraLegale_27Marzo2022_OffsetCorretto()
    {
        // Il 27 marzo 2022 alle 02:00 locali l'orologio passa a 03:00 (CEST, +02:00).
        DateTimeOffset before = Fixtures.RomeFromUtc(new DateTime(2022, 3, 27, 0, 30, 0), TimeSpan.FromHours(1));
        DateTimeOffset after = Fixtures.RomeFromUtc(new DateTime(2022, 3, 27, 1, 30, 0), TimeSpan.FromHours(2));
        TitleTemplate template = TitleTemplate.Parse("{ora:HH:mm zzz}").Template!;

        Assert.Equal(TimeSpan.FromHours(1), before.Offset);
        Assert.Equal(TimeSpan.FromHours(2), after.Offset);
        Assert.Equal("01:30 +01:00", template.Render(Fixtures.Sample() with { NowLocal = before }));
        Assert.Equal("03:30 +02:00", template.Render(Fixtures.Sample() with { NowLocal = after }));
    }

    [Fact]
    public void Render_CambioOraLegale_30Ottobre2022_OrarioRipetuto()
    {
        // Il 30 ottobre 2022 alle 03:00 CEST l'orologio torna alle 02:00 CET: le 02:30 compaiono due volte.
        DateTimeOffset first = Fixtures.RomeFromUtc(new DateTime(2022, 10, 30, 0, 30, 0), TimeSpan.FromHours(2));
        DateTimeOffset second = Fixtures.RomeFromUtc(new DateTime(2022, 10, 30, 1, 30, 0), TimeSpan.FromHours(1));
        TitleTemplate template = TitleTemplate.Parse("{data} {ora:HH:mm zzz}").Template!;

        Assert.Equal(TimeSpan.FromHours(2), first.Offset);
        Assert.Equal(TimeSpan.FromHours(1), second.Offset);
        Assert.Equal("2022-10-30 02:30 +02:00", template.Render(Fixtures.Sample() with { NowLocal = first }));
        Assert.Equal("2022-10-30 02:30 +01:00", template.Render(Fixtures.Sample() with { NowLocal = second }));
    }

    [Fact]
    public void Parse_FormatoNonValido_ConPosizione()
    {
        TemplateParseResult<TitleTemplate> parsed = TitleTemplate.Parse("Titolo {data:%}");

        Assert.False(parsed.IsValid);
        Assert.Null(parsed.Template);
        Assert.Single(parsed.Errors);
        Assert.Equal(7, parsed.Errors[0].Position);
    }

    [Fact]
    public void Parse_FormatoConCaratteriDiControllo_eRifiutato()
    {
        TemplateParseResult<TitleTemplate> parsed = TitleTemplate.Parse("{data:yyyy" + "\n" + "MM}");

        Assert.False(parsed.IsValid);
        Assert.Single(parsed.Errors);
        Assert.Equal(0, parsed.Errors[0].Position);
    }

    [Fact]
    public void Parse_SegnaposoSconosciuto_ConPosizione()
    {
        TemplateParseResult<TitleTemplate> parsed = TitleTemplate.Parse("Titolo {foo}");

        Assert.False(parsed.IsValid);
        Assert.Single(parsed.Errors);
        Assert.Equal(7, parsed.Errors[0].Position);
        Assert.Contains("{foo}", parsed.Errors[0].Message);
    }

    [Fact]
    public void Parse_RaccoglieTuttiGliErrori_SenzaLanciare()
    {
        // Segnaposto sconosciuto, formato non valido, '}' orfana e segnaposto non chiuso.
        TemplateParseResult<TitleTemplate> parsed = TitleTemplate.Parse("{foo} {data:%} } {durata");

        Assert.False(parsed.IsValid);
        Assert.Equal(4, parsed.Errors.Count);
    }

    [Fact]
    public void Parse_FormatoVuoto_eRifiutato()
    {
        Assert.Single(TitleTemplate.Parse("{ora:}").Errors);
    }

    [Fact]
    public void Parse_FormatoSuSegnaposoCheNonLoAccetta_eRifiutato()
    {
        Assert.Single(TitleTemplate.Parse("{luogo:x}").Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_ModelloVuoto_eRifiutato(string? template)
    {
        TemplateParseResult<TitleTemplate> parsed = TitleTemplate.Parse(template!);

        Assert.False(parsed.IsValid);
        Assert.Single(parsed.Errors);
    }

    [Fact]
    public void Render_GraffeLetterali_SonoStampate()
    {
        TitleTemplate template = TitleTemplate.Parse("{{data}} {{{luogo}}}").Template!;

        Assert.Equal("{data} {Osservatorio}", template.Render(Fixtures.Sample()));
    }

    [Fact]
    public void Render_SpaziECaratteriDiControllo_SonoNormalizzati()
    {
        TitleTemplate template = TitleTemplate.Parse("  Notte\tdel \u0007  {luogo}\r\n  ").Template!;

        Assert.Equal("Notte del Osservatorio", template.Render(Fixtures.Sample()));
    }

    [Fact]
    public void Render_AngolariRimossi()
    {
        TitleTemplate template = TitleTemplate.Parse("Titolo <x> > fine").Template!;

        Assert.Equal("Titolo x fine", template.Render(Fixtures.Sample()));
    }

    [Fact]
    public void Render_LocationConSurrogatiIsolati_SonoSostituiti()
    {
        var ctx = Fixtures.Sample() with { Location = "x\uD800y" };
        TitleTemplate template = TitleTemplate.Parse("{luogo}").Template!;

        Assert.Equal("x\uFFFDy", template.Render(ctx));
    }

    [Fact]
    public void Render_TroncaSenzaSpezzareUnaCoppiaSurrogata()
    {
        // 98 unità + emoji (2 unità) + 3 = 103 unità: il taglio cadrebbe a metà dell'emoji, quindi lo si scarta.
        string title = new string('a', 98) + "\U0001F30C" + "bbb";
        TitleTemplate template = TitleTemplate.Parse(title).Template!;

        Assert.Equal(new string('a', 98) + "…", template.Render(Fixtures.Sample()));
    }

    [Fact]
    public void Render_TitoloDentroIlLimite_NonViene_Troncato()
    {
        string title = new string('a', 96) + "\U0001F30C";
        TitleTemplate template = TitleTemplate.Parse(title).Template!;

        string rendered = template.Render(Fixtures.Sample());
        Assert.Equal(title, rendered);
        Assert.True(rendered.Length <= YouTubeLimits.TitleMaxLength);
    }

    [Fact]
    public void Render_RispettaIlLimiteDiLunghezza()
    {
        TitleTemplate template = TitleTemplate.Parse(new string('x', 300)).Template!;

        string rendered = template.Render(Fixtures.Sample());
        Assert.True(rendered.Length <= YouTubeLimits.TitleMaxLength);
        Assert.EndsWith("…", rendered);
    }

    [Fact]
    public void Render_DurataOltreUnOra_RestaInMinuti()
    {
        var ctx = Fixtures.Sample() with { VideoDuration = new TimeSpan(1, 15, 5) };
        TitleTemplate template = TitleTemplate.Parse("{durata}").Template!;

        Assert.Equal("75:05", template.Render(ctx));
    }

    [Fact]
    public void Preview_ModelloValido_RestituisceTesto()
    {
        TemplatePreview preview = TitleTemplate.Preview("Notte {notte}", Fixtures.Sample());

        Assert.True(preview.IsValid);
        Assert.Equal("Notte 2026-10-21", preview.Text);
        Assert.Empty(preview.Errors);
    }

    [Fact]
    public void Preview_ModelloNonValido_RestituisceErrori()
    {
        TemplatePreview preview = TitleTemplate.Preview("Notte {nope}", Fixtures.Sample());

        Assert.False(preview.IsValid);
        Assert.Null(preview.Text);
        Assert.Single(preview.Errors);
    }

    [Fact]
    public void TitleContext_ValoriNegativi_Lanciano()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = Fixtures.Sample() with { Frames = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = Fixtures.Sample() with { VideoDuration = TimeSpan.FromSeconds(-1) });
    }
}
