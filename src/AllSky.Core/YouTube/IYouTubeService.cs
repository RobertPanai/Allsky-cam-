namespace AllSky.Core.YouTube;

/// <summary>
/// Accesso della vista YouTube al caricamento automatico. Chi la implementa: l'Host (<c>AllSky.Host</c>), che avvolge
/// l'autenticazione OAuth, lo scheduler e la configurazione. Il contratto non contiene mai token, secret o URI di sessione.
/// </summary>
/// <remarks>
/// <para>
/// I metodi non lanciano per gli errori attesi (login negato, quota, impostazioni non valide): l'esito è nel risultato, con un
/// <see cref="YouTubeProblem"/> da tradurre con <see cref="YouTubeStatusFormatter"/>. Fanno eccezione solo
/// <see cref="OperationCanceledException"/> per l'annullamento.
/// </para>
/// <para>
/// <see cref="StatusChanged"/> arriva dal thread che ha cambiato lo stato, anche dallo sfondo: chi lo ascolta deve
/// tornare sul thread dell'interfaccia.
/// </para>
/// </remarks>
public interface IYouTubeService
{
    /// <summary>Stato corrente, senza segreti.</summary>
    YouTubeStatus GetStatus();

    /// <summary>Impostazioni modificabili dalla vista. Non contiene il client secret.</summary>
    YouTubeSettingsSnapshot GetSettings();

    /// <summary>Emesso a ogni cambio di stato: login, logout, salvataggio, esito di un caricamento o di un tick.</summary>
    event Action<YouTubeStatus>? StatusChanged;

    /// <summary>
    /// Collega l'account: apre il browser sull'autorizzazione Google e attende il ritorno sul loopback entro un tempo limite.
    /// Il secret viene cifrato subito e non viene più riletto. Annullabile.
    /// </summary>
    /// <exception cref="OperationCanceledException">Annullato dal chiamante.</exception>
    Task<YouTubeLoginResult> LoginAsync(string clientId, string clientSecret, CancellationToken cancellationToken);

    /// <summary>Scollega l'account: revoca il token presso Google, rimuove le credenziali locali e spegne il caricamento automatico.</summary>
    /// <exception cref="OperationCanceledException">Annullato dal chiamante.</exception>
    Task LogoutAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Valida e salva ora, tag, privacy e categoria. Con errori non cambia nulla. Il flag di attivazione non si modifica qui.
    /// </summary>
    YouTubeSaveResult SaveSettings(YouTubeSettingsChange change);

    /// <summary>
    /// Carica subito la notte più recente completata. Usa le stesse regole di idempotenza del caricamento automatico.
    /// </summary>
    /// <param name="acceptUncertainRisk">
    /// True solo dopo la conferma dell'utente a un esito incerto o fallito: ritenta la notte e può creare un duplicato.
    /// </param>
    /// <exception cref="OperationCanceledException">Annullato dal chiamante o dall'arresto dell'applicazione.</exception>
    Task<YouTubeUploadOutcome> UploadLastNightNowAsync(bool acceptUncertainRisk, CancellationToken cancellationToken);
}

/// <summary>Problemi che la vista può mostrare. Il testo si sceglie in <see cref="YouTubeStatusFormatter"/>.</summary>
public enum YouTubeProblem
{
    /// <summary>Accesso non valido o revocato: serve collegare di nuovo l'account.</summary>
    AuthRequired,

    /// <summary>Autorizzazione negata nel browser (access_denied).</summary>
    LoginDenied,

    /// <summary>Nessuna risposta dal browser entro il tempo limite.</summary>
    LoginTimedOut,

    /// <summary>Client ID o client secret mancanti.</summary>
    CredentialsMissing,

    /// <summary>Login non riuscito per altri motivi (rete, risposta di Google non valida, nessun refresh token).</summary>
    LoginFailed,

    /// <summary>La protezione dei segreti (DPAPI) non è disponibile: le credenziali non si possono salvare.</summary>
    SecretProtectionUnavailable,

    /// <summary>Quota giornaliera di YouTube esaurita: nuovo tentativo il giorno dopo.</summary>
    QuotaExceeded,

    /// <summary>Caricamento fallito in modo permanente per la notte indicata.</summary>
    UploadFailed,

    /// <summary>Esito incerto: prima di ricaricare conviene controllare su YouTube.</summary>
    NeedsReview,

    /// <summary>Errore temporaneo: nuovo tentativo programmato.</summary>
    RetryScheduled,

    /// <summary>Ora, privacy, tag o categoria non validi, oppure modelli non validi: nessuna chiamata di rete.</summary>
    SettingsInvalid,

    /// <summary>Le credenziali sono state gestite ma le impostazioni non si sono salvate nella configurazione.</summary>
    SettingsNotSaved,

    /// <summary>Il caricamento YouTube non è partito in questo avvio (per esempio lo store è illeggibile): vedere il log.</summary>
    Unavailable,
}

/// <summary>Esito dell'ultimo caricamento riuscito, per il link al video.</summary>
/// <param name="NightId">Notte caricata, "yyyy-MM-dd".</param>
/// <param name="VideoId">Id del video su YouTube.</param>
/// <param name="VideoUrl">Link https://www.youtube.com/watch?v=ID.</param>
/// <param name="UploadedUtc">Momento in cui il registro ha segnato il caricamento come completato.</param>
public sealed record YouTubeLastUpload(string NightId, string VideoId, string VideoUrl, DateTimeOffset UploadedUtc);

/// <summary>Stato del caricamento YouTube.</summary>
/// <param name="Connected">Account collegato (credenziali presenti e leggibili).</param>
/// <param name="AutomaticEnabled">Caricamento automatico attivo.</param>
/// <param name="ClientId">Client ID salvato, non segreto. Null se non collegato.</param>
/// <param name="UploadInProgress">Un caricamento è in corso in questo momento.</param>
/// <param name="NextUploadUtc">Prossimo tentativo automatico; null se il caricamento automatico è spento o l'account non è collegato.</param>
/// <param name="LastUpload">Ultimo caricamento completato, o null.</param>
/// <param name="Problem">Ultimo problema noto, o null.</param>
public sealed record YouTubeStatus(
    bool Connected,
    bool AutomaticEnabled,
    string? ClientId,
    bool UploadInProgress,
    DateTimeOffset? NextUploadUtc,
    YouTubeLastUpload? LastUpload,
    YouTubeProblem? Problem);

/// <summary>Impostazioni modificabili dalla vista.</summary>
/// <param name="DailyTime">Ora locale "HH:mm".</param>
/// <param name="Privacy">"private", "unlisted" o "public".</param>
/// <param name="Tags">Tag del video.</param>
/// <param name="CategoryId">ID numerico della categoria.</param>
public sealed record YouTubeSettingsSnapshot(string DailyTime, string Privacy, IReadOnlyList<string> Tags, string CategoryId);

/// <summary>Modifica proposta dalla vista. Stessi campi di <see cref="YouTubeSettingsSnapshot"/>.</summary>
public sealed record YouTubeSettingsChange(string DailyTime, string Privacy, IReadOnlyList<string> Tags, string CategoryId);

/// <summary>Campo non valido nel salvataggio delle impostazioni.</summary>
public enum YouTubeSettingsProblem
{
    DailyTimeInvalid,
    PrivacyInvalid,
    TagEmpty,
    CategoryIdInvalid,
}

/// <summary>Esito del salvataggio. Con <see cref="Ok"/> falso nulla è cambiato.</summary>
/// <param name="Ok">Impostazioni salvate.</param>
/// <param name="Problems">Campi non validi; vuoto se il salvataggio è fallito per altro motivo.</param>
public sealed record YouTubeSaveResult(bool Ok, IReadOnlyList<YouTubeSettingsProblem> Problems);

/// <summary>Esito del login.</summary>
/// <param name="Ok">Account collegato e salvato.</param>
/// <param name="Problem">Motivo se <see cref="Ok"/> è falso.</param>
public sealed record YouTubeLoginResult(bool Ok, YouTubeProblem? Problem);

/// <summary>Esito di un caricamento richiesto a mano. Stessi valori del pianificatore, senza dettagli tecnici.</summary>
public enum YouTubeUploadOutcomeKind
{
    Completed,
    AlreadyUploaded,
    InProgress,
    NeedsReview,
    QuotaBlocked,
    AuthRequired,
    Failed,
    ConfigError,
    NoNight,
    DailyLimitReached,
    NotDue,
    SlotMissed,
    /// <summary>Il caricamento non è disponibile in questo avvio.</summary>
    Unavailable,
}

/// <summary>Esito di <see cref="IYouTubeService.UploadLastNightNowAsync"/>.</summary>
/// <param name="Kind">Tipo di esito.</param>
/// <param name="VideoId">Id del video, solo se il caricamento è completato.</param>
public sealed record YouTubeUploadOutcome(YouTubeUploadOutcomeKind Kind, string? VideoId);
