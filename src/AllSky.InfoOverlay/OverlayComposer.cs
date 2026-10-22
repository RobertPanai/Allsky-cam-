using System.Globalization;
using AllSky.Archive;
using AllSky.Core;
using AllSky.Weather;

namespace AllSky.InfoOverlay;

/// <summary>
/// Compone la maschera informativa di un frame: dal layout versionato e dal sidecar ricava la lista dei comandi
/// di disegno (<see cref="OverlayScene"/>). Funzione pura: nessun accesso a file, rete, orologio o toolkit grafico.
/// Il renderer WPF esegue la scena.
/// </summary>
/// <remarks>
/// Ordine dei comandi: prima la maschera (cerchio o bande), poi i widget abilitati nelle posizioni scelte da
/// <see cref="OverlayPlacer"/>. Il posizionamento usa le righe effettivamente scritte (lunghezza massima in caratteri
/// per 0,6 corpi), non la stima generica: così un widget non esce dall'area che gli è stata riservata.
/// Dati mancanti: "n/d" in italiano, "n/a" in inglese (chiave <see cref="DesktopTexts.OvlMissing"/>).
/// Il widget <c>Bussola</c> non è ancora disponibile (dipende da P3): viene omesso e segnalato, senza riservare spazio.
/// </remarks>
public static class OverlayComposer
{
    /// <summary>Larghezza media di un carattere, in corpi del font.</summary>
    private const double LarghezzaCarattere = 0.6;

    /// <summary>Passo di riga, in corpi del font. Lo stesso di <see cref="OverlayMisure"/>.</summary>
    private const double PassoRiga = 1.3;

    private const int MaxCaratteriNome = 26;
    private const double OpacitaLogoPredefinita = 0.9;
    private const string MotivoBussola = "non ancora disponibile (dipende da P3)";

    private static readonly OverlayColore ColoreTesto = new(232, 238, 255);
    private static readonly OverlayColore Nero = new(0, 0, 0);
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    /// <summary>Compone la scena di un frame.</summary>
    /// <param name="layout">Layout versionato. Deve passare <see cref="OverlayLayoutValidator"/>.</param>
    /// <param name="sidecar">Metadati del frame.</param>
    /// <param name="frame">Dimensione del frame in pixel, entrambe positive.</param>
    /// <param name="cultura">Lingua delle etichette e dei dati mancanti.</param>
    public static OverlayScene Compose(OverlayLayout layout, FrameSidecar sidecar, OverlayFrameSize frame, Language cultura)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(sidecar);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frame.Larghezza, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frame.Altezza, 0);

        var errori = OverlayLayoutValidator.Valida(layout);
        if (errori.Count > 0)
        {
            throw new ArgumentException("Layout non valido: " + string.Join("; ", errori), nameof(layout));
        }

        DesktopTexts testi = cultura == Language.It ? DesktopTexts.Italian : DesktopTexts.English;
        int larghezza = frame.Larghezza;
        int altezza = frame.Altezza;
        double corpo = layout.Stile.ScalaTesto * altezza;

        var comandi = new List<OverlayComando>();
        AggiungiMaschera(layout.Maschera, larghezza, altezza, comandi);

        // Righe scritte in anticipo: servono sia a disegnare sia a misurare per il posizionamento.
        var righe = new Dictionary<OverlayWidget, IReadOnlyList<string>>(ReferenceEqualityComparer.Instance);
        foreach (OverlayWidget widget in layout.Widget.Where(w => w.Abilitato))
        {
            if (widget.Tipo is OverlayWidgetKind.Logo or OverlayWidgetKind.Bussola)
            {
                continue;
            }

            righe[widget] = Righe(widget, sidecar, cultura, testi);
        }

        var senzaBussola = layout with
        {
            Widget = layout.Widget
                .Select(w => w.Tipo == OverlayWidgetKind.Bussola ? w with { Abilitato = false } : w)
                .ToList(),
        };

        OverlayMisura misura = (widget, corpoPx, larghezzaFrame, altezzaFrame) =>
            righe.TryGetValue(widget, out IReadOnlyList<string>? linee) && linee.Count > 0
                ? new OverlaySize(linee.Max(l => l.Length) * corpoPx * LarghezzaCarattere, linee.Count * corpoPx * PassoRiga)
                : OverlayMisure.Approssimata(widget, corpoPx, larghezzaFrame, altezzaFrame);

        OverlayPosizionamento posizionamento = OverlayPlacer.Dispone(senzaBussola, larghezza, altezza, misura);

        var omessi = new List<OverlayWidgetOmesso>(posizionamento.Omessi);
        omessi.AddRange(layout.Widget
            .Where(w => w.Abilitato && w.Tipo == OverlayWidgetKind.Bussola)
            .Select(w => new OverlayWidgetOmesso(w, MotivoBussola)));

        foreach (OverlayWidgetPosizionato pw in posizionamento.Posizionati)
        {
            if (pw.Widget.Tipo == OverlayWidgetKind.Logo)
            {
                AggiungiLogo(pw, comandi);
            }
            else if (righe.TryGetValue(pw.Widget, out IReadOnlyList<string>? linee))
            {
                AggiungiTesto(pw, linee, corpo, comandi);
            }
        }

        return new OverlayScene(frame, comandi, omessi, posizionamento.AltezzaTestoPx);
    }

    /// <summary>Righe di testo di un widget. Vuote per i tipi che non sono testo (Logo, Bussola).</summary>
    private static IReadOnlyList<string> Righe(OverlayWidget widget, FrameSidecar sidecar, Language cultura, DesktopTexts testi)
    {
        OverlayWidgetFormat formato = FormatoDi(widget);
        return widget.Tipo switch
        {
            OverlayWidgetKind.DataOra => new[] { TestoDataOra(sidecar, formato, testi) },
            OverlayWidgetKind.Sole => new[] { $"{testi.OvlSun}: {Gradi(sidecar.SunAltitudeDeg, formato, cultura, testi)}" },
            OverlayWidgetKind.Luna => new[] { TestoLuna(sidecar, formato, cultura, testi) },
            OverlayWidgetKind.Satelliti => RigheSatelliti(sidecar, formato, testi),
            OverlayWidgetKind.Meteo => OverlayMeteoText.Righe(sidecar.Weather, formato.Campi, cultura),
            OverlayWidgetKind.TestoLibero => new[]
            {
                OverlayWidgetFormats.TestoValido(formato.Testo) ? formato.Testo! : testi.OvlMissing,
            },
            OverlayWidgetKind.Esposizione => new[] { TestoEsposizione(sidecar, formato, cultura, testi) },
            _ => Array.Empty<string>(),
        };
    }

    private static string TestoDataOra(FrameSidecar sidecar, OverlayWidgetFormat formato, DesktopTexts testi)
    {
        DateTimeOffset istante = formato.Fuso == OverlayTimeZone.Utc ? sidecar.TimestampUtc : sidecar.TimestampLocal;
        if (istante == default)
        {
            // Sidecar senza ora locale: si ricade sull'UTC, così la data resta leggibile.
            istante = sidecar.TimestampUtc;
        }

        if (istante == default)
        {
            return testi.OvlMissing;
        }

        string pattern = OverlayWidgetFormats.PatternValido(formato.Pattern)
            ? formato.Pattern!
            : OverlayWidgetFormats.Predefinito(OverlayWidgetKind.DataOra).Pattern!;
        return istante.ToString(pattern, Invariante);
    }

    private static string TestoLuna(FrameSidecar sidecar, OverlayWidgetFormat formato, Language cultura, DesktopTexts testi)
    {
        string testo = $"{testi.OvlMoon}: {Gradi(sidecar.MoonAltitudeDeg, formato, cultura, testi)}";
        if (formato.MostraFase == true)
        {
            string fase = Finito(sidecar.MoonIlluminationPercent) is { } p
                ? Formato(p, "F0", cultura) + "%"
                : testi.OvlMissing;
            testo += $" · {testi.OvlPhase} {fase}";
        }

        return testo;
    }

    /// <summary>Intestazione e nomi dei satelliti. Con più nomi del massimo, ne restano i primi.</summary>
    private static IReadOnlyList<string> RigheSatelliti(FrameSidecar sidecar, OverlayWidgetFormat formato, DesktopTexts testi)
    {
        string intestazione = testi.OvlSatellites + ":";
        IReadOnlyList<string>? nomi = NomiSatelliti(sidecar.Satellites);
        if (nomi is null)
        {
            return new[] { intestazione + " " + testi.OvlMissing };
        }

        if (nomi.Count == 0)
        {
            return new[] { intestazione + " " + testi.OvlNone };
        }

        int massimo = Math.Clamp(formato.MaxElementi ?? 3, OverlayLimits.MaxElementiMin, OverlayLimits.MaxElementiMax);
        var righe = new List<string>(1 + massimo) { intestazione };
        righe.AddRange(nomi.Take(massimo).Select(n => Tronca(n, MaxCaratteriNome)));
        return righe;
    }

    /// <summary>
    /// Nomi dei satelliti del sidecar, senza nomi vuoti. Null se il campo manca (previsione spenta o sidecar senza dati);
    /// lista vuota se nessun satellite è sopra l'orizzonte.
    /// </summary>
    internal static IReadOnlyList<string>? NomiSatelliti(IReadOnlyList<SatelliteInView>? satelliti)
    {
        if (satelliti is null)
        {
            return null;
        }

        return satelliti
            .Select(s => s.Name.Trim())
            .Where(n => n.Length > 0)
            .ToList();
    }

    private static string TestoEsposizione(FrameSidecar sidecar, OverlayWidgetFormat formato, Language cultura, DesktopTexts testi)
    {
        string esposizione = Finito(sidecar.ExposureMs) is { } ms && ms > 0
            ? ms >= 1000
                ? Formato(ms / 1000.0, "F1", cultura) + " s"
                : Formato(ms, "0.#", cultura) + " ms"
            : testi.OvlMissing;
        string testo = testi.OvlExposure + " " + esposizione;

        if (formato.MostraGain == true)
        {
            string gain = Finito(sidecar.Gain) is { } g ? Formato(g, "0.##", cultura) : testi.OvlMissing;
            testo += $" · {testi.OvlGain} {gain}";
        }

        return testo;
    }

    private static string Gradi(double? gradi, OverlayWidgetFormat formato, Language cultura, DesktopTexts testi)
    {
        if (Finito(gradi) is not { } v)
        {
            return testi.OvlMissing;
        }

        int decimali = Math.Clamp(formato.Decimali ?? 0, 0, OverlayLimits.DecimaliMax);
        return Formato(v, "F" + decimali.ToString(Invariante), cultura) + "°";
    }

    /// <summary>Numero con la virgola in italiano; lo zero negativo diventa zero.</summary>
    private static string Formato(double valore, string formato, Language cultura)
    {
        double v = valore;
        if (v == 0)
        {
            v = 0;
        }

        string testo = v.ToString(formato, Invariante);
        return cultura == Language.It ? testo.Replace('.', ',') : testo;
    }

    private static double? Finito(double? valore) => valore is { } v && double.IsFinite(v) ? v : null;

    private static string Tronca(string testo, int massimo) =>
        testo.Length <= massimo ? testo : testo[..(massimo - 1)] + "…";

    private static OverlayWidgetFormat FormatoDi(OverlayWidget widget) =>
        widget.Formato ?? OverlayWidgetFormats.Predefinito(widget.Tipo);

    private static OverlayAllineamento AllineamentoDi(OverlayPosition posizione) => posizione switch
    {
        OverlayPosition.TL or OverlayPosition.BL => OverlayAllineamento.Sinistra,
        OverlayPosition.TR or OverlayPosition.BR => OverlayAllineamento.Destra,
        _ => OverlayAllineamento.Centro,
    };

    private static void AggiungiMaschera(OverlayMask maschera, int larghezza, int altezza, List<OverlayComando> comandi)
    {
        switch (maschera.Modo)
        {
            case OverlayMaskMode.Cerchio:
                OverlayCircleMask c = maschera.Cerchio;
                comandi.Add(new OverlayMascheraCerchio(
                    c.CentroX * larghezza,
                    c.CentroY * altezza,
                    c.Raggio * altezza,
                    c.Esterno,
                    c.Sfumatura * altezza));
                break;

            case OverlayMaskMode.Bande:
                double alta = maschera.Bande.Superiore * altezza;
                double bassa = maschera.Bande.Inferiore * altezza;
                if (alta > 0)
                {
                    comandi.Add(new OverlayRiempimento(new OverlayRect(0, 0, larghezza, alta), Nero));
                }

                if (bassa > 0)
                {
                    comandi.Add(new OverlayRiempimento(new OverlayRect(0, altezza - bassa, larghezza, bassa), Nero));
                }

                break;
        }
    }

    private static void AggiungiTesto(OverlayWidgetPosizionato pw, IReadOnlyList<string> linee, double corpo, List<OverlayComando> comandi)
    {
        double passo = corpo * PassoRiga;
        OverlayAllineamento allineamento = AllineamentoDi(pw.Widget.Posizione);
        for (int i = 0; i < linee.Count; i++)
        {
            var area = new OverlayRect(pw.Area.X, pw.Area.Y + i * passo, pw.Area.Larghezza, passo);
            comandi.Add(new OverlayTestoPosizionato(area, linee[i], corpo, allineamento, ColoreTesto));
        }
    }

    private static void AggiungiLogo(OverlayWidgetPosizionato pw, List<OverlayComando> comandi)
    {
        OverlayWidgetFormat formato = FormatoDi(pw.Widget);
        string nome = formato.FileLogo ?? string.Empty;
        double opacita = Math.Clamp(formato.Opacita ?? OpacitaLogoPredefinita, OverlayLimits.OpacitaMin, OverlayLimits.OpacitaMax);
        comandi.Add(new OverlayImmagine(pw.Area, nome, opacita));
    }
}
