using AllSky.InfoOverlay;
using Xunit;

namespace AllSky.InfoOverlay.Tests;

public sealed class OverlayPlacementTests
{
    private const double Tolleranza = 1e-6;

    private static OverlayLayout ConTuttiAttivi(OverlayLayout layout) => layout with
    {
        Widget = layout.Widget.Select(w => w with { Abilitato = true }).ToList(),
    };

    private static OverlayLayout ConBande(OverlayLayout layout) => layout with
    {
        Maschera = layout.Maschera with
        {
            Modo = OverlayMaskMode.Bande,
            Bande = new OverlayBandMask(0.1, 0.1),
        },
    };

    private static void AssertDentroEdSenzaSovrapposizioni(OverlayPosizionamento p, int larghezza, int altezza)
    {
        foreach (var w in p.Posizionati)
        {
            var a = w.Area;
            Assert.True(a.X >= -Tolleranza, $"{w.Widget.Tipo} esce a sinistra");
            Assert.True(a.Y >= -Tolleranza, $"{w.Widget.Tipo} esce in alto");
            Assert.True(a.Destra <= larghezza + Tolleranza, $"{w.Widget.Tipo} esce a destra");
            Assert.True(a.Basso <= altezza + Tolleranza, $"{w.Widget.Tipo} esce in basso");
        }

        for (int i = 0; i < p.Posizionati.Count; i++)
        {
            for (int j = i + 1; j < p.Posizionati.Count; j++)
            {
                Assert.False(
                    p.Posizionati[i].Area.Interseca(p.Posizionati[j].Area),
                    $"sovrapposizione tra {p.Posizionati[i].Widget.Tipo} e {p.Posizionati[j].Widget.Tipo}");
            }
        }
    }

    [Theory]
    [InlineData(1600, 1200)]
    [InlineData(1920, 1080)]
    [InlineData(1000, 1000)]
    [InlineData(3840, 2160)]
    [InlineData(640, 480)]
    public void Layout_predefinito_entra_senza_sovrapposizioni_a_piu_risoluzioni(int larghezza, int altezza)
    {
        var layout = OverlayLayoutDefaults.Predefinito();

        var p = OverlayPlacer.Dispone(layout, larghezza, altezza);

        Assert.Empty(p.Omessi);
        Assert.Equal(3, p.Posizionati.Count);
        AssertDentroEdSenzaSovrapposizioni(p, larghezza, altezza);
    }

    [Theory]
    [InlineData(1600, 1200)]
    [InlineData(1920, 1080)]
    [InlineData(1000, 1000)]
    [InlineData(3840, 2160)]
    [InlineData(640, 480)]
    public void Tutti_i_widget_attivi_non_escono_dal_frame_e_non_si_sovrappongono(int larghezza, int altezza)
    {
        var layout = ConBande(ConTuttiAttivi(OverlayLayoutDefaults.Predefinito()));

        var p = OverlayPlacer.Dispone(layout, larghezza, altezza);

        AssertDentroEdSenzaSovrapposizioni(p, larghezza, altezza);
        Assert.Equal(layout.Widget.Count, p.Posizionati.Count + p.Omessi.Count);
    }

    [Fact]
    public void Widget_disabilitati_non_vengono_mai_posizionati()
    {
        var layout = OverlayLayoutDefaults.Predefinito();

        var p = OverlayPlacer.Dispone(layout, 1920, 1080);

        Assert.All(p.Posizionati, w => Assert.True(w.Widget.Abilitato));
        Assert.Empty(p.Omessi.Where(o => o.Widget.Abilitato == false));
        Assert.DoesNotContain(p.Posizionati, w => w.Widget.Tipo == OverlayWidgetKind.Satelliti);
    }

    [Fact]
    public void Attivare_un_widget_disabilitato_lo_aggiunge_al_posizionamento()
    {
        var layout = OverlayLayoutDefaults.Predefinito();
        var conMeteo = layout with
        {
            Widget = layout.Widget.Select(w => w.Tipo == OverlayWidgetKind.Meteo ? w with { Abilitato = true } : w).ToList(),
        };

        var p = OverlayPlacer.Dispone(conMeteo, 1920, 1080);

        Assert.Contains(p.Posizionati, w => w.Widget.Tipo == OverlayWidgetKind.Meteo);
        Assert.Equal(4, p.Posizionati.Count);
    }

    [Fact]
    public void Nello_stesso_angolo_il_widget_con_ordine_minore_sta_piu_vicino_al_bordo()
    {
        var p = OverlayPlacer.Dispone(OverlayLayoutDefaults.Predefinito(), 1920, 1080);

        var sole = p.Posizionati.Single(w => w.Widget.Tipo == OverlayWidgetKind.Sole).Area;
        var luna = p.Posizionati.Single(w => w.Widget.Tipo == OverlayWidgetKind.Luna).Area;

        Assert.True(sole.Y > luna.Y, "Sole (ordine 0) deve stare sotto Luna (ordine 1) nell'angolo BR");
        Assert.True(luna.Basso <= sole.Y + Tolleranza, "Luna non deve sovrapporsi a Sole");
        Assert.True(sole.Basso <= 1080 + Tolleranza);
    }

    [Fact]
    public void Il_layout_scala_con_l_altezza_del_frame()
    {
        var layout = OverlayLayoutDefaults.Predefinito();

        var piccolo = OverlayPlacer.Dispone(layout, 1920, 1080);
        var grande = OverlayPlacer.Dispone(layout, 3840, 2160);

        Assert.Equal(piccolo.Posizionati.Count, grande.Posizionati.Count);
        for (int i = 0; i < piccolo.Posizionati.Count; i++)
        {
            var a = piccolo.Posizionati[i].Area;
            var b = grande.Posizionati[i].Area;
            Assert.Equal(a.X * 2, b.X, Tolleranza);
            Assert.Equal(a.Y * 2, b.Y, Tolleranza);
            Assert.Equal(a.Larghezza * 2, b.Larghezza, Tolleranza);
            Assert.Equal(a.Altezza * 2, b.Altezza, Tolleranza);
        }

        Assert.Equal(piccolo.AltezzaTestoPx * 2, grande.AltezzaTestoPx, Tolleranza);
    }

    [Fact]
    public void Widget_di_banda_senza_maschera_a_bande_vengono_omessi_con_motivo()
    {
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Widget = new[] { new OverlayWidget(OverlayWidgetKind.Sole, true, OverlayPosition.BandaAlta, 0, OverlayWidgetFormats.Predefinito(OverlayWidgetKind.Sole)) },
        };

        var p = OverlayPlacer.Dispone(layout, 1920, 1080);

        Assert.Empty(p.Posizionati);
        var omesso = Assert.Single(p.Omessi);
        Assert.Contains("maschera a bande", omesso.Motivo);
    }

    [Theory]
    [InlineData(1600, 1200)]
    [InlineData(1920, 1080)]
    [InlineData(1000, 1000)]
    public void Widget_di_banda_stanno_dentro_la_fascia_nera(int larghezza, int altezza)
    {
        var layout = ConBande(OverlayLayoutDefaults.Predefinito()) with
        {
            Widget = new[]
            {
                new OverlayWidget(OverlayWidgetKind.Sole, true, OverlayPosition.BandaAlta, 0, OverlayWidgetFormats.Predefinito(OverlayWidgetKind.Sole)),
                new OverlayWidget(OverlayWidgetKind.Luna, true, OverlayPosition.BandaBassa, 0, OverlayWidgetFormats.Predefinito(OverlayWidgetKind.Luna)),
            },
        };

        var p = OverlayPlacer.Dispone(layout, larghezza, altezza);

        Assert.Equal(2, p.Posizionati.Count);
        var alta = p.Posizionati.Single(w => w.Widget.Tipo == OverlayWidgetKind.Sole).Area;
        var bassa = p.Posizionati.Single(w => w.Widget.Tipo == OverlayWidgetKind.Luna).Area;
        Assert.True(alta.Basso <= 0.1 * altezza + Tolleranza, "la banda alta resta dentro la fascia superiore");
        Assert.True(bassa.Y >= altezza - 0.1 * altezza - Tolleranza, "la banda bassa resta dentro la fascia inferiore");
        AssertDentroEdSenzaSovrapposizioni(p, larghezza, altezza);
    }

    [Fact]
    public void Con_un_frame_piccolo_la_coda_del_gruppo_viene_omessa_e_lo_si_segnala()
    {
        // Testo al massimo (10% dell'altezza) e 10 satelliti: la pila nell'angolo TL supera il frame.
        var layout = OverlayLayoutDefaults.Predefinito() with
        {
            Stile = new OverlayStyle(ScalaTesto: OverlayLimits.ScalaTestoMax, Margine: 0.02),
            Widget = new[]
            {
                new OverlayWidget(OverlayWidgetKind.Satelliti, true, OverlayPosition.TL, 0, OverlayWidgetFormats.Predefinito(OverlayWidgetKind.Satelliti) with { MaxElementi = 10 }),
                new OverlayWidget(OverlayWidgetKind.TestoLibero, true, OverlayPosition.TL, 1, OverlayWidgetFormats.Predefinito(OverlayWidgetKind.TestoLibero)),
                new OverlayWidget(OverlayWidgetKind.Meteo, true, OverlayPosition.TL, 2, OverlayWidgetFormats.Predefinito(OverlayWidgetKind.Meteo)),
            },
        };

        var p = OverlayPlacer.Dispone(layout, 300, 200);

        AssertDentroEdSenzaSovrapposizioni(p, 300, 200);
        Assert.Empty(p.Posizionati);
        Assert.Equal(3, p.Omessi.Count);
        Assert.False(p.Omessi[0].Motivo.StartsWith("in coda"), "il primo omesso dichiara la causa reale");
        Assert.True(p.Omessi[1].Motivo.StartsWith("in coda"), "dopo il primo omesso, il resto del gruppo è in coda");
        Assert.True(p.Omessi[2].Motivo.StartsWith("in coda"), "dopo il primo omesso, il resto del gruppo è in coda");
    }

    [Fact]
    public void Misura_personalizzata_viene_usata_per_le_dimensioni()
    {
        var layout = OverlayLayoutDefaults.Predefinito();

        var p = OverlayPlacer.Dispone(layout, 1920, 1080, (w, testo, l, a) => new OverlaySize(100, 20));

        Assert.All(p.Posizionati, w =>
        {
            Assert.Equal(100, w.Area.Larghezza, Tolleranza);
            Assert.Equal(20, w.Area.Altezza, Tolleranza);
        });
    }

    [Fact]
    public void Dimensioni_del_frame_non_valide_sono_rifiutate()
    {
        var layout = OverlayLayoutDefaults.Predefinito();

        Assert.Throws<ArgumentOutOfRangeException>(() => { OverlayPlacer.Dispone(layout, 0, 1080); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { OverlayPlacer.Dispone(layout, 1920, -1); });
    }

    [Fact]
    public void Rettangoli_adiacenti_non_si_intersecano()
    {
        var a = new OverlayRect(0, 0, 10, 10);

        Assert.False(a.Interseca(new OverlayRect(10, 0, 10, 10)));
        Assert.True(a.Interseca(new OverlayRect(9, 9, 10, 10)));
    }
}
