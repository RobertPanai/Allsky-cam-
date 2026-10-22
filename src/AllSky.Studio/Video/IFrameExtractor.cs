namespace AllSky.Studio;

/// <summary>Un frame estratto da un video.</summary>
/// <param name="FilePath">Percorso del JPEG estratto.</param>
/// <param name="OffsetFromStart">Istante del frame rispetto all'inizio del video (il video non ha un orario).</param>
public sealed record ExtractedFrame(string FilePath, TimeSpan OffsetFromStart);

/// <summary>Esito dell'estrazione: frame in ordine temporale.</summary>
/// <param name="Frames">Frame estratti, dal primo all'ultimo.</param>
public sealed record ExtractResult(IReadOnlyList<ExtractedFrame> Frames);

/// <summary>Estrazione di frame da un video a intervallo fisso.</summary>
public interface IFrameExtractor
{
    /// <summary>
    /// Estrae un frame ogni <paramref name="intervalSeconds"/> secondi nella cartella di output.
    /// Lancia <see cref="StudioException"/> in caso di errore e <see cref="OperationCanceledException"/> se annullata.
    /// </summary>
    /// <param name="videoPath">File video di ingresso.</param>
    /// <param name="outputFolder">Cartella dei JPEG estratti (creata se manca).</param>
    /// <param name="intervalSeconds">Intervallo tra due frame, in secondi.</param>
    /// <param name="ct">Token di annullamento: termina il processo.</param>
    Task<ExtractResult> ExtractAsync(string videoPath, string outputFolder, double intervalSeconds, CancellationToken ct);
}
