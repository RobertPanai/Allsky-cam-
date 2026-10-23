using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllSky.Studio;

/// <summary>
/// Progetto di montaggio salvato come <c>.asbproject</c> (JSON camelCase, versione 1).
/// Ogni oggetto conserva in <c>UnknownFields</c> i campi che questa versione non conosce, così
/// un salvataggio non li perde.
/// </summary>
public sealed record StudioProject
{
    /// <summary>Versione del formato. Oggi solo 1.</summary>
    public int Version { get; init; } = StudioProjectStore.CurrentVersion;

    /// <summary>Nome del progetto.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Fuso orario scelto dall'utente (IANA o Windows, es. "Europe/Rome"). Null = UTC: le date EXIF e
    /// quelle del nome file vengono lette come UTC e la scansione lo segnala con un avviso.
    /// </summary>
    public string? LocalTimeZoneId { get; init; }

    /// <summary>Sorgenti del progetto, nell'ordine dato dall'utente.</summary>
    public IReadOnlyList<StudioSource> Sources { get; init; } = [];

    /// <summary>Operazioni di montaggio. In P13.1 sono solo contenitori neutri: la logica arriva in P13.2.</summary>
    public IReadOnlyList<StudioOperation> Operations { get; init; } = [];

    /// <summary>
    /// Esportazioni richieste. Nel JSON stanno nel campo <c>exports</c>, che viene scritto solo se l'elenco non è
    /// vuoto: un progetto senza esportazioni resta identico a prima di P13.4.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<ExportTarget> Exports { get; init; } = [];

    /// <summary>Forma JSON di <see cref="Exports"/>: null (campo omesso) quando l'elenco è vuoto.</summary>
    [JsonPropertyName("exports")]
    public IReadOnlyList<ExportTarget>? ExportsJson
    {
        get => Exports.Count == 0 ? null : Exports;
        init => Exports = value ?? [];
    }

    /// <summary>Campi JSON sconosciuti al livello del progetto, riscritti tali e quali.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> UnknownFields { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Tipi di sorgente riconosciuti nel campo <c>kind</c>.</summary>
public static class StudioSourceKinds
{
    /// <summary>Una notte dell'archivio di All Sky Brain (campo <c>nightId</c>).</summary>
    public const string ArchiveNight = "archiveNight";

    /// <summary>Una cartella di JPEG o PNG (campi <c>path</c>, <c>timestampRule</c>, <c>extensions</c>).</summary>
    public const string Folder = "folder";

    /// <summary>Un video da cui estrarre frame (campi <c>path</c>, <c>intervalSeconds</c>).</summary>
    public const string Video = "video";
}

/// <summary>Modalità con cui si ricava l'istante di un frame di una cartella.</summary>
public static class TimestampRuleModes
{
    /// <summary>DateTimeOriginal (o DateTime) dell'EXIF dei JPEG.</summary>
    public const string Exif = "exif";

    /// <summary>Data e ora scritte nel nome file tramite un modello con segnaposto.</summary>
    public const string FileNamePattern = "fileNamePattern";

    /// <summary>Data di ultima modifica del file, in UTC.</summary>
    public const string FileModifiedTime = "fileModifiedTime";
}

/// <summary>Una sorgente del progetto. Quali campi contano dipende da <see cref="Kind"/>.</summary>
public sealed record StudioSource
{
    /// <summary>Tipo della sorgente, uno dei valori di <see cref="StudioSourceKinds"/>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Identificatore della notte ("AAAA-MM-GG"), solo per <see cref="StudioSourceKinds.ArchiveNight"/>.</summary>
    public string? NightId { get; init; }

    /// <summary>Cartella o file video, a seconda di <see cref="Kind"/>. Nel JSON è il campo "path".</summary>
    [JsonPropertyName("path")]
    public string? SourcePath { get; init; }

    /// <summary>Regola per l'istante dei frame, solo per <see cref="StudioSourceKinds.Folder"/>.</summary>
    public TimestampRule? TimestampRule { get; init; }

    /// <summary>Estensioni ammesse (es. ".jpg"). Null = .jpg, .jpeg, .png.</summary>
    public IReadOnlyList<string>? Extensions { get; init; }

    /// <summary>Intervallo di estrazione in secondi, solo per <see cref="StudioSourceKinds.Video"/>.</summary>
    public double? IntervalSeconds { get; init; }

    /// <summary>Campi JSON sconosciuti, riscritti tali e quali.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> UnknownFields { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Regola per ricavare l'istante di un frame da una cartella.</summary>
public sealed record TimestampRule
{
    /// <summary>Una delle modalità di <see cref="TimestampRuleModes"/>.</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>
    /// Modello del nome file con segnaposto <c>{yyyy}</c> <c>{MM}</c> <c>{dd}</c> <c>{HH}</c> <c>{mm}</c> <c>{ss}</c>
    /// e jolly <c>*</c>. Usato solo con <see cref="TimestampRuleModes.FileNamePattern"/>.
    /// </summary>
    public string? FileNamePattern { get; init; }

    /// <summary>Campi JSON sconosciuti, riscritti tali e quali.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> UnknownFields { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Operazione di montaggio. È un contenitore neutro: <see cref="Type"/> e <see cref="Parameters"/>
/// vengono salvati così come sono, senza interpretazione, fino a P13.2.
/// </summary>
public sealed record StudioOperation
{
    /// <summary>Nome dell'operazione (es. "interval", "crop").</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Parametri dell'operazione, conservati come JSON grezzo.</summary>
    public Dictionary<string, JsonElement> Parameters { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Campi JSON sconosciuti, riscritti tali e quali.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement> UnknownFields { get; init; } = new(StringComparer.Ordinal);
}
