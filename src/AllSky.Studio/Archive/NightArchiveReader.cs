using AllSky.Archive;

namespace AllSky.Studio;

/// <summary>Implementazione di <see cref="IArchiveReader"/> sull'archivio reale <see cref="NightArchive"/>.</summary>
public sealed class NightArchiveReader : IArchiveReader
{
    private readonly NightArchive _archive;

    /// <summary>Crea il lettore sopra un archivio già aperto.</summary>
    /// <param name="archive">Archivio delle notti.</param>
    public NightArchiveReader(NightArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        _archive = archive;
    }

    /// <inheritdoc />
    public IReadOnlyList<ArchiveFrame> ListFrames(string nightId, int offset, int limit)
    {
        return _archive.ListFrames(nightId, offset, limit)
            .Select(entry => new ArchiveFrame(entry.Id, entry.Timestamp))
            .ToList();
    }

    /// <inheritdoc />
    public string GetFramePath(string nightId, string frameId)
    {
        return _archive.GetFramePath(nightId, frameId);
    }
}
