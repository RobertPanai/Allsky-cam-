using System.Globalization;

namespace AllSky.Core.YouTube;

/// <summary>
/// Regole pure delle impostazioni YouTube: formato dell'ora, privacy, tag e categoria. Nessuna rete, nessun file.
/// La validazione dei modelli di titolo e descrizione sta nell'Host, perché richiede <c>AllSky.YouTube</c>.
/// </summary>
public static class YouTubeSettingsRules
{
    /// <summary>Formato dell'ora locale di caricamento.</summary>
    public const string DailyTimeFormat = "HH:mm";

    /// <summary>Valori ammessi per <c>youtube.privacy</c>, come nell'API (<c>status.privacyStatus</c>).</summary>
    public static readonly IReadOnlyList<string> PrivacyValues = ["private", "unlisted", "public"];

    /// <summary>Finestra di recupero massima, in ore.</summary>
    public const int CatchUpWindowMaxHours = 24;

    /// <summary>Legge un'ora "HH:mm" in formato invariante. Rifiuta spazi interni, secondi e ore fuori intervallo.</summary>
    public static bool TryParseDailyTime(string? text, out TimeOnly time)
    {
        time = default;
        return text is not null
            && TimeOnly.TryParseExact(text.Trim(), DailyTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    /// <summary>Ora nel formato "HH:mm".</summary>
    public static string FormatDailyTime(TimeOnly time) => time.ToString(DailyTimeFormat, CultureInfo.InvariantCulture);

    /// <summary>Vero se la privacy è uno dei tre valori dell'API.</summary>
    public static bool IsKnownPrivacy(string? value) => value is not null && PrivacyValues.Contains(value, StringComparer.Ordinal);

    /// <summary>L'ID categoria è un numero: almeno una cifra, solo cifre ASCII.</summary>
    public static bool IsValidCategoryId(string? value) =>
        !string.IsNullOrEmpty(value) && value.All(c => c is >= '0' and <= '9');

    /// <summary>Tag da un campo di testo separato da virgole. Spazi attorno a ogni tag tolti, voci vuote ignorate.</summary>
    public static IReadOnlyList<string> ParseTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
    }

    /// <summary>Tag come testo separato da virgole, per il campo della vista.</summary>
    public static string FormatTags(IEnumerable<string> tags) => string.Join(", ", tags);

    /// <summary>Problemi della modifica proposta. Lista vuota se è valida.</summary>
    public static IReadOnlyList<YouTubeSettingsProblem> Validate(YouTubeSettingsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var problems = new List<YouTubeSettingsProblem>();
        if (!TryParseDailyTime(change.DailyTime, out _))
        {
            problems.Add(YouTubeSettingsProblem.DailyTimeInvalid);
        }

        if (!IsKnownPrivacy(change.Privacy))
        {
            problems.Add(YouTubeSettingsProblem.PrivacyInvalid);
        }

        if (change.Tags.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add(YouTubeSettingsProblem.TagEmpty);
        }

        if (!IsValidCategoryId(change.CategoryId))
        {
            problems.Add(YouTubeSettingsProblem.CategoryIdInvalid);
        }

        return problems;
    }

    /// <summary>Impostazioni modificabili, lette dalla configurazione.</summary>
    public static YouTubeSettingsSnapshot ToSnapshot(AppConfig.YouTubeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new YouTubeSettingsSnapshot(config.DailyTime, config.Privacy, config.Tags, config.CategoryId);
    }

    /// <summary>
    /// Applica una modifica già validata. Ora normalizzata in "HH:mm", tag tolti dagli spazi. Non tocca
    /// <c>Enabled</c>, <c>ClientId</c>, luogo e modelli.
    /// </summary>
    public static AppConfig.YouTubeConfig Apply(AppConfig.YouTubeConfig current, YouTubeSettingsChange change)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(change);

        string dailyTime = TryParseDailyTime(change.DailyTime, out TimeOnly time)
            ? FormatDailyTime(time)
            : current.DailyTime;

        return current with
        {
            DailyTime = dailyTime,
            Privacy = change.Privacy,
            Tags = change.Tags.Select(t => t.Trim()).ToArray(),
            CategoryId = change.CategoryId,
        };
    }

    /// <summary>
    /// Validazione completa della sezione in configurazione, per l'avvio del caricamento. Percorsi nel formato
    /// <c>youtube.campo</c>. I testi sono in italiano: finiscono nel log, non nell'interfaccia.
    /// </summary>
    public static IReadOnlyList<ValidationError> ValidateConfig(AppConfig.YouTubeConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<ValidationError>();
        if (!TryParseDailyTime(config.DailyTime, out _))
        {
            errors.Add(new ValidationError("youtube.dailyTime", "L'ora di caricamento deve essere nel formato HH:mm."));
        }

        if (!IsKnownPrivacy(config.Privacy))
        {
            errors.Add(new ValidationError("youtube.privacy", "La privacy deve essere private, unlisted o public."));
        }

        if (config.Tags.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add(new ValidationError("youtube.tags", "Un tag e' vuoto."));
        }

        if (!IsValidCategoryId(config.CategoryId))
        {
            errors.Add(new ValidationError("youtube.categoryId", "L'ID categoria deve contenere solo cifre."));
        }

        if (config.CatchUpWindowHours < 0 || config.CatchUpWindowHours > CatchUpWindowMaxHours)
        {
            errors.Add(new ValidationError("youtube.catchUpWindowHours", "La finestra di recupero deve essere tra 0 e 24 ore."));
        }

        if (string.IsNullOrWhiteSpace(config.Location))
        {
            errors.Add(new ValidationError("youtube.location", "Il luogo di ripresa e' vuoto (serve per il modello del titolo)."));
        }

        return errors;
    }
}
