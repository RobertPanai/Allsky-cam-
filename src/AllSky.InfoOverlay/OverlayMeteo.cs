using System.Globalization;
using AllSky.Core;
using AllSky.Weather;

namespace AllSky.InfoOverlay;

/// <summary>
/// Insieme dei campi meteo che il widget <see cref="OverlayWidgetKind.Meteo"/> può mostrare.
/// L'elenco è quello di <see cref="OverlayMeteoField"/>; l'ordine scelto dall'utente è l'ordine delle righe.
/// </summary>
public static class OverlayMeteoFields
{
    /// <summary>Tutti i campi, nell'ordine dell'enum.</summary>
    public static IReadOnlyList<OverlayMeteoField> Tutti { get; } = Enum.GetValues<OverlayMeteoField>();

    /// <summary>
    /// Problemi di un elenco di campi scelto dall'utente. Vuoto se l'elenco è valido:
    /// almeno un campo, nessun campo ripetuto, solo valori dell'enum.
    /// </summary>
    public static IReadOnlyList<string> Valida(IReadOnlyList<OverlayMeteoField>? campi)
    {
        var errori = new List<string>();
        if (campi is null)
        {
            errori.Add("elenco dei campi meteo assente");
            return errori;
        }

        if (campi.Count == 0)
        {
            errori.Add("nessun campo meteo selezionato");
        }

        if (campi.Any(c => !Enum.IsDefined(c)))
        {
            errori.Add("campo meteo non valido");
        }

        if (campi.Distinct().Count() != campi.Count)
        {
            errori.Add("campo meteo ripetuto");
        }

        return errori;
    }

    /// <summary>
    /// Elenco pulito da una selezione: scarta i valori non validi e i ripetuti, mantiene l'ordine di comparsa.
    /// Non aggiunge campi: una selezione vuota resta vuota.
    /// </summary>
    public static IReadOnlyList<OverlayMeteoField> Normalizza(IEnumerable<OverlayMeteoField> campi) =>
        campi.Where(c => Enum.IsDefined(c)).Distinct().ToList();
}

/// <summary>
/// Testo delle righe del widget Meteo. Funzione pura: nessun accesso a sensori, rete o orologio.
/// </summary>
/// <remarks>
/// Un dato mancante (lettura assente o campo null) diventa "n/d" in italiano e "n/a" in inglese.
/// Decimali: una cifra per temperatura, vento e pioggia; due per la luminosità del cielo; zero per il resto.
/// In italiano la virgola è il separatore decimale. La formattazione non dipende dalla cultura del sistema.
/// </remarks>
public static class OverlayMeteoText
{
    /// <summary>Restituisce una riga per campo, nell'ordine dell'elenco. Elenco nullo o vuoto: nessuna riga.</summary>
    /// <param name="meteo">Lettura meteo, o null se non disponibile: tutte le righe diventano "n/d".</param>
    /// <param name="campi">Campi scelti per il widget.</param>
    /// <param name="lingua">Lingua delle etichette e dei dati mancanti.</param>
    public static IReadOnlyList<string> Righe(WeatherSnapshot? meteo, IReadOnlyList<OverlayMeteoField>? campi, Language lingua)
    {
        if (campi is null)
        {
            return Array.Empty<string>();
        }

        var righe = new List<string>(campi.Count);
        foreach (OverlayMeteoField campo in campi)
        {
            if (Enum.IsDefined(campo))
            {
                righe.Add(Riga(meteo, campo, lingua));
            }
        }

        return righe;
    }

    /// <summary>Una riga: "Etichetta: valore unità".</summary>
    internal static string Riga(WeatherSnapshot? m, OverlayMeteoField campo, Language lingua)
    {
        bool it = lingua == Language.It;
        string mancante = it ? "n/d" : "n/a";

        return campo switch
        {
            OverlayMeteoField.Temperatura =>
                Etichetta(it ? "Temperatura" : "Temperature", Valore(m?.TemperatureC, 1, "°C", mancante, it)),
            OverlayMeteoField.Umidita =>
                Etichetta(it ? "Umidità" : "Humidity", Valore(m?.HumidityPercent, 0, "%", mancante, it)),
            OverlayMeteoField.Pressione =>
                Etichetta(it ? "Pressione" : "Pressure", Valore(m?.PressureHpa, 0, "hPa", mancante, it)),
            OverlayMeteoField.Vento => Etichetta(it ? "Vento" : "Wind", Vento(m, it, mancante)),
            OverlayMeteoField.Nuvole =>
                Etichetta(it ? "Nuvole" : "Cloud cover", Valore(m?.CloudCoverPercent, 0, "%", mancante, it)),
            OverlayMeteoField.Pioggia =>
                Etichetta(it ? "Pioggia" : "Rain", Valore(m?.RainRateMmH, 1, "mm/h", mancante, it)),
            OverlayMeteoField.LuminositaCielo =>
                Etichetta(
                    it ? "Luminosità cielo" : "Sky brightness",
                    Valore(m?.SkyQualityMagArcsec2, 2, "mag/arcsec²", mancante, it)),
            _ => Etichetta(campo.ToString(), mancante),
        };
    }

    private static string Vento(WeatherSnapshot? m, bool it, string mancante)
    {
        string velocita = Valore(m?.WindSpeedMs, 1, "m/s", mancante, it);
        if (m?.WindDirectionDeg is not { } gradi || !double.IsFinite(gradi))
        {
            return velocita;
        }

        string direzione = Numero(gradi, 0, it) + "°";
        return it ? $"{velocita}, da {direzione}" : $"{velocita} from {direzione}";
    }

    private static string Etichetta(string nome, string valore) => nome + ": " + valore;

    /// <summary>Valore con unità, oppure il segnaposto per i dati mancanti (senza unità).</summary>
    private static string Valore(double? valore, int decimali, string unita, string mancante, bool it)
    {
        if (valore is not { } v || !double.IsFinite(v))
        {
            return mancante;
        }

        return Numero(v, decimali, it) + " " + unita;
    }

    private static string Numero(double valore, int decimali, bool it)
    {
        string testo = valore.ToString("F" + decimali.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return it ? testo.Replace('.', ',') : testo;
    }
}
