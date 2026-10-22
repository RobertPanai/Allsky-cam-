using AllSky.InfoOverlay;
using Xunit;

namespace AllSky.InfoOverlay.Tests;

public sealed class OverlayLayoutCodecTests
{
    private static string PredefinitoJson() => OverlayLayoutCodec.Serializza(OverlayLayoutDefaults.Predefinito());

    private static OverlayWidget Trova(OverlayLayout layout, OverlayWidgetKind tipo) =>
        layout.Widget.Single(w => w.Tipo == tipo);

    [Fact]
    public void RoundTrip_Predefinito_Restituiscelo_stesso_JSON_senza_avvisi()
    {
        string json = PredefinitoJson();

        var risultato = OverlayLayoutCodec.Leggi(json);

        Assert.Equal(json, OverlayLayoutCodec.Serializza(risultato.Layout));
        Assert.Empty(risultato.Avvisi);
        Assert.False(risultato.Migrata);
        Assert.Equal(OverlayLayoutVersions.Corrente, risultato.VersioneOriginale);
    }

    [Fact]
    public void RoundTrip_Layout_personalizzato_conserva_i_valori()
    {
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Maschera = new OverlayMask(
                OverlayMaskMode.Bande,
                new OverlayCircleMask(0.46, 0.52, 0.48, OverlayMaskEdge.Sfumato, 0.05),
                new OverlayBandMask(0.12, 0.08)),
            Stile = new OverlayStyle(0.035, 0.03),
        };

        var risultato = OverlayLayoutCodec.Leggi(OverlayLayoutCodec.Serializza(layout));

        Assert.Equal(OverlayLayoutCodec.Serializza(layout), OverlayLayoutCodec.Serializza(risultato.Layout));
        Assert.Empty(risultato.Avvisi);
    }

    [Fact]
    public void Formato_JSON_usa_camelCase_e_nomi_italiani()
    {
        string json = PredefinitoJson();

        Assert.Contains("\"versione\": 1", json);
        Assert.Contains("\"maschera\"", json);
        Assert.Contains("\"stile\"", json);
        Assert.Contains("\"widget\"", json);
        Assert.Contains("\"tipo\": \"dataOra\"", json);
        Assert.Contains("\"tipo\": \"testoLibero\"", json);
        Assert.Contains("\"posizione\": \"BL\"", json);
        Assert.Contains("\"abilitato\": true", json);
        Assert.Contains("\"fileLogo\": \"logo.png\"", json);
        Assert.Contains("\"altezzaMaxPercento\"", json);
    }

    [Fact]
    public void Posizione_BandaAlta_si_serializza_con_il_nome_del_piano()
    {
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Widget = new[] { new OverlayWidget(OverlayWidgetKind.Sole, true, OverlayPosition.BandaAlta, 0, OverlayWidgetFormats.Predefinito(OverlayWidgetKind.Sole)) },
        };

        Assert.Contains("\"posizione\": \"BandaAlta\"", OverlayLayoutCodec.Serializza(layout));
    }

    [Fact]
    public void Predefinito_ha_nove_widget_con_DataOra_Sole_e_Luna_attivi_e_maschera_cerchio()
    {
        var layout = OverlayLayoutDefaults.Predefinito();

        Assert.Equal(OverlayLayoutVersions.Corrente, layout.Versione);
        Assert.Equal(OverlayMaskMode.Cerchio, layout.Maschera.Modo);
        Assert.Equal(9, layout.Widget.Count);

        var attivi = layout.Widget.Where(w => w.Abilitato).Select(w => w.Tipo).ToList();
        Assert.Equal(3, attivi.Count);
        Assert.Contains(OverlayWidgetKind.DataOra, attivi);
        Assert.Contains(OverlayWidgetKind.Sole, attivi);
        Assert.Contains(OverlayWidgetKind.Luna, attivi);

        Assert.Equal(OverlayPosition.BL, Trova(layout, OverlayWidgetKind.DataOra).Posizione);
        Assert.Equal(OverlayPosition.BR, Trova(layout, OverlayWidgetKind.Sole).Posizione);
        Assert.Equal(OverlayPosition.BR, Trova(layout, OverlayWidgetKind.Luna).Posizione);
        Assert.True(Trova(layout, OverlayWidgetKind.Luna).Ordine > Trova(layout, OverlayWidgetKind.Sole).Ordine);
    }

    [Fact]
    public void Predefinito_passa_la_validazione()
    {
        Assert.Empty(OverlayLayoutValidator.Valida(OverlayLayoutDefaults.Predefinito()));
    }

    [Fact]
    public void Predefinito_ha_formati_completi_per_ogni_tipo()
    {
        foreach (var widget in OverlayLayoutDefaults.Predefinito().Widget)
        {
            Assert.NotNull(widget.Formato);
        }

        Assert.Equal("yyyy-MM-dd HH:mm:ss", Trova(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.DataOra).Formato!.Pattern);
    }

    [Fact]
    public void Validatore_accetta_un_layout_valido_costruito_a_mano()
    {
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Maschera = new OverlayMask(OverlayMaskMode.Bande, new OverlayCircleMask(0.5, 0.5, 0.5, OverlayMaskEdge.Opaco, 0), new OverlayBandMask(0.2, 0.2)),
        };

        Assert.Empty(OverlayLayoutValidator.Valida(layout));
    }

    [Fact]
    public void Validatore_segnala_valori_fuori_intervallo()
    {
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Maschera = new OverlayMask(OverlayMaskMode.Cerchio, new OverlayCircleMask(1.5, -0.1, 0, OverlayMaskEdge.Sfumato, 0.5), new OverlayBandMask(0.6, 0)),
            Stile = new OverlayStyle(0.5, 0.5),
        };

        var errori = OverlayLayoutValidator.Valida(layout);

        Assert.NotEmpty(errori);
        Assert.Contains(errori, e => e.Contains("centroX"));
        Assert.Contains(errori, e => e.Contains("centroY"));
        Assert.Contains(errori, e => e.Contains("raggio"));
        Assert.Contains(errori, e => e.Contains("sfumatura"));
        Assert.Contains(errori, e => e.Contains("banda superiore"));
        Assert.Contains(errori, e => e.Contains("scala testo"));
        Assert.Contains(errori, e => e.Contains("margine"));
    }

    [Fact]
    public void Validatore_segnala_bande_che_coprono_tutto_il_frame()
    {
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Maschera = new OverlayMask(OverlayMaskMode.Bande, new OverlayCircleMask(0.5, 0.5, 0.5, OverlayMaskEdge.Opaco, 0), new OverlayBandMask(0.5, 0.5)),
        };

        Assert.Contains(OverlayLayoutValidator.Valida(layout), e => e.Contains("coprono tutto"));
    }

    [Fact]
    public void Validatore_segnala_tipo_duplicato_ma_ammette_piu_loghi_e_testi()
    {
        var sole = Trova(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Sole);
        var logo = Trova(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Logo);
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Widget = new[] { sole, sole with { Ordine = 5 }, logo, logo with { Ordine = 6 } },
        };

        var errori = OverlayLayoutValidator.Valida(layout);

        Assert.Single(errori);
        Assert.Contains("Sole", errori[0]);
    }

    [Fact]
    public void Validatore_segnala_percorso_di_logo_non_ammesso()
    {
        var logo = Trova(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Logo);
        var cattivo = logo with { Formato = logo.Formato! with { FileLogo = "../segreto.png" } };
        var layout = OverlayLayoutDefaults.Predefinito() with { Widget = new[] { cattivo } };

        Assert.Contains(OverlayLayoutValidator.Valida(layout), e => e.Contains("logo"));
    }

    [Fact]
    public void Validatore_segnala_campo_di_formato_non_previsto_per_il_tipo()
    {
        var sole = Trova(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Sole);
        var errato = sole with { Formato = sole.Formato! with { Testo = "ciao" } };
        var layout = OverlayLayoutDefaults.Predefinito() with { Widget = new[] { errato } };

        Assert.Contains(OverlayLayoutValidator.Valida(layout), e => e.Contains("non è previsto"));
    }

    [Fact]
    public void Validatore_segnala_versione_diversa()
    {
        var layout = OverlayLayoutDefaults.Predefinito() with { Versione = 7 };

        Assert.Contains(OverlayLayoutValidator.Valida(layout), e => e.Contains("versione 7"));
    }

    [Fact]
    public void Lettura_senza_versione_migra_alla_versione_corrente()
    {
        string json = PredefinitoJson().Replace("\"versione\": 1,", string.Empty);

        var risultato = OverlayLayoutCodec.Leggi(json);

        Assert.True(risultato.Migrata);
        Assert.Equal(0, risultato.VersioneOriginale);
        Assert.Equal(OverlayLayoutVersions.Corrente, risultato.Layout.Versione);
        Assert.Contains(risultato.Avvisi, a => a.Contains("migrato"));
        Assert.Empty(OverlayLayoutValidator.Valida(risultato.Layout));
        Assert.Equal(PredefinitoJson(), OverlayLayoutCodec.Serializza(risultato.Layout));
    }

    [Fact]
    public void Lettura_di_versione_futura_restituisce_i_predefiniti()
    {
        string json = PredefinitoJson().Replace("\"versione\": 1", "\"versione\": 9");

        var risultato = OverlayLayoutCodec.Leggi(json);

        Assert.False(risultato.Migrata);
        Assert.Equal(9, risultato.VersioneOriginale);
        Assert.Contains(risultato.Avvisi, a => a.Contains("più recente"));
        Assert.Equal(PredefinitoJson(), OverlayLayoutCodec.Serializza(risultato.Layout));
    }

    [Fact]
    public void Lettura_di_JSON_non_valido_restituisce_i_predefiniti()
    {
        var risultato = OverlayLayoutCodec.Leggi("{ non è json");

        Assert.NotEmpty(risultato.Avvisi);
        Assert.Equal(PredefinitoJson(), OverlayLayoutCodec.Serializza(risultato.Layout));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[1, 2, 3]")]
    public void Lettura_di_input_vuoto_o_non_oggetto_restituisce_i_predefiniti(string? json)
    {
        var risultato = OverlayLayoutCodec.Leggi(json);

        Assert.NotEmpty(risultato.Avvisi);
        Assert.Equal(PredefinitoJson(), OverlayLayoutCodec.Serializza(risultato.Layout));
    }

    [Fact]
    public void Lettura_scarta_widget_di_tipo_sconosciuto_e_lo_segnala()
    {
        string json = PredefinitoJson().Replace("\"tipo\": \"dataOra\"", "\"tipo\": \"inventato\"");

        var risultato = OverlayLayoutCodec.Leggi(json);

        Assert.Equal(8, risultato.Layout.Widget.Count);
        Assert.DoesNotContain(risultato.Layout.Widget, w => w.Tipo == OverlayWidgetKind.DataOra);
        Assert.Contains(risultato.Avvisi, a => a.Contains("omesso"));
        Assert.Empty(OverlayLayoutValidator.Valida(risultato.Layout));
    }

    [Fact]
    public void Lettura_senza_elenco_widget_usa_i_predefiniti_per_i_widget()
    {
        var risultato = OverlayLayoutCodec.Leggi("{\"versione\": 1}");

        Assert.Equal(9, risultato.Layout.Widget.Count);
        Assert.Contains(risultato.Avvisi, a => a.Contains("widget"));
        Assert.Empty(OverlayLayoutValidator.Valida(risultato.Layout));
    }

    [Fact]
    public void Lettura_corregge_i_valori_fuori_intervallo_e_il_risultato_passa_la_validazione()
    {
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Maschera = new OverlayMask(OverlayMaskMode.Cerchio, new OverlayCircleMask(3, -2, 9, OverlayMaskEdge.Opaco, 4), new OverlayBandMask(0.4, 0.4)),
            Stile = new OverlayStyle(5, -1),
        };

        var risultato = OverlayLayoutCodec.Leggi(OverlayLayoutCodec.Serializza(layout));

        Assert.Equal(OverlayLimits.RaggioMax, risultato.Layout.Maschera.Cerchio.Raggio);
        Assert.Equal(1.0, risultato.Layout.Maschera.Cerchio.CentroX);
        Assert.Equal(0.0, risultato.Layout.Maschera.Cerchio.CentroY);
        Assert.Equal(OverlayLimits.ScalaTestoMax, risultato.Layout.Stile.ScalaTesto);
        Assert.Equal(0.0, risultato.Layout.Stile.Margine);
        Assert.NotEmpty(risultato.Avvisi);
        Assert.Empty(OverlayLayoutValidator.Valida(risultato.Layout));
    }

    [Fact]
    public void Lettura_sostituisce_formati_non_validi_con_i_predefiniti()
    {
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Widget = new[]
            {
                new OverlayWidget(OverlayWidgetKind.Logo, true, OverlayPosition.TR, 0,
                    new OverlayWidgetFormat(FileLogo: "../evil.png", AltezzaMaxPercento: 900, Opacita: 3)),
                new OverlayWidget(OverlayWidgetKind.Sole, true, OverlayPosition.BR, 0,
                    new OverlayWidgetFormat(Decimali: 7, Testo: "fuori posto")),
                new OverlayWidget(OverlayWidgetKind.DataOra, true, OverlayPosition.BL, 0,
                    new OverlayWidgetFormat(Pattern: "", Fuso: OverlayTimeZone.Utc)),
                new OverlayWidget(OverlayWidgetKind.Meteo, true, OverlayPosition.TR, 0,
                    new OverlayWidgetFormat(Campi: new List<OverlayMeteoField>())),
            },
        };

        var risultato = OverlayLayoutCodec.Leggi(OverlayLayoutCodec.Serializza(layout));

        Assert.Empty(OverlayLayoutValidator.Valida(risultato.Layout));
        var logo = risultato.Layout.Widget[0].Formato!;
        Assert.Equal("logo.png", logo.FileLogo);
        Assert.Equal(8.0, logo.AltezzaMaxPercento);
        Assert.Equal(0.9, logo.Opacita);
        var sole = risultato.Layout.Widget[1].Formato!;
        Assert.Equal(0, sole.Decimali);
        Assert.Null(sole.Testo);
        Assert.Equal("yyyy-MM-dd HH:mm:ss", risultato.Layout.Widget[2].Formato!.Pattern);
        Assert.Equal(OverlayTimeZone.Utc, risultato.Layout.Widget[2].Formato!.Fuso);
        Assert.Equal(3, risultato.Layout.Widget[3].Formato!.Campi!.Count);
        Assert.NotEmpty(risultato.Avvisi);
    }

    [Fact]
    public void Lettura_con_una_sezione_non_valida_usa_il_predefinito_per_quella_sezione()
    {
        string json = PredefinitoJson().Replace("\"modo\": \"cerchio\"", "\"modo\": \"sconosciuto\"");

        var risultato = OverlayLayoutCodec.Leggi(json);

        Assert.Equal(OverlayMaskMode.Cerchio, risultato.Layout.Maschera.Modo);
        Assert.Equal(9, risultato.Layout.Widget.Count);
        Assert.NotEmpty(risultato.Avvisi);
        Assert.Empty(OverlayLayoutValidator.Valida(risultato.Layout));
    }
}
