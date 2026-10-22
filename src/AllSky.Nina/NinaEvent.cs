namespace AllSky.Nina;

/// <summary>
/// Evento della cronologia di NINA. Due sorgenti: <c>websocket</c> (ninaAPI, nomi in <see cref="NinaWebSocketEvents"/>)
/// e <c>log</c> (riga del file di log, vedi <see cref="INinaLogParser"/>).
/// <see cref="AtUtc"/> e' gia' in UTC e gia' corretto con <c>clockOffsetSeconds</c> (vedi <see cref="NinaClock"/>).
/// <see cref="Message"/> e' ripulito da <see cref="NinaMessageSanitizer"/>.
/// Si costruisce solo con le fabbriche (<see cref="FromWebSocket"/>), che verificano nome e orario.
/// </summary>
public sealed record NinaEvent(
    DateTimeOffset AtUtc,
    string Source,
    string Name,
    string? Level,
    string Message,
    string? Category)
{
    /// <summary>Sorgente: evento del WebSocket di ninaAPI.</summary>
    public const string SourceWebSocket = "websocket";

    /// <summary>Sorgente: riga del file di log di NINA.</summary>
    public const string SourceLog = "log";

    /// <summary>
    /// Nome delle righe di log. Il formato del log non e' verificato (docs/NINA-API.md §5): non si deduce un nome di evento dal testo.
    /// </summary>
    public const string LogLineName = "log";

    /// <summary>
    /// Crea un evento dal WebSocket di ninaAPI. Il nome deve essere uno di <see cref="NinaWebSocketEvents.Known"/>:
    /// nomi non confermati vengono rifiutati. <paramref name="ninaClockInstant"/> e' l'orario che NINA associa al messaggio.
    /// </summary>
    /// <param name="eventName">Nome dell'evento, es. <c>AUTOFOCUS-FINISHED</c>.</param>
    /// <param name="ninaClockInstant">Istante del messaggio secondo l'orologio di NINA (qualsiasi offset).</param>
    /// <param name="clockOffsetSeconds">Correzione dell'orologio, da -600 a 600 secondi.</param>
    /// <param name="message">Testo opzionale; se manca si usa il nome dell'evento.</param>
    public static NinaEventResult FromWebSocket(
        string? eventName,
        DateTimeOffset ninaClockInstant,
        double clockOffsetSeconds,
        string? message = null)
    {
        if (string.IsNullOrWhiteSpace(eventName) || !NinaWebSocketEvents.Known.Contains(eventName))
        {
            return NinaEventResult.Rejected("evento WebSocket non confermato da docs/NINA-API.md §4");
        }

        try
        {
            DateTimeOffset at = NinaClock.ToUtc(ninaClockInstant, clockOffsetSeconds);
            string text = NinaMessageSanitizer.Clean(string.IsNullOrWhiteSpace(message) ? eventName : message);
            return NinaEventResult.Ok(new NinaEvent(
                at,
                SourceWebSocket,
                eventName,
                null,
                text,
                NinaWebSocketEvents.CategoryOf(eventName)));
        }
        catch (ArgumentOutOfRangeException)
        {
            return NinaEventResult.Rejected("correzione dell'orologio fuori intervallo");
        }
    }
}

/// <summary>Livelli di log normalizzati. Il nome e' quello della documentazione di NINA (docs/NINA-API.md §5).</summary>
public static class NinaLogLevels
{
    public const string Error = "Error";
    public const string Warning = "Warning";
    public const string Info = "Info";
    public const string Debug = "Debug";
    public const string Trace = "Trace";
}

/// <summary>Esito di una lettura: un evento, oppure il motivo del rifiuto (mai un'eccezione).</summary>
public sealed record NinaEventResult(NinaEvent? Event, string? Reason)
{
    /// <summary>Vero se la riga o il messaggio e' stato riconosciuto.</summary>
    public bool IsRecognized => Event is not null;

    /// <summary>Esito positivo.</summary>
    public static NinaEventResult Ok(NinaEvent ev) => new(ev, null);

    /// <summary>Esito negativo con il motivo.</summary>
    public static NinaEventResult Rejected(string reason) => new(null, reason);
}

/// <summary>
/// Nomi degli eventi WebSocket di ninaAPI, letti dal sorgente (docs/NINA-API.md §4, [V]).
/// Nessun nome aggiunto: se un evento non e' qui, non viene salvato.
/// </summary>
public static class NinaWebSocketEvents
{
    /// <summary>Tutti i nomi confermati.</summary>
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        "MOUNT-PARKED", "MOUNT-UNPARKED", "MOUNT-BEFORE-FLIP", "MOUNT-AFTER-FLIP", "MOUNT-HOMED",
        "MOUNT-CONNECTED", "MOUNT-DISCONNECTED",
        "SEQUENCE-STARTING", "SEQUENCE-FINISHED", "SEQUENCE-ENTITY-FAILED",
        "AUTOFOCUS-STARTING", "AUTOFOCUS-POINT-ADDED", "AUTOFOCUS-FINISHED",
        "GUIDER-START", "GUIDER-STOP", "GUIDER-DITHER",
        "CAMERA-CONNECTED", "CAMERA-DISCONNECTED", "CAMERA-DOWNLOAD-TIMEOUT", "API-CAPTURE-FINISHED",
        "PROFILE-CHANGED", "STACK-UPDATED", "STACK-STATUS",
        "SAFETY-CONNECTED", "SAFETY-DISCONNECTED", "WEATHER-CONNECTED", "WEATHER-DISCONNECTED",
    };

    /// <summary>
    /// Categoria dedotta dal nome, solo per i casi confermati. Null per gli altri (per esempio il parcheggio:
    /// nessuna categoria confermata da P11.0).
    /// </summary>
    public static string? CategoryOf(string eventName) => eventName switch
    {
        "AUTOFOCUS-STARTING" or "AUTOFOCUS-POINT-ADDED" or "AUTOFOCUS-FINISHED" => "messa a fuoco",
        "MOUNT-BEFORE-FLIP" or "MOUNT-AFTER-FLIP" => "meridian flip",
        "GUIDER-DITHER" => "dither",
        "GUIDER-START" or "GUIDER-STOP" => "guida",
        "SEQUENCE-ENTITY-FAILED" or "CAMERA-DOWNLOAD-TIMEOUT" => "errore",
        _ => null,
    };
}
