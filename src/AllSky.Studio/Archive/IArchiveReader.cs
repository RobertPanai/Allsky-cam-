namespace AllSky.Studio;

/// <summary>Un frame dell'archivio: identificatore (nome senza estensione) e istante UTC.</summary>
/// <param name="FrameId">Identificatore del frame (formato yyyyMMddTHHmmssZ).</param>
/// <param name="TimestampUtc">Istante del frame, in UTC.</param>
public sealed record ArchiveFrame(string FrameId, DateTimeOffset TimestampUtc);

/// <summary>
/// Accesso minimo all'archivio delle notti. Espone solo ciò che lo Studio usa, così i test non
/// hanno bisogno di un archivio reale. L'implementazione sull'archivio vero è <see cref="NightArchiveReader"/>.
/// </summary>
public interface IArchiveReader
{
    /// <summary>Frame di una notte, in ordine, a pagine.</summary>
    /// <param name="nightId">Identificatore della notte (AAAA-MM-GG).</param>
    /// <param name="offset">Frame da saltare, maggiore o uguale a zero.</param>
    /// <param name="limit">Numero massimo di frame, maggiore di zero.</param>
    IReadOnlyList<ArchiveFrame> ListFrames(string nightId, int offset, int limit);

    /// <summary>Percorso del JPEG di un frame. Il file può non esistere.</summary>
    string GetFramePath(string nightId, string frameId);
}
