using System.Text.Json.Serialization;
using AllSky.YouTube;

namespace AllSky.Studio;

/// <summary>
/// Destinazione di un'esportazione. È polimorfica: nel JSON il campo <c>type</c> dice quale delle tre è
/// (<c>localFile</c>, <c>youtube</c>, <c>telegram</c>). Nessun campo contiene segreti: token e client secret
/// restano nei loro archivi cifrati e arrivano ai servizi tramite le interfacce iniettate.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LocalFileDestination), "localFile")]
[JsonDerivedType(typeof(YouTubeDestination), "youtube")]
[JsonDerivedType(typeof(TelegramDestination), "telegram")]
public abstract record ExportDestination;

/// <summary>Cosa fare se nella cartella esiste già un file con il nome scelto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverwritePolicy
{
    /// <summary>Non sovrascrive: il lavoro fallisce con un messaggio chiaro.</summary>
    Fail,

    /// <summary>Aggiunge un numero tra parentesi, es. «notte (2).mp4».</summary>
    Rename,

    /// <summary>Sostituisce il file esistente.</summary>
    Overwrite,
}

/// <summary>
/// File locale in una cartella.
/// </summary>
/// <param name="Folder">Cartella di destinazione. Non viene controllata l'esistenza in fase di validazione: è una cosa del PC.</param>
/// <remarks>
/// <see cref="FileNameTemplate"/> accetta i segnaposto <c>{project}</c>, <c>{date}</c> (AAAA-MM-GG, data locale)
/// e <c>{n}</c> (numero del lavoro nell'elenco delle esportazioni del progetto, da 1). L'estensione <c>.mp4</c>
/// si aggiunge da sola se manca.
/// </remarks>
public sealed record LocalFileDestination : ExportDestination
{
    /// <summary>Cartella di destinazione.</summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>Modello del nome file, senza percorso.</summary>
    public string FileNameTemplate { get; init; } = "{project} {date}";

    /// <summary>Politica se il file esiste già.</summary>
    public OverwritePolicy Overwrite { get; init; } = OverwritePolicy.Fail;
}

/// <summary>
/// Caricamento su YouTube. Titolo e descrizione usano il motore di modelli di <c>AllSky.YouTube</c>.
/// </summary>
/// <remarks>
/// Il valore di <see cref="Privacy"/> è una stringa (<c>private</c>, <c>unlisted</c>, <c>public</c>) così che un
/// valore sconosciuto dia un errore in italiano invece di rompere la lettura del progetto.
/// </remarks>
public sealed record YouTubeDestination : ExportDestination
{
    /// <summary>Modello del titolo (JSON "titleTemplate"). Stessi segnaposto della coda di caricamento notturno.</summary>
    [JsonPropertyName("titleTemplate")]
    public string Title { get; init; } = YouTubeUploadSettings.Defaults.TitleTemplate;

    /// <summary>Modello della descrizione (JSON "descriptionTemplate"). Può essere vuota.</summary>
    [JsonPropertyName("descriptionTemplate")]
    public string Description { get; init; } = YouTubeUploadSettings.Defaults.DescriptionTemplate;

    /// <summary>Privacy: "private", "unlisted" o "public".</summary>
    public string Privacy { get; init; } = "private";
}

/// <summary>
/// Invio a una chat Telegram autorizzata. L'invio passa da <c>TelegramSender</c> dietro <c>ITelegramPublisher</c>.
/// </summary>
/// <param name="ChatId">Identificativo della chat come testo intero (il valore può essere negativo). Il testo evita problemi di precisione.</param>
/// <param name="Caption">Didascalia opzionale, al massimo quanto consentito da Telegram.</param>
public sealed record TelegramDestination : ExportDestination
{
    /// <summary>Identificativo della chat, come intero in forma di testo.</summary>
    public string ChatId { get; init; } = string.Empty;

    /// <summary>Didascalia del video. Null o vuota = nessuna didascalia.</summary>
    public string? Caption { get; init; }
}
