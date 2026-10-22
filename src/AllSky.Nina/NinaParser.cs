using System.Text.Json;

namespace AllSky.Nina;

/// <summary>
/// Lettura difensiva delle risposte. Ogni campo mancante o di tipo sbagliato diventa null.
/// Nessuna eccezione: un JSON di forma inattesa produce un modello con campi null.
/// </summary>
internal static class NinaParser
{
    private const int MaxProfonditaAlbero = 32;
    private const int MaxNodiAlbero = 20_000;
    private const int MaxLunghezzaTesto = 200;

    /// <summary>
    /// Dati della risposta. Se la radice e' il contenitore ninaAPI (proprieta' "Response"), restituisce il suo valore;
    /// altrimenti la radice cosi' com'e' (compatibilita' con risposte non avvolte).
    /// </summary>
    public static JsonElement Dati(JsonElement radice) =>
        radice.ValueKind == JsonValueKind.Object && TryProprieta(radice, NinaFieldNames.Envelope, out JsonElement dati)
            ? dati
            : radice;

    /// <summary>
    /// Errore dichiarato dal contenitore: "Success" false oppure "StatusCode" >= 400. Null se non c'e' errore
    /// o se la risposta non e' avvolta. Non restituisce il campo "Error" di NINA: puo' contenere dettagli interni.
    /// </summary>
    public static string? ErroreContenitore(JsonElement radice)
    {
        if (radice.ValueKind != JsonValueKind.Object || !TryProprieta(radice, NinaFieldNames.Envelope, out _))
        {
            return null;
        }

        int? codice = CodiceStato(radice);
        bool fallito = TryProprieta(radice, NinaFieldNames.EnvelopeSuccess, out JsonElement successo)
            && successo.ValueKind == JsonValueKind.False;

        if (!fallito && codice is not >= 400)
        {
            return null;
        }

        return codice is int c ? $"errore NINA (StatusCode {c})" : "errore NINA";
    }

    /// <summary>Codice di stato del contenitore, se presente e intero.</summary>
    public static int? CodiceStato(JsonElement radice)
    {
        if (radice.ValueKind != JsonValueKind.Object || !TryProprieta(radice, NinaFieldNames.EnvelopeStatusCode, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int c) ? c : null;
    }

    /// <summary>Legge lo stato del mount da <c>equipment/mount/info</c>. Accetta oggetto o array (primo elemento).</summary>
    public static NinaMountState Mount(JsonElement radiceGrezza)
    {
        JsonElement radice = Primo(radiceGrezza);
        bool? atPark = Booleano(radice, NinaFieldNames.MountAtPark);
        bool? slewing = Booleano(radice, NinaFieldNames.MountSlewing);
        string? tracking = Testo(radice, NinaFieldNames.MountTrackingMode);
        double? raDeg = Ascensione(radice, NinaFieldNames.MountRightAscension);
        double? decDeg = Declinazione(radice, NinaFieldNames.MountDeclination);
        return new NinaMountState(atPark, slewing, tracking, raDeg, decDeg);
    }

    /// <summary>
    /// Cerca il primo nodo della sequenza che porta un nome di target o le coordinate di input.
    /// Il nodo trovato e' il primo in ordine di documento, non per forza quello in esecuzione [NV].
    /// </summary>
    public static NinaTargetState? FindTarget(JsonElement radice)
    {
        int nodi = 0;
        return CercaTarget(radice, 0, ref nodi);
    }

    /// <summary>
    /// Legge lo stato della sequenza da <c>sequence/state</c>: testo diretto, campo di stato o primo elemento di un array.
    /// </summary>
    public static string? SequenceState(JsonElement radiceGrezza)
    {
        JsonElement radice = Primo(radiceGrezza);
        return radice.ValueKind switch
        {
            JsonValueKind.String => TestoDiretto(radice),
            JsonValueKind.Object => Testo(radice, NinaFieldNames.SequenceStatus),
            _ => null,
        };
    }

    /// <summary>Primo elemento se la radice e' un array non vuoto, altrimenti la radice stessa.</summary>
    private static JsonElement Primo(JsonElement radice)
    {
        if (radice.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in radice.EnumerateArray())
            {
                return e;
            }
        }

        return radice;
    }

    private static NinaTargetState? CercaTarget(JsonElement elemento, int profondita, ref int nodi)
    {
        if (profondita > MaxProfonditaAlbero || ++nodi > MaxNodiAlbero)
        {
            return null;
        }

        if (elemento.ValueKind == JsonValueKind.Object)
        {
            if (Ha(elemento, NinaFieldNames.TargetName) || Ha(elemento, NinaFieldNames.TargetCoordinates))
            {
                NinaTargetState trovato = LeggiTarget(elemento);
                if (trovato.Name is not null || trovato.RaDeg is not null || trovato.DecDeg is not null)
                {
                    return trovato;
                }
            }

            foreach (JsonProperty figlio in elemento.EnumerateObject())
            {
                NinaTargetState? dentro = CercaTarget(figlio.Value, profondita + 1, ref nodi);
                if (dentro is not null)
                {
                    return dentro;
                }
            }
        }
        else if (elemento.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement figlio in elemento.EnumerateArray())
            {
                NinaTargetState? dentro = CercaTarget(figlio, profondita + 1, ref nodi);
                if (dentro is not null)
                {
                    return dentro;
                }
            }
        }

        return null;
    }

    private static NinaTargetState LeggiTarget(JsonElement nodo)
    {
        string? nome = Testo(nodo, NinaFieldNames.TargetName);
        double? ra = null;
        double? dec = null;

        if (TryProprieta(nodo, NinaFieldNames.TargetCoordinates, out JsonElement input)
            && TryProprieta(input, NinaFieldNames.TargetCoordinatesValue, out JsonElement coord))
        {
            ra = Ascensione(coord, NinaFieldNames.CoordinateRightAscension);
            dec = Declinazione(coord, NinaFieldNames.CoordinateDeclination);
        }

        return new NinaTargetState(nome, ra, dec);
    }

    /// <summary>Ascensione retta in gradi. Scartata se fuori dall'intervallo atteso.</summary>
    private static double? Ascensione(JsonElement obj, string nome)
    {
        double? valore = Numero(obj, nome);
        if (valore is not { } v)
        {
            return null;
        }

        double limite = NinaFieldNames.RightAscensionInHours ? 24.0 : 360.0;
        double fattore = NinaFieldNames.RightAscensionInHours ? 15.0 : 1.0;
        return v >= 0.0 && v < limite ? v * fattore : null;
    }

    /// <summary>Declinazione in gradi. Scartata se fuori da -90..90.</summary>
    private static double? Declinazione(JsonElement obj, string nome)
    {
        double? valore = Numero(obj, nome);
        return valore is { } v && v >= -90.0 && v <= 90.0 ? v : null;
    }

    private static bool Ha(JsonElement obj, string nome) => TryProprieta(obj, nome, out _);

    private static bool TryProprieta(JsonElement obj, string nome, out JsonElement valore)
    {
        valore = default;
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase))
            {
                valore = p.Value;
                return true;
            }
        }

        return false;
    }

    private static bool? Booleano(JsonElement obj, string nome)
    {
        if (!TryProprieta(obj, nome, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static double? Numero(JsonElement obj, string nome)
    {
        if (!TryProprieta(obj, nome, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d) ? d : null;
    }

    private static string? Testo(JsonElement obj, string nome) =>
        TryProprieta(obj, nome, out JsonElement v) ? TestoDiretto(v) : null;

    private static string? TestoDiretto(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? s = v.GetString()?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        return s.Length > MaxLunghezzaTesto ? s[..MaxLunghezzaTesto] : s;
    }
}
