using System.Globalization;
using System.Text.RegularExpressions;

namespace AllSky.Core.CameraView;

/// <summary>Classe di un errore della camera, dedotta dal testo che l'Engine restituisce.</summary>
public enum CameraErrorKind
{
    /// <summary>Il servizio non è avviato.</summary>
    ServiceNotStarted,

    /// <summary>Sensore monocromatico: All Sky Brain non lo supporta.</summary>
    MonochromeSensor,

    /// <summary>Altro sensore non supportato (CMYG, LRGB, colore senza matrice di Bayer).</summary>
    UnsupportedSensor,

    /// <summary>Il driver riporta limiti di esposizione non validi (versione 0.2.1 e successive).</summary>
    DriverExposureLimits,

    /// <summary>La cattura non è partita con la camera collegata.</summary>
    CaptureStart,

    /// <summary>La ricerca delle camere non è riuscita.</summary>
    Discovery,

    /// <summary>La camera non risponde alla connessione.</summary>
    Connection,

    /// <summary>Impostazioni non valide.</summary>
    ConfigInvalid,

    /// <summary>La configurazione non si è salvata.</summary>
    SaveFailed,

    /// <summary>Errore non riconosciuto.</summary>
    Unknown,
}

/// <summary>Limiti di esposizione riportati dal driver e quelli effettivamente usati, come testo dell'Engine.</summary>
/// <param name="DriverMin">Minimo riportato dal driver (testo grezzo, es. "0" o "NaN").</param>
/// <param name="DriverMax">Massimo riportato dal driver.</param>
/// <param name="UsedMin">Minimo usato dall'app.</param>
/// <param name="UsedMax">Massimo usato dall'app.</param>
public sealed record ExposureLimitsText(string DriverMin, string DriverMax, string UsedMin, string UsedMax);

/// <summary>Messaggio leggibile per la vista Camera: classe, testo principale e dettaglio tecnico facoltativo.</summary>
/// <param name="Kind">Classe dell'errore.</param>
/// <param name="Message">Testo da mostrare, nella lingua scelta. Dice cosa è successo e cosa fare.</param>
/// <param name="Detail">Una riga di dettaglio tecnico, senza stack trace; null se non serve.</param>
public sealed record CameraErrorView(CameraErrorKind Kind, string Message, string? Detail);

/// <summary>
/// Traduce gli errori dell'Engine (testi in italiano con la causa del driver) in messaggi leggibili IT/EN.
/// Logica pura: nessuna dipendenza da WPF né dall'Engine. Non lancia mai per testi sconosciuti.
/// </summary>
public static class CameraErrorFormatter
{
    /// <summary>Lunghezza massima del dettaglio tecnico, in caratteri.</summary>
    public const int MaxDetailLength = 300;

    private static readonly string[] MonochromeMarkers = ["monocrom", "monochrome"];

    private static readonly string[] UnsupportedSensorMarkers =
    [
        "matrice bayer",
        "matrice di bayer",
        "sensore ascom di tipo",
        "sensortype",
        "cmyg",
        "lrgb",
    ];

    private static readonly Regex ExposureLimitsPattern = new(
        @"min=(?<dmin>[^,)]+),\s*max=(?<dmax>[^)]+)\).*?uso min=(?<umin>[^,]+),\s*max=(?<umax>[^\s,]+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>Dedotta la classe di errore dal testo dell'Engine. Testo nullo o vuoto: <see cref="CameraErrorKind.Unknown"/>.</summary>
    /// <param name="raw">Messaggio ricevuto da <c>ConnectResult.Error</c>, <c>StatusDto.Error</c> o da un'eccezione dell'Engine.</param>
    public static CameraErrorKind Classify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CameraErrorKind.Unknown;
        }

        if (raw.Contains("Limiti di esposizione del driver", StringComparison.OrdinalIgnoreCase))
        {
            return CameraErrorKind.DriverExposureLimits;
        }

        // Prima dei controlli sui driver: il messaggio di un sensore monocromatico contiene anche "non supportato".
        if (ContainsAny(raw, MonochromeMarkers))
        {
            return CameraErrorKind.MonochromeSensor;
        }

        if (ContainsAny(raw, UnsupportedSensorMarkers))
        {
            return CameraErrorKind.UnsupportedSensor;
        }

        if (raw.Contains("non è avviato", StringComparison.OrdinalIgnoreCase))
        {
            return CameraErrorKind.ServiceNotStarted;
        }

        if (raw.Contains("Ricerca delle camere", StringComparison.OrdinalIgnoreCase))
        {
            return CameraErrorKind.Discovery;
        }

        if (raw.Contains("Avvio della cattura", StringComparison.OrdinalIgnoreCase))
        {
            return CameraErrorKind.CaptureStart;
        }

        if (raw.Contains("Configurazione non valida", StringComparison.OrdinalIgnoreCase))
        {
            return CameraErrorKind.ConfigInvalid;
        }

        if (raw.Contains("Salvataggio della configurazione", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("Impossibile salvare", StringComparison.OrdinalIgnoreCase))
        {
            return CameraErrorKind.SaveFailed;
        }

        if (raw.Contains("Connessione alla camera", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("non è disponibile", StringComparison.OrdinalIgnoreCase))
        {
            return CameraErrorKind.Connection;
        }

        return CameraErrorKind.Unknown;
    }

    /// <summary>
    /// Legge i valori dei limiti di esposizione dal messaggio dell'Engine, per esempio
    /// "... non validi (min=0, max=NaN): uso min=0.001, max=30000". Restituisce null se il testo non li contiene.
    /// </summary>
    /// <param name="raw">Messaggio dell'Engine.</param>
    public static ExposureLimitsText? ParseExposureLimits(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        Match match = ExposureLimitsPattern.Match(raw);
        if (!match.Success)
        {
            return null;
        }

        return new ExposureLimitsText(
            match.Groups["dmin"].Value.Trim(),
            match.Groups["dmax"].Value.Trim(),
            match.Groups["umin"].Value.Trim(),
            match.Groups["umax"].Value.Trim());
    }

    /// <summary>Messaggio leggibile per un errore dell'Engine. Non mostra mai uno stack trace.</summary>
    /// <param name="raw">Messaggio dell'Engine; null se non c'è errore (il risultato è comunque un messaggio generico).</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static CameraErrorView Describe(string? raw, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        CameraErrorKind kind = Classify(raw);
        string message = kind switch
        {
            CameraErrorKind.ServiceNotStarted => texts.CameraErrorServiceNotStarted,
            CameraErrorKind.MonochromeSensor => texts.CameraErrorMonochrome,
            CameraErrorKind.UnsupportedSensor => texts.CameraErrorUnsupportedSensor,
            CameraErrorKind.DriverExposureLimits => ExposureLimitsMessage(raw, texts),
            CameraErrorKind.CaptureStart => texts.CameraErrorCaptureStart,
            CameraErrorKind.Discovery => texts.CameraErrorDiscovery,
            CameraErrorKind.Connection => texts.CameraErrorConnection,
            CameraErrorKind.ConfigInvalid => texts.CameraErrorConfigInvalid,
            CameraErrorKind.SaveFailed => texts.CameraErrorSaveFailed,
            _ => texts.CameraErrorUnknown,
        };

        // Per i casi già spiegati nel messaggio il dettaglio tecnico sarebbe solo rumore.
        string? detail = ShowsDetail(kind) ? Summarize(raw) : null;
        return new CameraErrorView(kind, message, detail);
    }

    /// <summary>Messaggio per gli errori di validazione della configurazione (<c>UpdateConfig</c>).</summary>
    /// <param name="errors">Errori restituiti dall'Engine; non vuoto nel caso normale.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static CameraErrorView DescribeValidation(IReadOnlyList<ValidationError> errors, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(texts);

        // Il percorso "file" indica un salvataggio su disco fallito, non un valore sbagliato.
        if (errors.Any(e => string.Equals(e.Path, "file", StringComparison.Ordinal)))
        {
            return new CameraErrorView(
                CameraErrorKind.SaveFailed,
                texts.CameraErrorSaveFailed,
                Summarize(string.Join("; ", errors.Select(e => e.Message))));
        }

        string detail = string.Join("; ", errors.Select(e => $"{e.Path}: {e.Message}"));
        return new CameraErrorView(CameraErrorKind.ConfigInvalid, texts.CameraErrorConfigInvalid, Summarize(detail));
    }

    /// <summary>Riduce un testo tecnico a una sola riga, senza spazi multipli e con lunghezza limitata. Null se vuoto.</summary>
    /// <param name="text">Testo grezzo, anche su più righe.</param>
    public static string? Summarize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Solo la prima riga: le righe successive di un'eccezione (stack trace) non entrano mai nell'interfaccia.
        string firstLine = text.Split(new[] { '\r', '\n' }, 2, StringSplitOptions.RemoveEmptyEntries)[0];
        string oneLine = WhitespacePattern.Replace(firstLine, " ").Trim();
        if (oneLine.Length == 0)
        {
            return null;
        }

        return oneLine.Length <= MaxDetailLength
            ? oneLine
            : oneLine[..MaxDetailLength] + "…";
    }

    private static string ExposureLimitsMessage(string? raw, DesktopTexts texts)
    {
        ExposureLimitsText? limits = ParseExposureLimits(raw);
        if (limits is null)
        {
            return texts.CameraErrorDriverExposureLimitsUnknown;
        }

        return string.Format(
            texts.Culture,
            texts.CameraErrorDriverExposureLimits,
            FormatMilliseconds(limits.DriverMin, texts),
            FormatMilliseconds(limits.DriverMax, texts),
            FormatMilliseconds(limits.UsedMin, texts),
            FormatMilliseconds(limits.UsedMax, texts));
    }

    /// <summary>Valore in millisecondi per la schermata; "NaN", infiniti e valori illeggibili diventano "n/d".</summary>
    private static string FormatMilliseconds(string token, DesktopTexts texts)
    {
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && double.IsFinite(value))
        {
            return value.ToString("0.###", texts.Culture);
        }

        return texts.NotAvailable;
    }

    private static bool ShowsDetail(CameraErrorKind kind) => kind is
        CameraErrorKind.CaptureStart
        or CameraErrorKind.Discovery
        or CameraErrorKind.Connection
        or CameraErrorKind.SaveFailed
        or CameraErrorKind.Unknown;

    private static bool ContainsAny(string text, string[] markers)
    {
        foreach (string marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
