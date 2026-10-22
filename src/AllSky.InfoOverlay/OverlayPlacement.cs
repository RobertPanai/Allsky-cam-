namespace AllSky.InfoOverlay;

/// <summary>Dimensione di un widget in pixel. Il renderer può fornire la misura reale del testo.</summary>
public sealed record OverlaySize(double Larghezza, double Altezza);

/// <summary>Misura di un widget dato il font in pixel e la dimensione del frame.</summary>
public delegate OverlaySize OverlayMisura(OverlayWidget widget, double altezzaTestoPx, int larghezzaFrame, int altezzaFrame);

/// <summary>Rettangolo in pixel del frame, origine in alto a sinistra.</summary>
public sealed record OverlayRect(double X, double Y, double Larghezza, double Altezza)
{
    public double Destra => X + Larghezza;

    public double Basso => Y + Altezza;

    public bool Interseca(OverlayRect altro) =>
        X < altro.Destra && altro.X < Destra && Y < altro.Basso && altro.Y < Basso;
}

public sealed record OverlayWidgetPosizionato(OverlayWidget Widget, OverlayRect Area);

public sealed record OverlayWidgetOmesso(OverlayWidget Widget, string Motivo);

/// <summary>Esito del posizionamento: i rettangoli dei widget visibili e quelli omessi con il motivo.</summary>
public sealed record OverlayPosizionamento(
    IReadOnlyList<OverlayWidgetPosizionato> Posizionati,
    IReadOnlyList<OverlayWidgetOmesso> Omessi,
    double AltezzaTestoPx);

/// <summary>
/// Calcolo puro dei rettangoli dei widget per un frame di dimensione data. Non disegna nulla.
/// Tutte le misure derivano dall'altezza del frame, quindi il layout scala con la risoluzione.
/// Garanzie: nessun rettangolo esce dal frame, due rettangoli non si sovrappongono, e nella stessa
/// posizione un widget con ordine maggiore non viene mai piazzato prima di uno con ordine minore.
/// Se lo spazio manca, il widget che non entra e tutti quelli dopo di lui nello stesso gruppo vengono omessi.
/// </summary>
public static class OverlayPlacer
{
    private static readonly OverlayPosition[] OrdineGruppi =
    {
        OverlayPosition.TL,
        OverlayPosition.TR,
        OverlayPosition.BL,
        OverlayPosition.BR,
        OverlayPosition.BandaAlta,
        OverlayPosition.BandaBassa,
    };

    public static OverlayPosizionamento Dispone(OverlayLayout layout, int larghezza, int altezza, OverlayMisura? misura = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(larghezza, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(altezza, 0);

        var misuraEffettiva = misura ?? OverlayMisure.Approssimata;
        var contesto = new Contesto(layout, larghezza, altezza);
        var posizionati = new List<OverlayWidgetPosizionato>();
        var omessi = new List<OverlayWidgetOmesso>();

        foreach (var posizione in OrdineGruppi)
        {
            var gruppo = layout.Widget
                .Select((w, indice) => (w, indice))
                .Where(x => x.w.Abilitato && x.w.Posizione == posizione)
                .OrderBy(x => x.w.Ordine)
                .ThenBy(x => x.indice)
                .Select(x => x.w)
                .ToList();

            if (gruppo.Count == 0)
            {
                continue;
            }

            if (posizione is OverlayPosition.BandaAlta or OverlayPosition.BandaBassa)
            {
                DisponiBanda(contesto, posizione, gruppo, misuraEffettiva, posizionati, omessi);
            }
            else
            {
                DisponiAngolo(contesto, posizione, gruppo, misuraEffettiva, posizionati, omessi);
            }
        }

        return new OverlayPosizionamento(posizionati, omessi, contesto.Testo);
    }

    private static void DisponiAngolo(
        Contesto c,
        OverlayPosition posizione,
        List<OverlayWidget> gruppo,
        OverlayMisura misura,
        List<OverlayWidgetPosizionato> posizionati,
        List<OverlayWidgetOmesso> omessi)
    {
        bool inAlto = posizione is OverlayPosition.TL or OverlayPosition.TR;
        bool aSinistra = posizione is OverlayPosition.TL or OverlayPosition.BL;
        double cursore = inAlto ? c.InizioAngoli + c.Margine : c.FineAngoli - c.Margine;
        string? motivoStop = null;

        foreach (var widget in gruppo)
        {
            if (motivoStop is not null)
            {
                omessi.Add(new OverlayWidgetOmesso(widget, $"in coda: {motivoStop}"));
                continue;
            }

            var misure = misura(widget, c.Testo, c.Larghezza, c.Altezza);
            if (!IsDimensioneValida(misure))
            {
                omessi.Add(new OverlayWidgetOmesso(widget, "dimensione non valida"));
                motivoStop = "dimensione non valida";
                continue;
            }

            if (misure.Larghezza > c.Larghezza - 2 * c.Margine)
            {
                omessi.Add(new OverlayWidgetOmesso(widget, "più largo del frame"));
                motivoStop = "più largo del frame";
                continue;
            }

            double x = aSinistra ? c.Margine : c.Larghezza - c.Margine - misure.Larghezza;
            double y = inAlto ? cursore : cursore - misure.Altezza;
            var area = new OverlayRect(x, y, misure.Larghezza, misure.Altezza);

            bool dentro = inAlto
                ? y + misure.Altezza <= c.FineAngoli - c.Margine
                : y >= c.InizioAngoli + c.Margine;
            if (!dentro)
            {
                omessi.Add(new OverlayWidgetOmesso(widget, "spazio verticale insufficiente"));
                motivoStop = "spazio verticale insufficiente";
                continue;
            }

            if (posizionati.Any(p => p.Area.Interseca(area)))
            {
                omessi.Add(new OverlayWidgetOmesso(widget, "sovrapposto a un altro widget"));
                motivoStop = "sovrapposizione";
                continue;
            }

            posizionati.Add(new OverlayWidgetPosizionato(widget, area));
            cursore = inAlto ? y + misure.Altezza + c.Intervallo : y - c.Intervallo;
        }
    }

    private static void DisponiBanda(
        Contesto c,
        OverlayPosition posizione,
        List<OverlayWidget> gruppo,
        OverlayMisura misura,
        List<OverlayWidgetPosizionato> posizionati,
        List<OverlayWidgetOmesso> omessi)
    {
        if (!c.BandeAttive)
        {
            foreach (var widget in gruppo)
            {
                omessi.Add(new OverlayWidgetOmesso(widget, "richiede una maschera a bande"));
            }

            return;
        }

        double spessore = posizione == OverlayPosition.BandaAlta ? c.BandaAlta : c.BandaBassa;
        if (spessore <= 0)
        {
            foreach (var widget in gruppo)
            {
                omessi.Add(new OverlayWidgetOmesso(widget, "banda di altezza zero"));
            }

            return;
        }

        double yBanda = posizione == OverlayPosition.BandaAlta ? 0 : c.Altezza - spessore;
        var misure = gruppo.Select(w => misura(w, c.Testo, c.Larghezza, c.Altezza)).ToList();

        // Entrano in fila, da sinistra, i widget fino al primo che non sta in larghezza o in altezza.
        double disponibile = c.Larghezza - 2 * c.Margine;
        double totale = 0;
        int n = 0;
        for (int i = 0; i < gruppo.Count; i++)
        {
            var s = misure[i];
            if (!IsDimensioneValida(s) || s.Altezza > spessore)
            {
                break;
            }

            double necessario = totale + (n > 0 ? c.Intervallo : 0) + s.Larghezza;
            if (necessario > disponibile)
            {
                break;
            }

            totale = necessario;
            n++;
        }

        double x = (c.Larghezza - totale) / 2;
        for (int i = 0; i < n; i++)
        {
            var s = misure[i];
            var area = new OverlayRect(x, yBanda + (spessore - s.Altezza) / 2, s.Larghezza, s.Altezza);
            if (posizionati.Any(p => p.Area.Interseca(area)))
            {
                // La banda è disgiunta dagli angoli per costruzione; questo controllo copre gli altri casi.
                omessi.Add(new OverlayWidgetOmesso(gruppo[i], "sovrapposto a un altro widget"));
                continue;
            }

            posizionati.Add(new OverlayWidgetPosizionato(gruppo[i], area));
            x += s.Larghezza + c.Intervallo;
        }

        for (int i = n; i < gruppo.Count; i++)
        {
            string motivo = IsDimensioneValida(misure[i]) && misure[i].Altezza > spessore
                ? "banda troppo bassa"
                : "spazio orizzontale insufficiente";
            omessi.Add(new OverlayWidgetOmesso(gruppo[i], motivo));
        }
    }

    private static bool IsDimensioneValida(OverlaySize s) =>
        double.IsFinite(s.Larghezza) && double.IsFinite(s.Altezza) && s.Larghezza > 0 && s.Altezza > 0;

    /// <summary>Misure derivate dal layout, una sola volta per chiamata.</summary>
    private sealed class Contesto
    {
        public Contesto(OverlayLayout layout, int larghezza, int altezza)
        {
            Larghezza = larghezza;
            Altezza = altezza;
            Testo = layout.Stile.ScalaTesto * altezza;
            Margine = layout.Stile.Margine * altezza;
            Intervallo = Testo * 0.6;
            BandeAttive = layout.Maschera.Modo == OverlayMaskMode.Bande;
            BandaAlta = BandeAttive ? layout.Maschera.Bande.Superiore * altezza : 0;
            BandaBassa = BandeAttive ? layout.Maschera.Bande.Inferiore * altezza : 0;
            InizioAngoli = BandaAlta;
            FineAngoli = altezza - BandaBassa;
        }

        public int Larghezza { get; }

        public int Altezza { get; }

        public double Testo { get; }

        public double Margine { get; }

        public double Intervallo { get; }

        public bool BandeAttive { get; }

        public double BandaAlta { get; }

        public double BandaBassa { get; }

        /// <summary>Limite superiore dell'area degli angoli: sotto la banda alta, se la maschera ha bande.</summary>
        public double InizioAngoli { get; }

        /// <summary>Limite inferiore dell'area degli angoli: sopra la banda bassa, se la maschera ha bande.</summary>
        public double FineAngoli { get; }
    }
}

/// <summary>Misura approssimata dei widget, in multipli del font. Il renderer può sostituirla con quella reale.</summary>
public static class OverlayMisure
{
    public static OverlaySize Approssimata(OverlayWidget widget, double altezzaTestoPx, int larghezzaFrame, int altezzaFrame)
    {
        double riga = altezzaTestoPx * 1.3;
        double carattere = altezzaTestoPx * 0.6;
        var f = widget.Formato;

        switch (widget.Tipo)
        {
            case OverlayWidgetKind.DataOra:
                return new OverlaySize(carattere * Math.Max(1, f?.Pattern?.Length ?? 19), riga);
            case OverlayWidgetKind.Sole:
                return new OverlaySize(carattere * 9, riga);
            case OverlayWidgetKind.Luna:
                return new OverlaySize(carattere * (f?.MostraFase == true ? 21 : 12), riga);
            case OverlayWidgetKind.Satelliti:
                return new OverlaySize(carattere * 26, riga * (1 + (f?.MaxElementi ?? 1)));
            case OverlayWidgetKind.Meteo:
                return new OverlaySize(carattere * 22, riga * Math.Max(1, f?.Campi?.Count ?? 1));
            case OverlayWidgetKind.Logo:
                double lato = (f?.AltezzaMaxPercento ?? 8) / 100.0 * altezzaFrame;
                return new OverlaySize(lato, lato);
            case OverlayWidgetKind.TestoLibero:
                return new OverlaySize(carattere * Math.Max(1, f?.Testo?.Length ?? 1), riga);
            case OverlayWidgetKind.Esposizione:
                return new OverlaySize(carattere * (f?.MostraGain == true ? 16 : 9), riga);
            default:
                return new OverlaySize(carattere * 6, riga);
        }
    }
}
