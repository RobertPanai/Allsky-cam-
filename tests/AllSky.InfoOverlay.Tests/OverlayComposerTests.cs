using System.Text.Json;
using AllSky.Archive;
using AllSky.Core;
using AllSky.InfoOverlay;
using AllSky.Weather;
using Xunit;

namespace AllSky.InfoOverlay.Tests;

public sealed class OverlayComposerTests
{
    private const double Tolleranza = 1e-6;

    private static readonly OverlayFrameSize Full = new(1920, 1080);

    private static readonly WeatherSnapshot Completo = new(
        new DateTimeOffset(2026, 10, 21, 22, 0, 0, TimeSpan.Zero),
        TemperatureC: 11.46,
        HumidityPercent: 80.4,
        PressureHpa: 1012.6,
        DewPointC: 4.0,
        CloudCoverPercent: 12.2,
        WindSpeedMs: 3.24,
        WindGustMs: 5.0,
        WindDirectionDeg: 270.2,
        RainRateMmH: 0.0,
        SkyTemperatureC: -20.0,
        SkyQualityMagArcsec2: 20.8,
        Source: "ASCOM:ObservingConditions.Test");

    /// <summary>Sidecar di una notte: Sole sotto l'orizzonte, Luna in salita, dati di esposizione presenti.</summary>
    private static FrameSidecar Sidecar(WeatherSnapshot? meteo = null, JsonElement? satelliti = null) => new()
    {
        TimestampUtc = new DateTimeOffset(2026, 10, 21, 22, 0, 0, TimeSpan.Zero),
        TimestampLocal = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.FromHours(2)),
        TimeZoneId = "Europe/Rome",
        PhaseOfDay = "night",
        ExposureMs = 8000,
        Gain = 250,
        SensorTempC = 12.3,
        SunAltitudeDeg = -34.4,
        SunAzimuthDeg = 200,
        MoonAltitudeDeg = 12.6,
        MoonAzimuthDeg = 120,
        MoonIlluminationPercent = 47.6,
        Weather = meteo,
        Satellites = satelliti,
    };

    private static FrameSidecar SidecarSenzaDati() => new()
    {
        TimestampUtc = new DateTimeOffset(2026, 10, 21, 22, 0, 0, TimeSpan.Zero),
        TimestampLocal = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.FromHours(2)),
    };

    private static JsonElement Json(string testo)
    {
        using JsonDocument documento = JsonDocument.Parse(testo);
        return documento.RootElement.Clone();
    }

    private static OverlayLayout Abilita(OverlayLayout layout, OverlayWidgetKind tipo, OverlayWidgetFormat? formato = null) =>
        layout with
        {
            Widget = layout.Widget
                .Select(w => w.Tipo == tipo ? w with { Abilitato = true, Formato = formato ?? w.Formato } : w)
                .ToList(),
        };

    private static OverlayLayout Disabilita(OverlayLayout layout, OverlayWidgetKind tipo) => layout with
    {
        Widget = layout.Widget.Select(w => w.Tipo == tipo ? w with { Abilitato = false } : w).ToList(),
    };

    private static OverlayLayout ConPosizione(OverlayLayout layout, OverlayWidgetKind tipo, OverlayPosition posizione) => layout with
    {
        Widget = layout.Widget.Select(w => w.Tipo == tipo ? w with { Posizione = posizione } : w).ToList(),
    };

    private static OverlayLayout ConMaschera(OverlayLayout layout, OverlayMaskMode modo) => layout with
    {
        Maschera = layout.Maschera with { Modo = modo },
    };

    private static IEnumerable<string> Testi(OverlayScene scena) =>
        scena.Comandi.OfType<OverlayTestoPosizionato>().Select(t => t.Testo);

    private static IEnumerable<OverlayRect> Aree(OverlayScene scena) =>
        scena.Comandi.Select(c => c switch
        {
            OverlayTestoPosizionato t => t.Area,
            OverlayImmagine i => i.Area,
            _ => (OverlayRect?)null,
        }).OfType<OverlayRect>();

    [Fact]
    public void Layout_predefinito_mostra_data_sole_e_luna_in_italiano()
    {
        var scena = OverlayComposer.Compose(OverlayLayoutDefaults.Predefinito(), Sidecar(), Full, Language.It);

        Assert.Equal(
            new[] { "2026-10-22 00:00:00", "Sole: -34°", "Luna: 13° · fase 48%" },
            Testi(scena).ToArray());
        Assert.Empty(scena.Omessi);
    }

    [Fact]
    public void Layout_predefinito_in_inglese_usa_le_etichette_inglesi()
    {
        var scena = OverlayComposer.Compose(OverlayLayoutDefaults.Predefinito(), Sidecar(), Full, Language.En);

        Assert.Contains("Sun: -34°", Testi(scena));
        Assert.Contains("Moon: 13° · phase 48%", Testi(scena));
    }

    [Fact]
    public void Sole_e_Luna_sono_in_basso_a_destra_e_il_Sole_sta_sotto_la_Luna()
    {
        var scena = OverlayComposer.Compose(OverlayLayoutDefaults.Predefinito(), Sidecar(), Full, Language.It);
        var sole = scena.Comandi.OfType<OverlayTestoPosizionato>().Single(t => t.Testo.StartsWith("Sole"));
        var luna = scena.Comandi.OfType<OverlayTestoPosizionato>().Single(t => t.Testo.StartsWith("Luna"));

        Assert.True(sole.Area.Destra <= Full.Larghezza + Tolleranza);
        Assert.True(luna.Area.Destra <= Full.Larghezza + Tolleranza);
        Assert.True(sole.Area.Y > luna.Area.Y, "il widget con ordine 0 deve stare sotto quello con ordine 1");
        Assert.Equal(OverlayAllineamento.Destra, sole.Allineamento);
    }

    [Fact]
    public void Widget_disabilitato_non_produce_comandi()
    {
        var layout = Disabilita(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Sole);

        var scena = OverlayComposer.Compose(layout, Sidecar(), Full, Language.It);

        Assert.DoesNotContain(Testi(scena), t => t.StartsWith("Sole"));
        Assert.Contains("Luna: 13° · fase 48%", Testi(scena));
    }

    [Fact]
    public void Maschera_cerchio_predefinita_e_centrata_con_raggio_meta_altezza()
    {
        var scena = OverlayComposer.Compose(OverlayLayoutDefaults.Predefinito(), Sidecar(), Full, Language.It);

        var cerchio = Assert.IsType<OverlayMascheraCerchio>(scena.Comandi[0]);
        Assert.Equal(960, cerchio.CentroX, 3);
        Assert.Equal(540, cerchio.CentroY, 3);
        Assert.Equal(540, cerchio.Raggio, 3);
        Assert.Equal(OverlayMaskEdge.Opaco, cerchio.Esterno);
        Assert.Equal(0.02 * 1080, cerchio.Sfumatura, 3);
    }

    [Fact]
    public void Maschera_cerchio_sfumata_passa_il_tipo_di_bordo()
    {
        var layout = OverlayLayoutDefaults.Predefinito();
        layout = layout with
        {
            Maschera = layout.Maschera with
            {
                Cerchio = layout.Maschera.Cerchio with { Esterno = OverlayMaskEdge.Sfumato },
            },
        };

        var scena = OverlayComposer.Compose(layout, Sidecar(), Full, Language.It);

        Assert.Equal(OverlayMaskEdge.Sfumato, Assert.IsType<OverlayMascheraCerchio>(scena.Comandi[0]).Esterno);
    }

    [Fact]
    public void Maschera_nessuna_non_disegna_maschere()
    {
        var layout = ConMaschera(OverlayLayoutDefaults.Predefinito(), OverlayMaskMode.Nessuna);

        var scena = OverlayComposer.Compose(layout, Sidecar(), Full, Language.It);

        Assert.DoesNotContain(scena.Comandi, c => c is OverlayMascheraCerchio or OverlayRiempimento);
    }

    [Fact]
    public void Maschera_a_bande_disegna_due_fasce_nere_e_il_widget_sta_nella_banda()
    {
        var layout = ConPosizione(
            ConMaschera(OverlayLayoutDefaults.Predefinito(), OverlayMaskMode.Bande),
            OverlayWidgetKind.DataOra,
            OverlayPosition.BandaBassa);

        var scena = OverlayComposer.Compose(layout, Sidecar(), Full, Language.It);

        var fasce = scena.Comandi.OfType<OverlayRiempimento>().ToList();
        Assert.Equal(2, fasce.Count);
        Assert.Equal(0, fasce[0].Area.Y, 3);
        Assert.Equal(108, fasce[0].Area.Altezza, 3);
        Assert.Equal(972, fasce[1].Area.Y, 3);
        Assert.Equal(108, fasce[1].Area.Altezza, 3);
        Assert.All(fasce, f => Assert.Equal(1920, f.Area.Larghezza, 3));

        var data = scena.Comandi.OfType<OverlayTestoPosizionato>().Single(t => t.Testo.StartsWith("2026"));
        Assert.True(data.Area.Y >= 972 - Tolleranza, "la data deve stare nella fascia bassa");
        Assert.True(data.Area.Basso <= 1080 + Tolleranza);
        Assert.Equal(OverlayAllineamento.Centro, data.Allineamento);
    }

    [Fact]
    public void Widget_di_banda_senza_maschera_a_bande_viene_omesso_con_motivo()
    {
        var layout = ConPosizione(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.DataOra, OverlayPosition.BandaBassa);

        var scena = OverlayComposer.Compose(layout, Sidecar(), Full, Language.It);

        var omesso = Assert.Single(scena.Omessi);
        Assert.Equal(OverlayWidgetKind.DataOra, omesso.Widget.Tipo);
        Assert.Equal("richiede una maschera a bande", omesso.Motivo);
        Assert.DoesNotContain(Testi(scena), t => t.StartsWith("2026"));
    }

    [Fact]
    public void Satelliti_elencano_i_nomi_fino_al_massimo()
    {
        var layout = Abilita(
            OverlayLayoutDefaults.Predefinito(),
            OverlayWidgetKind.Satelliti,
            new OverlayWidgetFormat(MaxElementi: 2));
        var satelliti = Json("[\"ISS (ZARYA)\", \"STARLINK-1234\", \"NOAA 19\"]");

        var scena = OverlayComposer.Compose(layout, Sidecar(satelliti: satelliti), Full, Language.It);

        var righe = Testi(scena).ToList();
        Assert.Contains("Satelliti:", righe);
        Assert.Contains("ISS (ZARYA)", righe);
        Assert.Contains("STARLINK-1234", righe);
        Assert.DoesNotContain("NOAA 19", righe);
    }

    [Fact]
    public void Satelliti_accettano_oggetti_con_proprieta_nome()
    {
        var layout = Abilita(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Satelliti);
        var satelliti = Json("[{\"name\": \"ISS (ZARYA)\", \"altDeg\": 41}]");

        var scena = OverlayComposer.Compose(layout, Sidecar(satelliti: satelliti), Full, Language.En);

        Assert.Contains("ISS (ZARYA)", Testi(scena));
    }

    [Fact]
    public void Satelliti_senza_dato_mostrano_n_d_e_senza_transiti_nessuno()
    {
        var layout = Abilita(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Satelliti);

        var mancanti = OverlayComposer.Compose(layout, Sidecar(satelliti: null), Full, Language.It);
        var vuoto = OverlayComposer.Compose(layout, Sidecar(satelliti: Json("[]")), Full, Language.It);
        var formatoSconosciuto = OverlayComposer.Compose(layout, Sidecar(satelliti: Json("\"ISS\"")), Full, Language.It);
        var inglese = OverlayComposer.Compose(layout, Sidecar(satelliti: null), Full, Language.En);

        Assert.Contains("Satelliti: n/d", Testi(mancanti));
        Assert.Contains("Satelliti: nessuno", Testi(vuoto));
        Assert.Contains("Satelliti: n/d", Testi(formatoSconosciuto));
        Assert.Contains("Satellites: n/a", Testi(inglese));
    }

    [Fact]
    public void Satelliti_con_nome_troppo_lungo_vengono_troncati()
    {
        var layout = Abilita(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Satelliti);
        string lungo = new string('A', 40);

        var scena = OverlayComposer.Compose(layout, Sidecar(satelliti: Json($"[\"{lungo}\"]")), Full, Language.It);

        string riga = Testi(scena).Single(t => t.StartsWith("AAA"));
        Assert.Equal(26, riga.Length);
        Assert.EndsWith("…", riga);
    }

    [Fact]
    public void Logo_produce_immagine_con_altezza_e_opacita_del_formato()
    {
        var layout = Abilita(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Logo);

        var scena = OverlayComposer.Compose(layout, Sidecar(), Full, Language.It);

        var immagine = scena.Comandi.OfType<OverlayImmagine>().Single();
        Assert.Equal("logo.png", immagine.NomeFile);
        Assert.Equal(0.9, immagine.Opacita, 6);
        Assert.Equal(0.08 * 1080, immagine.Area.Altezza, 3);
        Assert.Equal(0.08 * 1080, immagine.Area.Larghezza, 3);
        Assert.True(immagine.Area.Destra <= 1920 + Tolleranza);
    }

    [Fact]
    public void Meteo_mostra_le_righe_scelte_nel_formato_del_widget()
    {
        var layout = Abilita(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Meteo);

        var scena = OverlayComposer.Compose(layout, Sidecar(meteo: Completo), Full, Language.It);

        Assert.Contains("Temperatura: 11,5 °C", Testi(scena));
        Assert.Contains("Umidità: 80 %", Testi(scena));
        Assert.Contains("Vento: 3,2 m/s, da 270°", Testi(scena));
    }

    [Fact]
    public void Meteo_senza_lettura_mostra_n_d_in_ogni_riga()
    {
        var layout = Abilita(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Meteo);

        var scena = OverlayComposer.Compose(layout, Sidecar(meteo: null), Full, Language.It);

        Assert.Contains("Temperatura: n/d", Testi(scena));
        Assert.Contains("Umidità: n/d", Testi(scena));
        Assert.Contains("Vento: n/d", Testi(scena));
    }

    [Fact]
    public void Dati_astronomici_mancanti_diventano_n_d()
    {
        var scena = OverlayComposer.Compose(OverlayLayoutDefaults.Predefinito(), SidecarSenzaDati(), Full, Language.It);

        Assert.Contains("Sole: n/d", Testi(scena));
        Assert.Contains("Luna: n/d · fase n/d", Testi(scena));
    }

    [Fact]
    public void Dati_mancanti_in_inglese_diventano_n_a()
    {
        var scena = OverlayComposer.Compose(OverlayLayoutDefaults.Predefinito(), SidecarSenzaDati(), Full, Language.En);

        Assert.Contains("Sun: n/a", Testi(scena));
        Assert.Contains("Moon: n/a · phase n/a", Testi(scena));
    }

    [Fact]
    public void Testo_libero_ed_esposizione_mostrano_formato_e_sidecar()
    {
        var layout = Abilita(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.TestoLibero);
        layout = Abilita(layout, OverlayWidgetKind.Esposizione);

        var scena = OverlayComposer.Compose(layout, Sidecar(), Full, Language.It);

        Assert.Contains("Osservatorio", Testi(scena));
        Assert.Contains("esp 8,0 s · gain 250", Testi(scena));
    }

    [Fact]
    public void Esposizione_sotto_il_secondo_usa_i_millisecondi_in_inglese()
    {
        var layout = Abilita(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Esposizione);
        var sidecar = Sidecar() with { ExposureMs = 250 };

        var scena = OverlayComposer.Compose(layout, sidecar, Full, Language.En);

        Assert.Contains("exp 250 ms · gain 250", Testi(scena));
    }

    [Fact]
    public void Bussola_abilitata_viene_omessa_perche_non_ancora_disponibile()
    {
        var layout = Abilita(OverlayLayoutDefaults.Predefinito(), OverlayWidgetKind.Bussola);

        var scena = OverlayComposer.Compose(layout, Sidecar(), Full, Language.It);

        var omesso = Assert.Single(scena.Omessi, o => o.Widget.Tipo == OverlayWidgetKind.Bussola);
        Assert.Contains("P3", omesso.Motivo);
        Assert.DoesNotContain(scena.Comandi, c => c is OverlayImmagine);
    }

    [Theory]
    [InlineData(1600, 1200)]
    [InlineData(1920, 1080)]
    [InlineData(1000, 1000)]
    public void Tutti_i_widget_restano_nel_frame_senza_sovrapposizioni(int larghezza, int altezza)
    {
        var layout = OverlayLayoutDefaults.Predefinito();
        foreach (OverlayWidgetKind tipo in Enum.GetValues<OverlayWidgetKind>())
        {
            if (tipo != OverlayWidgetKind.Bussola)
            {
                layout = Abilita(layout, tipo);
            }
        }

        var scena = OverlayComposer.Compose(
            layout,
            Sidecar(meteo: Completo, satelliti: Json("[\"ISS\", \"NOAA 19\"]")),
            new OverlayFrameSize(larghezza, altezza),
            Language.It);

        var aree = Aree(scena).ToList();
        Assert.NotEmpty(aree);
        foreach (OverlayRect a in aree)
        {
            Assert.True(a.X >= -Tolleranza && a.Y >= -Tolleranza, "area fuori dal bordo alto/sinistro");
            Assert.True(a.Destra <= larghezza + Tolleranza, "area fuori dal bordo destro");
            Assert.True(a.Basso <= altezza + Tolleranza, "area fuori dal bordo basso");
        }

        for (int i = 0; i < aree.Count; i++)
        {
            for (int j = i + 1; j < aree.Count; j++)
            {
                Assert.False(aree[i].Interseca(aree[j]), $"sovrapposizione tra aree {i} e {j}");
            }
        }
    }

    [Fact]
    public void Corpo_del_font_scala_con_l_altezza_del_frame()
    {
        var scena = OverlayComposer.Compose(OverlayLayoutDefaults.Predefinito(), Sidecar(), new OverlayFrameSize(1600, 1200), Language.It);

        Assert.Equal(0.028 * 1200, scena.AltezzaTestoPx, 6);
        Assert.All(scena.Comandi.OfType<OverlayTestoPosizionato>(), t => Assert.Equal(0.028 * 1200, t.AltezzaTestoPx, 6));
    }

    [Fact]
    public void Composizione_e_deterministica()
    {
        var layout = OverlayLayoutDefaults.Predefinito();
        var sidecar = Sidecar(meteo: Completo);

        var prima = OverlayComposer.Compose(layout, sidecar, Full, Language.It);
        var seconda = OverlayComposer.Compose(layout, sidecar, Full, Language.It);

        Assert.Equal(Testi(prima).ToArray(), Testi(seconda).ToArray());
        Assert.Equal(prima.Comandi.Count, seconda.Comandi.Count);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    public void Frame_non_positivo_lancia_eccezione(int larghezza, int altezza)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
            OverlayComposer.Compose(OverlayLayoutDefaults.Predefinito(), Sidecar(), new OverlayFrameSize(larghezza, altezza), Language.It));
    }
}
