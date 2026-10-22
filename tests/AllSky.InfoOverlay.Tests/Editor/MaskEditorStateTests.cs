using System.Globalization;
using System.Text.Json;
using AllSky.Core;
using AllSky.InfoOverlay;
using AllSky.InfoOverlay.Editor;
using Xunit;

namespace AllSky.InfoOverlay.Tests.Editor;

public sealed class MaskEditorStateTests
{
    [Fact]
    public void Predefinito_ha_DataOra_BL_Sole_e_Luna_BR_maschera_a_cerchio_e_il_resto_spento()
    {
        var stato = new MaskEditorState();

        Assert.Equal(OverlayMaskMode.Cerchio, stato.Modo);
        Assert.Equal(OverlayPosition.BL, PosizioneDi(stato, OverlayWidgetKind.DataOra));
        Assert.True(AbilitatoDi(stato, OverlayWidgetKind.DataOra));
        Assert.True(AbilitatoDi(stato, OverlayWidgetKind.Sole));
        Assert.Equal(OverlayPosition.BR, PosizioneDi(stato, OverlayWidgetKind.Sole));
        Assert.True(AbilitatoDi(stato, OverlayWidgetKind.Luna));
        Assert.Equal(OverlayPosition.BR, PosizioneDi(stato, OverlayWidgetKind.Luna));
        Assert.All(
            stato.Widget.Where(w => w.Tipo is not (OverlayWidgetKind.DataOra or OverlayWidgetKind.Sole or OverlayWidgetKind.Luna)),
            w => Assert.False(w.Abilitato));
    }

    [Fact]
    public void Predefinito_passa_la_validazione_e_coincide_con_il_layout_di_default()
    {
        var stato = new MaskEditorState();

        Assert.Empty(stato.Valida());
        Assert.Equal(Firma(OverlayLayoutDefaults.Predefinito()), Firma(stato.ToLayout()));
    }

    [Fact]
    public void Da_configurazione_senza_layout_usa_il_predefinito_senza_avvisi()
    {
        var stato = MaskEditorState.Da(new AppConfig.OverlayConfig());

        Assert.Empty(stato.AvvisiLettura);
        Assert.False(stato.ApplyToTimelapse);
        Assert.Equal(OverlayMaskMode.Cerchio, stato.Modo);
    }

    [Fact]
    public void Da_layout_non_leggibile_usa_il_predefinito_e_segnala()
    {
        using var json = JsonDocument.Parse("""{"versione": 1, "maschera": 5}""");
        var config = new AppConfig.OverlayConfig { Layout = json.RootElement.Clone() };

        var stato = MaskEditorState.Da(config);

        Assert.NotEmpty(stato.AvvisiLettura);
        Assert.Empty(stato.Valida());
    }

    [Fact]
    public void Un_tipo_mancante_nel_file_compare_comunque_nella_lista_spento()
    {
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Widget = new[] { new OverlayWidget(OverlayWidgetKind.Sole, true, OverlayPosition.BR, 0, OverlayWidgetFormats.Predefinito(OverlayWidgetKind.Sole)) },
        };
        using var json = JsonDocument.Parse(OverlayLayoutCodec.Serializza(layout));
        var config = new AppConfig.OverlayConfig { Layout = json.RootElement.Clone() };

        var stato = MaskEditorState.Da(config);

        Assert.Equal(1, stato.Widget.Count(w => w.Tipo == OverlayWidgetKind.Sole));
        Assert.Equal(1, stato.Widget.Count(w => w.Tipo == OverlayWidgetKind.Esposizione));
        Assert.False(AbilitatoDi(stato, OverlayWidgetKind.Esposizione));
    }

    [Fact]
    public void AggiornaConfig_scrive_layout_e_interruttori_e_rilegge_lo_stesso_stato()
    {
        var stato = new MaskEditorState { ApplyToTimelapse = true, ApplyToLive = true, ApplyToPublish = false };
        stato.Aggiungi(OverlayWidgetKind.TestoLibero, OverlayPosition.TL);

        AppConfig.OverlayConfig salvata = stato.AggiornaConfig(new AppConfig.OverlayConfig { Enabled = false });

        Assert.False(salvata.Enabled);
        Assert.True(salvata.ApplyToTimelapse);
        Assert.True(salvata.ApplyToLive);
        Assert.False(salvata.ApplyToPublish);
        Assert.NotNull(salvata.Layout);

        var riletta = MaskEditorState.Da(salvata);
        Assert.Empty(riletta.AvvisiLettura);
        Assert.Equal(Firma(stato.ToLayout()), Firma(riletta.ToLayout()));
    }

    [Fact]
    public void ToLayout_assegna_ordine_come_posizione_nella_lista()
    {
        var stato = new MaskEditorState();

        OverlayLayout layout = stato.ToLayout();

        foreach (IGrouping<OverlayPosition, OverlayWidget> gruppo in layout.Widget.GroupBy(w => w.Posizione))
        {
            Assert.Equal(
                Enumerable.Range(0, gruppo.Count()),
                gruppo.Select(w => w.Ordine));
        }
    }

    [Fact]
    public void SpostaSu_scambia_solo_con_la_voce_della_stessa_posizione()
    {
        var stato = new MaskEditorState();
        int indiceSole = IndiceDi(stato, OverlayWidgetKind.Sole);
        int indiceLuna = IndiceDi(stato, OverlayWidgetKind.Luna);
        OverlayWidgetKind[] prima = Tipi(stato);

        Assert.True(stato.SpostaSu(indiceLuna));

        // Luna passa davanti a Sole nel gruppo BR; le altre voci restano dove sono.
        Assert.Equal(OverlayWidgetKind.Luna, stato.Widget[indiceSole].Tipo);
        Assert.Equal(OverlayWidgetKind.Sole, stato.Widget[indiceLuna].Tipo);
        Assert.Equal(
            prima.Where((_, i) => i != indiceSole && i != indiceLuna),
            Tipi(stato).Where((_, i) => i != indiceSole && i != indiceLuna));
    }

    [Fact]
    public void SpostaSu_della_prima_voce_del_gruppo_non_fa_nulla()
    {
        var stato = new MaskEditorState();
        OverlayWidgetKind[] prima = Tipi(stato);

        Assert.False(stato.SpostaSu(IndiceDi(stato, OverlayWidgetKind.Sole)));
        Assert.Equal(prima, Tipi(stato));
    }

    [Fact]
    public void SpostaGiu_cambia_ordine_solo_dentro_la_stessa_posizione()
    {
        var stato = new MaskEditorState();
        // BL contiene DataOra ed Esposizione (spento); il gruppo BR non deve cambiare.
        OverlayWidgetKind[] brPrima = Gruppo(stato, OverlayPosition.BR);

        Assert.True(stato.SpostaGiu(IndiceDi(stato, OverlayWidgetKind.DataOra)));

        Assert.Equal(new[] { OverlayWidgetKind.Esposizione, OverlayWidgetKind.DataOra }, Gruppo(stato, OverlayPosition.BL));
        Assert.Equal(brPrima, Gruppo(stato, OverlayPosition.BR));
    }

    [Fact]
    public void SpostaGiu_dell_ultima_voce_del_gruppo_non_fa_nulla()
    {
        var stato = new MaskEditorState();

        Assert.False(stato.SpostaGiu(IndiceDi(stato, OverlayWidgetKind.Luna)));
    }

    [Fact]
    public void Aggiungi_rifiuta_un_tipo_non_ripetibile_gia_presente_e_accetta_i_ripetibili()
    {
        var stato = new MaskEditorState();

        Assert.Equal(-1, stato.Aggiungi(OverlayWidgetKind.Sole, OverlayPosition.TL));
        Assert.True(stato.Aggiungi(OverlayWidgetKind.TestoLibero, OverlayPosition.TL) >= 0);
        Assert.True(stato.Aggiungi(OverlayWidgetKind.TestoLibero, OverlayPosition.TL) >= 0);
    }

    [Fact]
    public void Rimuovi_accetta_solo_i_tipi_ripetibili()
    {
        var stato = new MaskEditorState();
        int indiceSole = IndiceDi(stato, OverlayWidgetKind.Sole);
        int indiceTesto = stato.Aggiungi(OverlayWidgetKind.TestoLibero, OverlayPosition.TL);
        int prima = stato.Widget.Count;

        Assert.False(stato.Rimuovi(indiceSole));
        Assert.True(stato.Rimuovi(indiceTesto));
        Assert.Equal(prima - 1, stato.Widget.Count);
    }

    [Fact]
    public void Formato_conserva_solo_i_campi_previsti_dal_tipo()
    {
        var stato = new MaskEditorState();
        int indiceSole = IndiceDi(stato, OverlayWidgetKind.Sole);
        stato.Widget[indiceSole].Formato = new OverlayWidgetFormat(Decimali: 2, Testo: "fuori posto", MostraGain: true);

        OverlayWidget voce = stato.ToLayout().Widget.Single(w => w.Tipo == OverlayWidgetKind.Sole);

        Assert.NotNull(voce.Formato);
        Assert.Equal(2, voce.Formato!.Decimali);
        Assert.Null(voce.Formato.Testo);
        Assert.Null(voce.Formato.MostraGain);
        Assert.Empty(stato.Valida());
    }

    [Fact]
    public void Formato_previsto_ma_mancante_prende_il_predefinito()
    {
        var stato = new MaskEditorState();
        stato.Widget[IndiceDi(stato, OverlayWidgetKind.DataOra)].Formato = new OverlayWidgetFormat();

        OverlayWidget voce = stato.ToLayout().Widget.Single(w => w.Tipo == OverlayWidgetKind.DataOra);

        Assert.Equal("yyyy-MM-dd HH:mm:ss", voce.Formato!.Pattern);
        Assert.Equal(OverlayTimeZone.Locale, voce.Formato.Fuso);
        Assert.Empty(stato.Valida());
    }

    [Fact]
    public void Valida_segnala_un_pattern_data_vuoto_o_troppo_lungo_e_accetta_uno_corretto()
    {
        var stato = new MaskEditorState();
        int indiceDati = IndiceDi(stato, OverlayWidgetKind.DataOra);

        stato.Widget[indiceDati].Formato = new OverlayWidgetFormat(Pattern: "", Fuso: OverlayTimeZone.Utc);
        Assert.NotEmpty(stato.Valida());

        stato.Widget[indiceDati].Formato = new OverlayWidgetFormat(Pattern: new string('y', 41), Fuso: OverlayTimeZone.Utc);
        Assert.NotEmpty(stato.Valida());

        stato.Widget[indiceDati].Formato = new OverlayWidgetFormat(Pattern: "HH:mm", Fuso: OverlayTimeZone.Utc);
        Assert.Empty(stato.Valida());
    }

    [Fact]
    public void Valida_segnala_opacita_e_bande_fuori_intervallo()
    {
        var stato = new MaskEditorState();
        int indiceLogo = stato.AggiungiLogo("logo.png", OverlayPosition.TR);
        stato.Widget[indiceLogo].Formato = stato.Widget[indiceLogo].Formato! with { Opacita = 1.5 };
        Assert.NotEmpty(stato.Valida());

        stato.Widget[indiceLogo].Formato = stato.Widget[indiceLogo].Formato! with { Opacita = 0.8 };
        Assert.Empty(stato.Valida());

        stato.Modo = OverlayMaskMode.Bande;
        stato.Bande = new OverlayBandMask(0.5, 0.5);
        Assert.NotEmpty(stato.Valida());
    }

    [Fact]
    public void RipristinaPredefiniti_riporta_il_layout_ma_non_gli_interruttori_di_applicazione()
    {
        var stato = new MaskEditorState { ApplyToTimelapse = true, ApplyToLive = true };
        stato.Modo = OverlayMaskMode.Nessuna;
        stato.Aggiungi(OverlayWidgetKind.TestoLibero, OverlayPosition.TL);
        stato.Widget[IndiceDi(stato, OverlayWidgetKind.Meteo)].Abilitato = true;

        stato.RipristinaPredefiniti();

        Assert.Equal(Firma(OverlayLayoutDefaults.Predefinito()), Firma(stato.ToLayout()));
        Assert.True(stato.ApplyToTimelapse);
        Assert.True(stato.ApplyToLive);
    }

    [Fact]
    public void LoghiUsati_include_anche_i_loghi_disabilitati_e_solo_nomi_di_file()
    {
        var stato = new MaskEditorState();
        int indice = stato.AggiungiLogo("osservatorio.png", OverlayPosition.TR);
        stato.Widget[indice].Abilitato = false;

        IReadOnlyList<string> usati = stato.LoghiUsati();

        Assert.Contains("osservatorio.png", usati);
        Assert.False(usati[0].Contains('/') || usati[0].Contains('\\'));
    }

    [Fact]
    public void AggiungiLogo_rifiuta_un_nome_con_percorso()
    {
        var stato = new MaskEditorState();

        Assert.Throws<ArgumentException>(() => stato.AggiungiLogo("..\\esterno.png", OverlayPosition.TR));
        Assert.Throws<ArgumentException>(() => stato.AggiungiLogo("C:\\foto\\logo.png", OverlayPosition.TR));
    }

    [Fact]
    public void Bussola_resta_nella_lista_ma_non_e_disponibile()
    {
        var stato = new MaskEditorState();

        Assert.Contains(stato.Widget, w => w.Tipo == OverlayWidgetKind.Bussola);
        Assert.False(MaskEditorCatalog.Disponibile(OverlayWidgetKind.Bussola));
        Assert.True(MaskEditorCatalog.Disponibile(OverlayWidgetKind.DataOra));
    }

    [Fact]
    public void Posizioni_del_catalogo_coprono_le_sei_voci_della_tendina()
    {
        Assert.Equal(
            new[] { OverlayPosition.TL, OverlayPosition.TR, OverlayPosition.BL, OverlayPosition.BR, OverlayPosition.BandaAlta, OverlayPosition.BandaBassa },
            MaskEditorCatalog.Posizioni);
    }

    [Fact]
    public void Percentuali_con_virgola_e_segno_percento_si_convertono_in_frazione()
    {
        var it = CultureInfo.GetCultureInfo("it-IT");

        bool ok = MaskInput.TryParsePercento("12,5 %", it, 0, 100, out double frazione);

        Assert.True(ok);
        Assert.Equal(0.125, frazione, 6);
        Assert.Equal("12,5", MaskInput.FormatPercento(frazione, it));
    }

    [Fact]
    public void Percentuali_fuori_intervallo_o_non_numeriche_sono_rifiutate()
    {
        var it = CultureInfo.GetCultureInfo("it-IT");

        Assert.False(MaskInput.TryParsePercento("abc", it, 0, 100, out _));
        Assert.False(MaskInput.TryParsePercento("", it, 0, 100, out _));
        Assert.False(MaskInput.TryParsePercento("101", it, 0, 100, out _));
        Assert.False(MaskInput.TryParsePercento("-1", it, 0, 100, out _));
        Assert.False(MaskInput.TryParsePercento("NaN", it, 0, 100, out _));
    }

    /// <summary>
    /// Confronto semantico: stessi widget con stessi campi, posizione, rango e stato, anche se la lista è in un altro ordine.
    /// Maschera e stile si confrontano come JSON.
    /// </summary>
    private static string Firma(OverlayLayout layout)
    {
        string corpo = string.Join(
            "\n",
            layout.Widget
                .OrderBy(w => w.Tipo)
                .ThenBy(w => w.Posizione)
                .Select(w => JsonSerializer.Serialize(w, OverlayLayoutCodec.Opzioni)));
        return OverlayLayoutCodec.Serializza(layout with { Widget = Array.Empty<OverlayWidget>() }) + corpo;
    }

    private static int IndiceDi(MaskEditorState stato, OverlayWidgetKind tipo)
    {
        for (int i = 0; i < stato.Widget.Count; i++)
        {
            if (stato.Widget[i].Tipo == tipo)
            {
                return i;
            }
        }

        throw new InvalidOperationException("tipo non presente: " + tipo);
    }

    private static OverlayPosition PosizioneDi(MaskEditorState stato, OverlayWidgetKind tipo) =>
        stato.Widget[IndiceDi(stato, tipo)].Posizione;

    private static bool AbilitatoDi(MaskEditorState stato, OverlayWidgetKind tipo) =>
        stato.Widget[IndiceDi(stato, tipo)].Abilitato;

    private static OverlayWidgetKind[] Tipi(MaskEditorState stato) =>
        stato.Widget.Select(w => w.Tipo).ToArray();

    private static OverlayWidgetKind[] Gruppo(MaskEditorState stato, OverlayPosition posizione) =>
        stato.Widget.Where(w => w.Posizione == posizione).Select(w => w.Tipo).ToArray();
}
