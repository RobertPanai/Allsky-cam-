namespace AllSky.InfoOverlay;

/// <summary>Controlla un layout senza modificarlo. Restituisce l'elenco dei problemi (vuoto se il layout è valido).</summary>
public static class OverlayLayoutValidator
{
    public static IReadOnlyList<string> Valida(OverlayLayout layout)
    {
        var errori = new List<string>();

        if (layout.Versione != OverlayLayoutVersions.Corrente)
        {
            errori.Add($"versione {layout.Versione} non supportata (attesa {OverlayLayoutVersions.Corrente})");
        }

        ValidaMaschera(layout.Maschera, errori);
        ValidaStile(layout.Stile, errori);

        if (layout.Widget is null)
        {
            errori.Add("elenco dei widget assente");
            return errori;
        }

        var conteggi = new Dictionary<OverlayWidgetKind, int>();
        for (int i = 0; i < layout.Widget.Count; i++)
        {
            var widget = layout.Widget[i];
            ValidaWidget(widget, i, errori);
            conteggi[widget.Tipo] = conteggi.GetValueOrDefault(widget.Tipo) + 1;
        }

        foreach (var (tipo, n) in conteggi)
        {
            if (n > 1 && !OverlayWidgetKinds.AmmetteIstanzeMultiple(tipo))
            {
                errori.Add($"il tipo {tipo} compare {n} volte; ammesso una sola volta");
            }
        }

        return errori;
    }

    private static void ValidaMaschera(OverlayMask maschera, List<string> errori)
    {
        if (!Enum.IsDefined(maschera.Modo))
        {
            errori.Add($"modo maschera non valido: {maschera.Modo}");
        }

        var c = maschera.Cerchio;
        if (!Entro(c.CentroX, OverlayLimits.CentroMin, OverlayLimits.CentroMax)) errori.Add("centroX fuori intervallo [0, 1]");
        if (!Entro(c.CentroY, OverlayLimits.CentroMin, OverlayLimits.CentroMax)) errori.Add("centroY fuori intervallo [0, 1]");
        if (!Entro(c.Raggio, OverlayLimits.RaggioMin, OverlayLimits.RaggioMax)) errori.Add("raggio fuori intervallo [0.01, 1]");
        if (!Enum.IsDefined(c.Esterno)) errori.Add($"bordo esterno non valido: {c.Esterno}");
        if (!Entro(c.Sfumatura, 0, OverlayLimits.SfumaturaMax)) errori.Add("sfumatura fuori intervallo [0, 0.2]");

        var b = maschera.Bande;
        if (!Entro(b.Superiore, 0, OverlayLimits.BandaMax)) errori.Add("banda superiore fuori intervallo [0, 0.5]");
        if (!Entro(b.Inferiore, 0, OverlayLimits.BandaMax)) errori.Add("banda inferiore fuori intervallo [0, 0.5]");
        if (Entro(b.Superiore, 0, OverlayLimits.BandaMax) && Entro(b.Inferiore, 0, OverlayLimits.BandaMax)
            && b.Superiore + b.Inferiore >= 1)
        {
            errori.Add("le bande nere sommate coprono tutto il frame");
        }
    }

    private static void ValidaStile(OverlayStyle stile, List<string> errori)
    {
        if (!Entro(stile.ScalaTesto, OverlayLimits.ScalaTestoMin, OverlayLimits.ScalaTestoMax))
        {
            errori.Add("scala testo fuori intervallo [0.005, 0.1]");
        }

        if (!Entro(stile.Margine, 0, OverlayLimits.MargineMax))
        {
            errori.Add("margine fuori intervallo [0, 0.1]");
        }
    }

    private static void ValidaWidget(OverlayWidget widget, int indice, List<string> errori)
    {
        string etichetta = $"widget {indice} ({widget.Tipo})";

        if (!Enum.IsDefined(widget.Tipo)) errori.Add($"widget {indice}: tipo non valido");
        if (!Enum.IsDefined(widget.Posizione)) errori.Add($"{etichetta}: posizione non valida");
        if (widget.Ordine < 0 || widget.Ordine > OverlayLimits.OrdineMax) errori.Add($"{etichetta}: ordine fuori intervallo [0, 99]");
        if (widget.Formato is null)
        {
            errori.Add($"{etichetta}: formato assente");
            return;
        }

        var formato = widget.Formato;
        var previsti = OverlayWidgetKinds.CampiPrevisti(widget.Tipo);
        foreach (OverlayFormatField campo in Enum.GetValues<OverlayFormatField>())
        {
            if (campo == OverlayFormatField.Nessuno)
            {
                continue;
            }

            bool presente = OverlayWidgetFormats.Presente(formato, campo);
            bool atteso = previsti.HasFlag(campo);
            if (atteso && !presente) errori.Add($"{etichetta}: manca il campo {campo}");
            if (!atteso && presente) errori.Add($"{etichetta}: il campo {campo} non è previsto per questo tipo");
        }

        if (formato.Pattern is not null && !OverlayWidgetFormats.PatternValido(formato.Pattern))
            errori.Add($"{etichetta}: pattern data/ora non valido");
        if (formato.Fuso is not null && !Enum.IsDefined(formato.Fuso.Value))
            errori.Add($"{etichetta}: fuso non valido");
        if (formato.Decimali is { } d && (d < 0 || d > OverlayLimits.DecimaliMax))
            errori.Add($"{etichetta}: decimali fuori intervallo [0, 2]");
        if (formato.MaxElementi is { } m && (m < OverlayLimits.MaxElementiMin || m > OverlayLimits.MaxElementiMax))
            errori.Add($"{etichetta}: numero di satelliti fuori intervallo [1, 10]");
        if (formato.Campi is { } campi && !CampiValidi(campi))
            errori.Add($"{etichetta}: elenco campi meteo vuoto, ripetuto o non valido");
        if (formato.FileLogo is not null && !OverlayWidgetFormats.FileLogoValido(formato.FileLogo))
            errori.Add($"{etichetta}: il logo deve essere il nome di un file .png nella cartella loghi");
        if (formato.AltezzaMaxPercento is { } a && !Entro(a, OverlayLimits.AltezzaLogoMin, OverlayLimits.AltezzaLogoMax))
            errori.Add($"{etichetta}: altezza massima del logo fuori intervallo [1, 50]");
        if (formato.Opacita is { } o && !Entro(o, OverlayLimits.OpacitaMin, OverlayLimits.OpacitaMax))
            errori.Add($"{etichetta}: opacità fuori intervallo [0, 1]");
        if (formato.Testo is not null && !OverlayWidgetFormats.TestoValido(formato.Testo))
            errori.Add($"{etichetta}: testo libero vuoto o troppo lungo");
    }

    internal static bool CampiValidi(IReadOnlyList<OverlayMeteoField> campi) =>
        campi.Count > 0
        && campi.All(c => Enum.IsDefined(c))
        && campi.Distinct().Count() == campi.Count;

    internal static bool Entro(double valore, double minimo, double massimo) =>
        double.IsFinite(valore) && valore >= minimo && valore <= massimo;
}
