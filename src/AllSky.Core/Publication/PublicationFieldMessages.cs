using System.Globalization;

namespace AllSky.Core.Publication;

/// <summary>Campi del modulo Uplink, per collegare un errore del server al campo giusto.</summary>
public enum PublicationField
{
    /// <summary>Errore che non riguarda un campo preciso (o il modulo non è partito).</summary>
    General,

    /// <summary>Indirizzo del server.</summary>
    ServerUrl,

    /// <summary>Identificativo del dispositivo.</summary>
    DeviceId,

    /// <summary>Token.</summary>
    Token,

    /// <summary>Nome del profilo.</summary>
    ProfileName,

    /// <summary>Intervallo tra gli invii.</summary>
    IntervalSeconds,

    /// <summary>Lato lungo dell'immagine.</summary>
    LongEdgePx,

    /// <summary>Qualità JPEG.</summary>
    JpegQuality,

    /// <summary>Nome dell'header HTTP aggiuntivo.</summary>
    ExtraHeaderName,

    /// <summary>Valore dell'header HTTP aggiuntivo.</summary>
    ExtraHeaderValue,
}

/// <summary>Messaggi dei campi del modulo Uplink, nella lingua scelta.</summary>
public static class PublicationFieldMessages
{
    /// <summary>Percorso del campo che il server usa per "modulo non partito".</summary>
    public const string UnavailablePath = "publish";

    /// <summary>Campo del modulo corrispondente al percorso camelCase restituito dal validatore.</summary>
    /// <param name="path">Percorso, per esempio "serverUrl". Null o sconosciuto dà <see cref="PublicationField.General"/>.</param>
    public static PublicationField FieldOf(string? path) => path switch
    {
        "serverUrl" => PublicationField.ServerUrl,
        "deviceId" => PublicationField.DeviceId,
        "token" => PublicationField.Token,
        "profileName" => PublicationField.ProfileName,
        "intervalSeconds" => PublicationField.IntervalSeconds,
        "longEdgePx" => PublicationField.LongEdgePx,
        "jpegQuality" => PublicationField.JpegQuality,
        "extraHeaderName" => PublicationField.ExtraHeaderName,
        "extraHeaderValue" => PublicationField.ExtraHeaderValue,
        _ => PublicationField.General,
    };

    /// <summary>Nome del campo, come appare nell'etichetta.</summary>
    /// <param name="field">Campo.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string NameOf(PublicationField field, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return field switch
        {
            PublicationField.ServerUrl => texts.PublicationServerUrl,
            PublicationField.DeviceId => texts.PublicationDeviceId,
            PublicationField.Token => texts.PublicationTokenLabel,
            PublicationField.ProfileName => texts.PublicationProfileName,
            PublicationField.IntervalSeconds => texts.PublicationInterval,
            PublicationField.LongEdgePx => texts.PublicationLongEdge,
            PublicationField.JpegQuality => texts.PublicationJpegQuality,
            PublicationField.ExtraHeaderName => texts.PublicationExtraHeaderName,
            PublicationField.ExtraHeaderValue => texts.PublicationExtraHeaderValue,
            _ => texts.PublicationFieldGeneral,
        };
    }

    /// <summary>
    /// Riga di un errore di salvataggio. Per il modulo non partito mostra il testo localizzato, non quello del server.
    /// Il messaggio del server (validazione) è in italiano in entrambe le lingue: è un limite noto.
    /// </summary>
    /// <param name="issue">Errore restituito dal salvataggio.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string IssueLine(PublicationIssue issue, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(texts);

        if (string.Equals(issue.Path, UnavailablePath, StringComparison.Ordinal))
        {
            return texts.PublicationUnavailable;
        }

        string name = NameOf(FieldOf(issue.Path), texts);
        return string.Format(CultureInfo.InvariantCulture, texts.PublicationIssueLine, name, issue.Message);
    }

    /// <summary>
    /// Messaggio sotto un campo numerico; null se il campo è valido.
    /// </summary>
    /// <param name="problem">Problema rilevato da <see cref="PublicationSettingsForm"/>.</param>
    /// <param name="min">Minimo ammesso.</param>
    /// <param name="max">Massimo ammesso.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public static string? ForNumber(PublicationNumberProblem problem, int min, int max, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return problem switch
        {
            PublicationNumberProblem.None => null,
            PublicationNumberProblem.Empty => texts.PublicationProblemEmpty,
            PublicationNumberProblem.NotInteger => texts.PublicationProblemNotInteger,
            PublicationNumberProblem.OutOfRange => string.Format(CultureInfo.InvariantCulture, texts.PublicationProblemOutOfRange, min, max),
            _ => texts.PublicationProblemNotInteger,
        };
    }
}
