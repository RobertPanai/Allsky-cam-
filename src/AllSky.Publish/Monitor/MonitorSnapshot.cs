namespace AllSky.Publish.Monitor;

// Payload del monitoraggio pubblico (P12.1). Tipi primitivi: nessun riferimento ad AllSky.Nina.
// Elenco CHIUSO dei campi: aggiungere una proprietà pubblica fa fallire il test di contratto
// (MonitorSnapshotContractTests). Nessuna posizione dell'osservatorio, nessun percorso, nessun segreto.

/// <summary>Tipo di asset pubblicabile. Ogni valore corrisponde a una voce di <c>monitor.voices</c>.</summary>
public enum MonitorAssetKind
{
    AllSkyImage,
    LatestTimelapse,
    Keogram,
    StartTrails,
}

/// <summary>Un punto della serie di altezza del soggetto.</summary>
public sealed record MonitorAltitudePoint(DateTimeOffset TimeUtc, double AltitudeDeg);

/// <summary>Un asset pubblicabile. <see cref="FileName"/> è solo il nome del file, senza cartelle.</summary>
public sealed record MonitorAsset(MonitorAssetKind Kind, string FileName);

/// <summary>
/// Istantanea pubblicabile del monitoraggio. Immutabile. Un campo è valorizzato solo se la sua voce è attiva
/// (vedi <see cref="MonitorSnapshotBuilder"/>); gli altri restano null o lista vuota.
/// </summary>
public sealed record MonitorSnapshot(
    int SchemaVersion,
    DateTimeOffset UpdatedUtc,
    string? NightPhase,
    double? SunAltitudeDeg,
    double? MoonAltitudeDeg,
    string? TargetName,
    string? ParkState,
    double? MoonSeparationDeg,
    IReadOnlyList<MonitorAltitudePoint>? TargetAltitudeChart,
    IReadOnlyList<MonitorAsset> Assets)
{
    /// <summary>Versione dello schema del payload.</summary>
    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// Valori grezzi disponibili al momento della costruzione. Il builder applica le voci e riduce i percorsi
/// al solo nome del file. Tutti i campi sono facoltativi: null significa «non disponibile».
/// </summary>
public sealed record MonitorInputs
{
    public string? NightPhase { get; init; }

    public double? SunAltitudeDeg { get; init; }

    public double? MoonAltitudeDeg { get; init; }

    public string? TargetName { get; init; }

    public string? ParkState { get; init; }

    public double? MoonSeparationDeg { get; init; }

    public IReadOnlyList<MonitorAltitudePoint>? TargetAltitudeChart { get; init; }

    /// <summary>Percorso completo dell'ultima immagine all-sky. Il builder ne tiene solo il nome file.</summary>
    public string? AllSkyImagePath { get; init; }

    /// <summary>Percorso completo dell'ultimo timelapse.</summary>
    public string? LatestTimelapsePath { get; init; }

    /// <summary>Percorso completo del keogram.</summary>
    public string? KeogramPath { get; init; }

    /// <summary>Percorso completo dello start trails.</summary>
    public string? StartTrailsPath { get; init; }
}
