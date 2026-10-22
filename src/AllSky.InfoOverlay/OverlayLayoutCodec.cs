using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AllSky.InfoOverlay;

/// <summary>Esito della lettura di un layout: il layout usabile, la versione trovata e gli avvisi per l'utente.</summary>
public sealed record OverlayLayoutReadResult(OverlayLayout Layout, int VersioneOriginale, bool Migrata, IReadOnlyList<string> Avvisi);

/// <summary>
/// Lettura e scrittura del layout in JSON. La lettura è tollerante: un valore sbagliato viene corretto o sostituito
/// con il predefinito e segnalato negli avvisi, così il risultato passa sempre <see cref="OverlayLayoutValidator"/>.
/// </summary>
public static class OverlayLayoutCodec
{
    /// <summary>
    /// Passi di migrazione. L'indice è la versione di partenza: <c>PassiMigrazione[v]</c> porta da v a v+1.
    /// Quando si alza <see cref="OverlayLayoutVersions.Corrente"/>, va aggiunto un passo qui.
    /// </summary>
    private static readonly Action<JsonObject>[] PassiMigrazione =
    {
        // v0 -> v1: i file salvati prima del campo "versione" hanno già la forma attuale; il passo aggiunge solo il numero.
        radice => { },
    };

    public static JsonSerializerOptions Opzioni { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serializza(OverlayLayout layout) =>
        JsonSerializer.Serialize(layout, Opzioni);

    public static OverlayLayoutReadResult Leggi(string? json)
    {
        var avvisi = new List<string>();

        if (string.IsNullOrWhiteSpace(json))
        {
            avvisi.Add("layout assente: uso i predefiniti");
            return Predefiniti(0, avvisi);
        }

        JsonObject? radice;
        try
        {
            radice = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            radice = null;
        }

        if (radice is null)
        {
            avvisi.Add("layout non leggibile (JSON non valido): uso i predefiniti");
            return Predefiniti(0, avvisi);
        }

        int versione = LeggiVersione(radice, avvisi);
        if (versione > OverlayLayoutVersions.Corrente)
        {
            avvisi.Add($"layout di versione {versione}, più recente di {OverlayLayoutVersions.Corrente}: uso i predefiniti");
            return Predefiniti(versione, avvisi);
        }

        bool migrata = versione < OverlayLayoutVersions.Corrente;
        if (migrata)
        {
            for (int v = Math.Max(versione, 0); v < OverlayLayoutVersions.Corrente; v++)
            {
                PassiMigrazione[v](radice);
            }

            radice["versione"] = OverlayLayoutVersions.Corrente;
            avvisi.Add($"layout di versione {versione} migrato alla {OverlayLayoutVersions.Corrente}");
        }

        var predefinito = OverlayLayoutDefaults.Predefinito();
        var maschera = LeggiSezione(radice, "maschera", predefinito.Maschera, avvisi);
        var stile = LeggiSezione(radice, "stile", predefinito.Stile, avvisi);
        var widget = LeggiWidget(radice, predefinito.Widget, avvisi);

        var layout = OverlayLayoutNormalizer.Normalizza(
            new OverlayLayout(OverlayLayoutVersions.Corrente, maschera, stile, widget),
            avvisi);

        return new OverlayLayoutReadResult(layout, versione, migrata, avvisi);
    }

    private static OverlayLayoutReadResult Predefiniti(int versioneOriginale, List<string> avvisi) =>
        new(OverlayLayoutDefaults.Predefinito(), versioneOriginale, false, avvisi);

    private static int LeggiVersione(JsonObject radice, List<string> avvisi)
    {
        if (!radice.TryGetPropertyValue("versione", out var nodo) || nodo is null)
        {
            return 0;
        }

        if (nodo is JsonValue valore && valore.TryGetValue(out int versione))
        {
            return Math.Max(versione, 0);
        }

        avvisi.Add("campo versione non numerico: trattato come versione 0");
        return 0;
    }

    private static T LeggiSezione<T>(JsonObject radice, string nome, T predefinito, List<string> avvisi)
        where T : class
    {
        if (!radice.TryGetPropertyValue(nome, out var nodo) || nodo is null)
        {
            avvisi.Add($"sezione '{nome}' assente: uso i predefiniti");
            return predefinito;
        }

        try
        {
            return nodo.Deserialize<T>(Opzioni) ?? predefinito;
        }
        catch (JsonException)
        {
            avvisi.Add($"sezione '{nome}' non valida: uso i predefiniti");
            return predefinito;
        }
    }

    private static IReadOnlyList<OverlayWidget> LeggiWidget(
        JsonObject radice,
        IReadOnlyList<OverlayWidget> predefiniti,
        List<string> avvisi)
    {
        if (!radice.TryGetPropertyValue("widget", out var nodo) || nodo is not JsonArray elementi)
        {
            avvisi.Add("elenco 'widget' assente: uso i predefiniti");
            return predefiniti;
        }

        var lista = new List<OverlayWidget>();
        for (int i = 0; i < elementi.Count; i++)
        {
            var elemento = elementi[i];
            if (elemento is not JsonObject oggetto || !oggetto.ContainsKey("tipo") || !oggetto.ContainsKey("posizione"))
            {
                avvisi.Add($"widget {i} incompleto (manca tipo o posizione): omesso");
                continue;
            }

            try
            {
                var widget = elemento.Deserialize<OverlayWidget>(Opzioni);
                if (widget is not null)
                {
                    lista.Add(widget);
                }
            }
            catch (JsonException)
            {
                avvisi.Add($"widget {i} non valido (tipo o formato sconosciuti): omesso");
            }
        }

        return lista;
    }
}

/// <summary>Riporta un layout qualsiasi ai limiti: corregge i valori fuori intervallo e scarta ciò che non si può correggere.</summary>
internal static class OverlayLayoutNormalizer
{
    internal static OverlayLayout Normalizza(OverlayLayout layout, List<string> avvisi)
    {
        var predefinito = OverlayLayoutDefaults.Predefinito();

        var maschera = NormalizzaMaschera(layout.Maschera, predefinito.Maschera, avvisi);
        var stile = new OverlayStyle(
            ScalaTesto: Limita(layout.Stile.ScalaTesto, OverlayLimits.ScalaTestoMin, OverlayLimits.ScalaTestoMax, predefinito.Stile.ScalaTesto, "scala testo", avvisi),
            Margine: Limita(layout.Stile.Margine, 0, OverlayLimits.MargineMax, predefinito.Stile.Margine, "margine", avvisi));

        var widget = new List<OverlayWidget>();
        var gia = new HashSet<OverlayWidgetKind>();
        foreach (var w in layout.Widget)
        {
            if (!Enum.IsDefined(w.Tipo))
            {
                avvisi.Add("widget con tipo sconosciuto: omesso");
                continue;
            }

            if (!OverlayWidgetKinds.AmmetteIstanzeMultiple(w.Tipo) && !gia.Add(w.Tipo))
            {
                avvisi.Add($"tipo {w.Tipo} duplicato: ignoro la voce successiva");
                continue;
            }

            widget.Add(NormalizzaWidget(w, avvisi));
        }

        return new OverlayLayout(OverlayLayoutVersions.Corrente, maschera, stile, widget);
    }

    private static OverlayMask NormalizzaMaschera(OverlayMask m, OverlayMask predefinito, List<string> avvisi)
    {
        var modo = m.Modo;
        if (!Enum.IsDefined(modo))
        {
            avvisi.Add("modo maschera sconosciuto: nessuna maschera");
            modo = OverlayMaskMode.Nessuna;
        }

        var esterno = m.Cerchio.Esterno;
        if (!Enum.IsDefined(esterno))
        {
            avvisi.Add("bordo esterno sconosciuto: opaco");
            esterno = OverlayMaskEdge.Opaco;
        }

        var c = m.Cerchio;
        var cerchio = new OverlayCircleMask(
            CentroX: Limita(c.CentroX, OverlayLimits.CentroMin, OverlayLimits.CentroMax, predefinito.Cerchio.CentroX, "centro X", avvisi),
            CentroY: Limita(c.CentroY, OverlayLimits.CentroMin, OverlayLimits.CentroMax, predefinito.Cerchio.CentroY, "centro Y", avvisi),
            Raggio: Limita(c.Raggio, OverlayLimits.RaggioMin, OverlayLimits.RaggioMax, predefinito.Cerchio.Raggio, "raggio", avvisi),
            Esterno: esterno,
            Sfumatura: Limita(c.Sfumatura, 0, OverlayLimits.SfumaturaMax, predefinito.Cerchio.Sfumatura, "sfumatura", avvisi));

        double superiore = Limita(m.Bande.Superiore, 0, OverlayLimits.BandaMax, predefinito.Bande.Superiore, "banda superiore", avvisi);
        double inferiore = Limita(m.Bande.Inferiore, 0, OverlayLimits.BandaMax, predefinito.Bande.Inferiore, "banda inferiore", avvisi);
        if (superiore + inferiore >= 1)
        {
            avvisi.Add("bande nere troppo alte: ripristino i valori predefiniti");
            superiore = predefinito.Bande.Superiore;
            inferiore = predefinito.Bande.Inferiore;
        }

        return new OverlayMask(modo, cerchio, new OverlayBandMask(superiore, inferiore));
    }

    private static OverlayWidget NormalizzaWidget(OverlayWidget w, List<string> avvisi)
    {
        var tipo = w.Tipo;
        var posizione = w.Posizione;
        if (!Enum.IsDefined(posizione))
        {
            avvisi.Add($"posizione di {tipo} sconosciuta: angolo in alto a sinistra");
            posizione = OverlayPosition.TL;
        }

        int ordine = w.Ordine;
        if (ordine < 0 || ordine > OverlayLimits.OrdineMax)
        {
            avvisi.Add($"ordine di {tipo} fuori intervallo: corretto");
            ordine = Math.Clamp(ordine, 0, OverlayLimits.OrdineMax);
        }

        var predefinito = OverlayWidgetFormats.Predefinito(tipo);
        var f = w.Formato ?? predefinito;
        string etichetta = tipo.ToString();

        // Ogni tipo conserva solo i campi che gli competono; gli altri restano null.
        OverlayWidgetFormat formato = tipo switch
        {
            OverlayWidgetKind.DataOra => new OverlayWidgetFormat(
                Pattern: Scegli(f.Pattern, OverlayWidgetFormats.PatternValido, predefinito.Pattern!, $"formato di {etichetta}", avvisi),
                Fuso: ScegliVal(f.Fuso, v => Enum.IsDefined(v), predefinito.Fuso!.Value, $"fuso di {etichetta}", avvisi)),
            OverlayWidgetKind.Sole => new OverlayWidgetFormat(
                Decimali: ScegliVal(f.Decimali, d => d >= 0 && d <= OverlayLimits.DecimaliMax, predefinito.Decimali!.Value, $"decimali di {etichetta}", avvisi)),
            OverlayWidgetKind.Luna => new OverlayWidgetFormat(
                Decimali: ScegliVal(f.Decimali, d => d >= 0 && d <= OverlayLimits.DecimaliMax, predefinito.Decimali!.Value, $"decimali di {etichetta}", avvisi),
                MostraFase: f.MostraFase ?? predefinito.MostraFase!.Value),
            OverlayWidgetKind.Satelliti => new OverlayWidgetFormat(
                MaxElementi: ScegliVal(f.MaxElementi, m => m >= OverlayLimits.MaxElementiMin && m <= OverlayLimits.MaxElementiMax, predefinito.MaxElementi!.Value, $"numero di satelliti", avvisi)),
            OverlayWidgetKind.Meteo => new OverlayWidgetFormat(
                Campi: Scegli<IReadOnlyList<OverlayMeteoField>>(f.Campi, OverlayLayoutValidator.CampiValidi, predefinito.Campi!, "campi meteo", avvisi)),
            OverlayWidgetKind.Logo => new OverlayWidgetFormat(
                FileLogo: Scegli(f.FileLogo, OverlayWidgetFormats.FileLogoValido, predefinito.FileLogo!, "file del logo", avvisi),
                AltezzaMaxPercento: ScegliVal(f.AltezzaMaxPercento, a => OverlayLayoutValidator.Entro(a, OverlayLimits.AltezzaLogoMin, OverlayLimits.AltezzaLogoMax), predefinito.AltezzaMaxPercento!.Value, "altezza del logo", avvisi),
                Opacita: ScegliVal(f.Opacita, o => OverlayLayoutValidator.Entro(o, OverlayLimits.OpacitaMin, OverlayLimits.OpacitaMax), predefinito.Opacita!.Value, "opacità del logo", avvisi)),
            OverlayWidgetKind.TestoLibero => new OverlayWidgetFormat(
                Testo: Scegli(f.Testo, OverlayWidgetFormats.TestoValido, predefinito.Testo!, "testo libero", avvisi)),
            OverlayWidgetKind.Esposizione => new OverlayWidgetFormat(
                MostraGain: f.MostraGain ?? predefinito.MostraGain!.Value),
            _ => new OverlayWidgetFormat(),
        };

        return new OverlayWidget(tipo, w.Abilitato, posizione, ordine, formato);
    }

    /// <summary>Valore assente: predefinito in silenzio. Valore presente ma non valido: predefinito con avviso.</summary>
    private static T Scegli<T>(T? valore, Func<T, bool> valido, T predefinito, string nome, List<string> avvisi)
        where T : class
    {
        if (valore is null)
        {
            return predefinito;
        }

        if (valido(valore))
        {
            return valore;
        }

        avvisi.Add($"{nome} non valido: uso il predefinito");
        return predefinito;
    }

    private static T ScegliVal<T>(T? valore, Func<T, bool> valido, T predefinito, string nome, List<string> avvisi)
        where T : struct
    {
        if (valore is null)
        {
            return predefinito;
        }

        if (valido(valore.Value))
        {
            return valore.Value;
        }

        avvisi.Add($"{nome} non valido: uso il predefinito");
        return predefinito;
    }

    /// <summary>Porta il valore nell'intervallo, o usa il predefinito se non è un numero finito.</summary>
    private static double Limita(double valore, double minimo, double massimo, double predefinito, string nome, List<string> avvisi)
    {
        if (!double.IsFinite(valore))
        {
            avvisi.Add($"{nome} non numerico: uso il predefinito");
            return predefinito;
        }

        if (valore < minimo)
        {
            avvisi.Add($"{nome} sotto il minimo: portato a {minimo}");
            return minimo;
        }

        if (valore > massimo)
        {
            avvisi.Add($"{nome} sopra il massimo: portato a {massimo}");
            return massimo;
        }

        return valore;
    }
}
