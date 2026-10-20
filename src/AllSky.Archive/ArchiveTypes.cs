namespace AllSky.Archive;

/// <summary>
/// Stato della generazione del timelapse di una notte.
/// <see cref="Ready"/> e' derivato dalla presenza di timelapse.mp4; <see cref="Generating"/> e
/// <see cref="Failed"/> sono tracciati nel file marker timelapse.state.
/// </summary>
public enum TimelapseState
{
    None,
    Ready,
    Generating,
    Failed,
}

/// <summary>Prodotti di una notte, ciascuno con nome file fisso nella cartella della notte.</summary>
public enum NightProduct
{
    /// <summary>keogram.jpg</summary>
    Keogram,

    /// <summary>startrails.jpg</summary>
    Startrails,

    /// <summary>timelapse.mp4</summary>
    Timelapse,
}

/// <summary>
/// Metadati di un frame forniti dal chiamante. Id e timestamp derivano da <c>ts</c> in
/// <see cref="NightArchive.SaveFrame"/>; la fase e' il parametro separato <c>phase</c>.
/// </summary>
public sealed record FrameMeta(double ExposureMs, double Gain, int MedianAdu);

/// <summary>
/// Una riga di meta.jsonl. <see cref="Id"/> e' il nome del frame (<c>yyyyMMddTHHmmssZ</c>, UTC);
/// <see cref="Timestamp"/> e' in UTC; <see cref="Phase"/> e' "day", "twilight" o "night".
/// </summary>
public sealed record FrameEntry(
    string Id,
    DateTimeOffset Timestamp,
    string Phase,
    double ExposureMs,
    double Gain,
    int MedianAdu);

/// <summary>
/// Riepilogo di una notte. <see cref="FirstFrame"/> e <see cref="LastFrame"/> sono i timestamp UTC
/// minimo e massimo dei frame (null se la notte non ha frame).
/// </summary>
public sealed record NightInfo(
    string Id,
    int Frames,
    long SizeBytes,
    DateTimeOffset? FirstFrame,
    DateTimeOffset? LastFrame,
    bool HasKeogram,
    bool HasStartrails,
    TimelapseState TimelapseState);

/// <summary>
/// Uso del disco. <see cref="UsedBytes"/> e' la dimensione dell'archivio (cartella nights);
/// <see cref="FreeBytes"/> e' lo spazio libero sul volume che ospita la root, null se non leggibile.
/// </summary>
public sealed record DiskUsage(long UsedBytes, long? FreeBytes);
