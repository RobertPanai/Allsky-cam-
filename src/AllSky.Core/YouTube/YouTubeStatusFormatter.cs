using System.Globalization;

namespace AllSky.Core.YouTube;

/// <summary>
/// Testi della vista YouTube: stato, problemi, esiti dei caricamenti ed errori di campo. Logica pura: l'istante si passa
/// dal chiamante e i testi arrivano da <see cref="DesktopTexts"/>. Nessun testo tecnico dell'Host arriva all'interfaccia.
/// </summary>
public static class YouTubeStatusFormatter
{
    private const string DateTimeFormat = "dd/MM/yyyy HH:mm";

    /// <summary>Riga di collegamento dell'account.</summary>
    public static string ConnectionText(bool connected, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return connected ? texts.YouTubeConnected : texts.YouTubeNotConnected;
    }

    /// <summary>Riga del caricamento automatico.</summary>
    public static string AutomaticText(bool enabled, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return enabled ? texts.YouTubeAutomaticOn : texts.YouTubeAutomaticOff;
    }

    /// <summary>Prossimo caricamento in ora locale del PC; se non è programmato, la riga dedicata.</summary>
    public static string NextUploadText(DateTimeOffset? nextUtc, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return nextUtc is null
            ? texts.YouTubeNoNextUpload
            : string.Format(CultureInfo.InvariantCulture, texts.YouTubeNextUpload, LocalText(nextUtc.Value));
    }

    /// <summary>Ultimo caricamento riuscito: notte e data di registrazione. Senza caricamenti, la riga dedicata.</summary>
    public static string LastUploadText(YouTubeLastUpload? last, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return last is null
            ? texts.YouTubeNoUploadYet
            : string.Format(CultureInfo.InvariantCulture, texts.YouTubeLastUpload, last.NightId, LocalText(last.UploadedUtc));
    }

    /// <summary>Testo di un problema. Null se non c'è problema.</summary>
    public static string? ProblemText(YouTubeProblem? problem, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return problem switch
        {
            null => null,
            YouTubeProblem.AuthRequired => texts.YouTubeProblemAuthRequired,
            YouTubeProblem.LoginDenied => texts.YouTubeProblemLoginDenied,
            YouTubeProblem.LoginTimedOut => texts.YouTubeProblemLoginTimedOut,
            YouTubeProblem.CredentialsMissing => texts.YouTubeProblemCredentialsMissing,
            YouTubeProblem.LoginFailed => texts.YouTubeProblemLoginFailed,
            YouTubeProblem.SecretProtectionUnavailable => texts.YouTubeProblemSecretUnavailable,
            YouTubeProblem.QuotaExceeded => texts.YouTubeProblemQuota,
            YouTubeProblem.UploadFailed => texts.YouTubeProblemUploadFailed,
            YouTubeProblem.NeedsReview => texts.YouTubeProblemNeedsReview,
            YouTubeProblem.RetryScheduled => texts.YouTubeProblemRetry,
            YouTubeProblem.SettingsInvalid => texts.YouTubeProblemSettingsInvalid,
            YouTubeProblem.SettingsNotSaved => texts.YouTubeProblemSettingsNotSaved,
            YouTubeProblem.Unavailable => texts.YouTubeProblemUnavailable,
            _ => texts.YouTubeProblemUnavailable,
        };
    }

    /// <summary>Riga "Ultimo problema: ..." per la vista. Null se non c'è problema.</summary>
    public static string? LastProblemLine(YouTubeProblem? problem, DesktopTexts texts)
    {
        string? text = ProblemText(problem, texts);
        return text is null ? null : string.Format(CultureInfo.InvariantCulture, texts.YouTubeLastProblem, text);
    }

    /// <summary>Testo dell'esito di un caricamento richiesto a mano.</summary>
    public static string OutcomeText(YouTubeUploadOutcomeKind kind, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return kind switch
        {
            YouTubeUploadOutcomeKind.Completed => texts.YouTubeOutcomeCompleted,
            YouTubeUploadOutcomeKind.AlreadyUploaded => texts.YouTubeOutcomeAlreadyUploaded,
            YouTubeUploadOutcomeKind.InProgress => texts.YouTubeOutcomeInProgress,
            YouTubeUploadOutcomeKind.NoNight => texts.YouTubeOutcomeNoNight,
            YouTubeUploadOutcomeKind.DailyLimitReached => texts.YouTubeOutcomeDailyLimit,
            YouTubeUploadOutcomeKind.NotDue => texts.YouTubeOutcomeNotDue,
            YouTubeUploadOutcomeKind.SlotMissed => texts.YouTubeOutcomeSlotMissed,
            YouTubeUploadOutcomeKind.NeedsReview => texts.YouTubeProblemNeedsReview,
            YouTubeUploadOutcomeKind.QuotaBlocked => texts.YouTubeProblemQuota,
            YouTubeUploadOutcomeKind.AuthRequired => texts.YouTubeProblemAuthRequired,
            YouTubeUploadOutcomeKind.Failed => texts.YouTubeProblemUploadFailed,
            YouTubeUploadOutcomeKind.ConfigError => texts.YouTubeProblemSettingsInvalid,
            _ => texts.YouTubeProblemUnavailable,
        };
    }

    /// <summary>Vero se l'esito è un errore da segnalare in rosso (il resto è neutro o positivo).</summary>
    public static bool IsErrorOutcome(YouTubeUploadOutcomeKind kind) => kind is
        YouTubeUploadOutcomeKind.NeedsReview
        or YouTubeUploadOutcomeKind.QuotaBlocked
        or YouTubeUploadOutcomeKind.AuthRequired
        or YouTubeUploadOutcomeKind.Failed
        or YouTubeUploadOutcomeKind.ConfigError;

    /// <summary>Vero se, dopo questo esito, la conferma dell'utente può sbloccare il caricamento della notte.</summary>
    public static bool CanForceRetry(YouTubeUploadOutcomeKind kind) => kind is
        YouTubeUploadOutcomeKind.NeedsReview
        or YouTubeUploadOutcomeKind.Failed;

    /// <summary>Testo di un campo non valido.</summary>
    public static string SettingsProblemText(YouTubeSettingsProblem problem, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return problem switch
        {
            YouTubeSettingsProblem.DailyTimeInvalid => texts.YouTubeProblemDailyTime,
            YouTubeSettingsProblem.PrivacyInvalid => texts.YouTubeProblemPrivacy,
            YouTubeSettingsProblem.TagEmpty => texts.YouTubeProblemTag,
            YouTubeSettingsProblem.CategoryIdInvalid => texts.YouTubeProblemCategory,
            _ => texts.YouTubeSaveFailed,
        };
    }

    /// <summary>Nome della privacy nella lingua scelta. Valori sconosciuti restituiscono il valore grezzo.</summary>
    public static string PrivacyName(string privacy, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return privacy switch
        {
            "private" => texts.YouTubePrivacyPrivate,
            "unlisted" => texts.YouTubePrivacyUnlisted,
            "public" => texts.YouTubePrivacyPublic,
            _ => privacy,
        };
    }

    private static string LocalText(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString(DateTimeFormat, CultureInfo.InvariantCulture);
}
