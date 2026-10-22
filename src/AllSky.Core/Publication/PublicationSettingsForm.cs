using System.Globalization;

namespace AllSky.Core.Publication;

/// <summary>Problema di un campo numerico del modulo Uplink.</summary>
public enum PublicationNumberProblem
{
    /// <summary>Nessun problema: il valore è valido.</summary>
    None,

    /// <summary>Campo vuoto.</summary>
    Empty,

    /// <summary>Il testo non è un numero intero.</summary>
    NotInteger,

    /// <summary>Numero intero fuori dall'intervallo ammesso.</summary>
    OutOfRange,
}

/// <summary>Esito della lettura di un campo numerico.</summary>
/// <param name="Value">Valore letto; null se il campo non è valido.</param>
/// <param name="Problem">Problema rilevato.</param>
public sealed record PublicationNumberCheck(int? Value, PublicationNumberProblem Problem);

/// <summary>Esito della validazione dei campi numerici del modulo.</summary>
public sealed record PublicationFormCheck(
    PublicationNumberCheck IntervalSeconds,
    PublicationNumberCheck LongEdgePx,
    PublicationNumberCheck JpegQuality)
{
    /// <summary>Vero se tutti i campi numerici sono validi.</summary>
    public bool IsValid =>
        IntervalSeconds.Problem == PublicationNumberProblem.None
        && LongEdgePx.Problem == PublicationNumberProblem.None
        && JpegQuality.Problem == PublicationNumberProblem.None;
}

/// <summary>
/// Logica del modulo "Impostazioni Uplink": lettura dei campi numerici, rilevamento delle modifiche e pulizia del testo.
/// I limiti sono quelli di <c>SettingsValidator</c> (AllSky.Publish). Il server resta l'autorità: questa validazione serve
/// a segnalare subito gli errori di digitazione. Logica pura, la cultura è passata dal chiamante.
/// </summary>
public static class PublicationSettingsForm
{
    /// <summary>Intervallo tra gli invii, in secondi: minimo.</summary>
    public const int IntervalMin = 15;

    /// <summary>Intervallo tra gli invii, in secondi: massimo.</summary>
    public const int IntervalMax = 86400;

    /// <summary>Intervallo tra gli invii, in secondi: predefinito.</summary>
    public const int IntervalDefault = 60;

    /// <summary>Lato lungo dell'immagine inviata, in pixel: minimo.</summary>
    public const int LongEdgeMin = 200;

    /// <summary>Lato lungo dell'immagine inviata, in pixel: massimo.</summary>
    public const int LongEdgeMax = 2000;

    /// <summary>Lato lungo dell'immagine inviata, in pixel: predefinito.</summary>
    public const int LongEdgeDefault = 1200;

    /// <summary>Qualità JPEG: minimo.</summary>
    public const int JpegQualityMin = 30;

    /// <summary>Qualità JPEG: massimo.</summary>
    public const int JpegQualityMax = 95;

    /// <summary>Qualità JPEG: predefinito.</summary>
    public const int JpegQualityDefault = 75;

    /// <summary>Valida i tre campi numerici come testo digitato.</summary>
    /// <param name="intervalText">Testo dell'intervallo, in secondi.</param>
    /// <param name="longEdgeText">Testo del lato lungo, in pixel.</param>
    /// <param name="jpegQualityText">Testo della qualità JPEG.</param>
    /// <param name="culture">Cultura dell'interfaccia (per il segno, gli spazi e le cifre).</param>
    public static PublicationFormCheck Check(string? intervalText, string? longEdgeText, string? jpegQualityText, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new PublicationFormCheck(
            CheckInteger(intervalText, IntervalMin, IntervalMax, culture),
            CheckInteger(longEdgeText, LongEdgeMin, LongEdgeMax, culture),
            CheckInteger(jpegQualityText, JpegQualityMin, JpegQualityMax, culture));
    }

    /// <summary>Legge un intero dentro un intervallo. Accetta anche il punto come separatore, come le altre viste.</summary>
    /// <param name="text">Testo digitato.</param>
    /// <param name="min">Minimo ammesso, incluso.</param>
    /// <param name="max">Massimo ammesso, incluso.</param>
    /// <param name="culture">Cultura dell'interfaccia.</param>
    public static PublicationNumberCheck CheckInteger(string? text, int min, int max, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new PublicationNumberCheck(null, PublicationNumberProblem.Empty);
        }

        bool parsed = int.TryParse(trimmed, NumberStyles.Integer, culture, out int value)
            || int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        if (!parsed)
        {
            return new PublicationNumberCheck(null, PublicationNumberProblem.NotInteger);
        }

        if (value < min || value > max)
        {
            return new PublicationNumberCheck(null, PublicationNumberProblem.OutOfRange);
        }

        return new PublicationNumberCheck(value, PublicationNumberProblem.None);
    }

    /// <summary>
    /// Vero se la modifica cambia qualcosa rispetto alle impostazioni correnti. Un segreto digitato conta come modifica;
    /// <see cref="PublicationSettingsChange.ClearToken"/> conta solo se un token è davvero salvato.
    /// </summary>
    /// <param name="change">Valori del modulo.</param>
    /// <param name="current">Impostazioni salvate.</param>
    public static bool HasChanges(PublicationSettingsChange change, PublicationSettingsSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(current);

        return change.Enabled != current.Enabled
            || Trim(change.ServerUrl) != Trim(current.ServerUrl)
            || Trim(change.DeviceId) != Trim(current.DeviceId)
            || TrimOrNull(change.ProfileName) != TrimOrNull(current.ProfileName)
            || change.IntervalSeconds != current.IntervalSeconds
            || change.LongEdgePx != current.LongEdgePx
            || change.JpegQuality != current.JpegQuality
            || TrimOrNull(change.ExtraHeaderName) != TrimOrNull(current.ExtraHeaderName)
            || !string.IsNullOrWhiteSpace(change.NewToken)
            || (change.ClearToken && current.HasToken)
            || !string.IsNullOrWhiteSpace(change.NewExtraHeaderValue)
            || (change.ClearExtraHeaderValue && current.HasExtraHeaderValue);
    }

    /// <summary>Testo senza spazi ai bordi; vuoto se null.</summary>
    /// <param name="text">Testo da pulire.</param>
    public static string Trim(string? text) => text?.Trim() ?? string.Empty;

    /// <summary>Testo senza spazi ai bordi, oppure null se vuoto: come il server, un campo vuoto è assente.</summary>
    /// <param name="text">Testo da pulire.</param>
    public static string? TrimOrNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
