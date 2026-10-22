namespace AllSky.Studio;

/// <summary>Esito dell'elenco dei frame di una notte dell'archivio.</summary>
/// <param name="NightId">Identificatore della notte.</param>
/// <param name="Frames">Frame con file JPEG presente, in ordine di istante.</param>
/// <param name="Warnings">Avvisi (notte vuota, frame indicizzati senza file), in italiano.</param>
public sealed record ArchiveNightResult(string NightId, IReadOnlyList<StudioFrame> Frames, IReadOnlyList<string> Warnings);

/// <summary>
/// Sorgente "notte dell'archivio": elenca i frame di una notte leggendo l'archivio tramite
/// <see cref="IArchiveReader"/>, a pagine. I frame senza file JPEG vengono contati in un avviso.
/// </summary>
public sealed class ArchiveNightSource
{
    /// <summary>Dimensione della pagina letta dall'archivio.</summary>
    public const int PageSize = 500;

    private readonly IArchiveReader _reader;

    /// <summary>Crea la sorgente.</summary>
    /// <param name="reader">Accesso all'archivio.</param>
    public ArchiveNightSource(IArchiveReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    /// <summary>Elenca i frame della notte.</summary>
    /// <param name="nightId">Identificatore della notte (AAAA-MM-GG).</param>
    /// <exception cref="StudioException">Identificatore di notte non valido.</exception>
    public ArchiveNightResult ListFrames(string nightId)
    {
        if (!AllSky.Archive.NightArchive.IsValidNightId(nightId))
        {
            throw new StudioException("identificatore di notte non valido (atteso AAAA-MM-GG).");
        }

        var frames = new List<StudioFrame>();
        var warnings = new List<string>();
        int missing = 0;
        int offset = 0;

        while (true)
        {
            IReadOnlyList<ArchiveFrame> page = _reader.ListFrames(nightId, offset, PageSize);
            foreach (ArchiveFrame entry in page)
            {
                string path;
                try
                {
                    path = _reader.GetFramePath(nightId, entry.FrameId);
                }
                catch (ArgumentException)
                {
                    missing++;
                    continue;
                }

                if (!File.Exists(path))
                {
                    missing++;
                    continue;
                }

                frames.Add(new StudioFrame(entry.FrameId + ".jpg", path, entry.TimestampUtc));
            }

            offset += page.Count;
            if (page.Count < PageSize)
            {
                break;
            }
        }

        if (frames.Count == 0 && missing == 0)
        {
            warnings.Add("la notte non ha frame.");
        }

        if (missing > 0)
        {
            warnings.Add($"{missing} frame indicizzati senza file JPEG: saltati.");
        }

        return new ArchiveNightResult(nightId, frames, warnings);
    }
}
