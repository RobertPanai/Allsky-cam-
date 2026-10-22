namespace AllSky.YouTube;

/// <summary>
/// Sorgente dell'ora UTC, iniettabile per i test. Ha la stessa forma di <c>AllSky.Core.IClock</c>, ma e' qui
/// ridefinita perche' questo progetto non ha riferimenti ad altri progetti. L'Host puo' passare il proprio orologio.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Privacy del video caricato. Il valore predefinito e' <see cref="Private"/>.</summary>
public enum YouTubePrivacy
{
    Private,
    Unlisted,
    Public,
}

/// <summary>Valori di <c>status.privacyStatus</c> nell'API.</summary>
public static class YouTubePrivacyApi
{
    public static string ToApiValue(this YouTubePrivacy privacy) => privacy switch
    {
        YouTubePrivacy.Private => "private",
        YouTubePrivacy.Unlisted => "unlisted",
        YouTubePrivacy.Public => "public",
        _ => throw new ArgumentOutOfRangeException(nameof(privacy), privacy, "Privacy sconosciuta."),
    };
}

/// <summary>
/// Impostazioni del caricamento automatico. <see cref="CategoryId"/> e' un valore configurabile: l'ID categoria
/// non e' verificato nella documentazione ufficiale letta il 2026-10-22, quindi l'utente puo' cambiarlo.
/// </summary>
/// <param name="DailyTime">Ora locale del caricamento giornaliero.</param>
/// <param name="CatchUpWindow">Se il PC era spento all'ora prevista, il caricamento parte all'avvio successivo solo entro questa finestra.</param>
/// <param name="Location">Luogo usato da {luogo} nei modelli.</param>
/// <param name="MaxUploadsPerDay">Limite di nuovi caricamenti al giorno (la ripresa di un caricamento interrotto non conta).</param>
public sealed record YouTubeUploadSettings(
    TimeOnly DailyTime,
    TimeSpan CatchUpWindow,
    YouTubePrivacy Privacy,
    IReadOnlyList<string> Tags,
    string CategoryId,
    string Location,
    string TitleTemplate,
    string DescriptionTemplate,
    int MaxUploadsPerDay)
{
    /// <summary>Valori iniziali: 08:00 locali, finestra di 12 ore, privacy private, un caricamento al giorno.</summary>
    public static YouTubeUploadSettings Defaults => new(
        new TimeOnly(8, 0),
        TimeSpan.FromHours(12),
        YouTubePrivacy.Private,
        new[] { "timelapse", "allsky", "astrofotografia" },
        "28",
        "Osservatorio",
        "Timelapse notte del {notte:yyyy-MM-dd}",
        "Timelapse della notte del {notte:yyyy-MM-dd} da {luogo}: {fotogrammi} fotogrammi, durata {durata}, meteore {meteore}, transiti {transiti}.",
        1);
}

/// <summary>Timelapse dell'ultima notte completata, fornito da <see cref="ILatestTimelapseProvider"/>.</summary>
/// <param name="NightId">Identificatore della notte (chiave dell'idempotenza), es. "2026-10-21".</param>
/// <param name="NightStartLocal">Inizio della notte in ora locale, per {notte}.</param>
/// <param name="FilePath">File MP4 del timelapse, gia' composto (con maschera se attiva).</param>
public sealed record LatestTimelapse(
    string NightId,
    DateTime NightStartLocal,
    string FilePath,
    int Frames,
    TimeSpan VideoDuration,
    int Meteors,
    int Transits);

/// <summary>Fonte del timelapse da caricare. Implementata dall'Host verso l'archivio notti, e nei test con un fake.</summary>
public interface ILatestTimelapseProvider
{
    /// <summary>Ultima notte completata con timelapse pronto, oppure null se non ce n'e' nessuna.</summary>
    LatestTimelapse? GetLatestCompleted();
}

/// <summary>Metadati inviati nella risorsa <c>video</c> all'avvio della sessione.</summary>
public sealed record VideoMetadata(
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    string CategoryId,
    YouTubePrivacy Privacy);

/// <summary>
/// Avanzamento di una richiesta di upload. <see cref="NextOffset"/> e' il primo byte non ancora ricevuto.
/// <see cref="VideoId"/> valorizzato solo quando il server ha completato il caricamento (200/201).
/// <see cref="RetryAfter"/> e' l'eventuale attesa richiesta da un 308 con Retry-After.
/// </summary>
public sealed record UploadProgress(long NextOffset, string? VideoId, TimeSpan? RetryAfter)
{
    public bool Completed => VideoId is not null;
}

/// <summary>Retry con backoff esponenziale senza jitter: attesa = Base * 2^(tentativo-1), limitata a MaxDelay.</summary>
public sealed record RetryPolicy(int MaxAttempts, TimeSpan BaseDelay, TimeSpan MaxDelay)
{
    /// <summary>Cinque tentativi, attese di 2, 4, 8, 16 secondi (massimo 2 minuti).</summary>
    public static RetryPolicy Default => new(5, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(2));

    /// <summary>Attesa prima del tentativo successivo a quello indicato (1-based).</summary>
    public TimeSpan DelayAfter(int attempt)
    {
        double seconds = BaseDelay.TotalSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(MaxDelay.TotalSeconds, seconds));
    }
}

/// <summary>Esito di un tentativo di caricamento o di tick del pianificatore. Il messaggio e' leggibile e non contiene segreti.</summary>
public sealed record UploadOutcome(UploadOutcomeKind Kind, string Message, string? VideoId = null);

public enum UploadOutcomeKind
{
    /// <summary>Video caricato in questa esecuzione.</summary>
    Completed,

    /// <summary>La notte risulta gia' caricata: nessun nuovo upload.</summary>
    AlreadyUploaded,

    /// <summary>Caricamento in corso o da riprendere (errore transitorio, nuovo tentativo programmato).</summary>
    InProgress,

    /// <summary>Caricamento interrotto in modo ambiguo: serve una verifica dell'utente prima di ricaricare.</summary>
    NeedsReview,

    /// <summary>Quota YouTube esaurita: nessun tentativo fino al giorno successivo.</summary>
    QuotaBlocked,

    /// <summary>Token non rinnovabile: serve un nuovo login.</summary>
    AuthRequired,

    /// <summary>Errore permanente per questa notte.</summary>
    Failed,

    /// <summary>Impostazioni o modelli non validi, oppure file mancante: nessuna chiamata di rete.</summary>
    ConfigError,

    /// <summary>Nessuna notte completata disponibile.</summary>
    NoNight,

    /// <summary>Raggiunto il limite giornaliero di nuovi caricamenti.</summary>
    DailyLimitReached,

    /// <summary>Caricamento non dovuto adesso (prima dell'ora, gia' eseguito oggi, in attesa di ripresa).</summary>
    NotDue,

    /// <summary>L'ora prevista e' passata oltre la finestra di recupero: nessun caricamento per oggi.</summary>
    SlotMissed,
}
