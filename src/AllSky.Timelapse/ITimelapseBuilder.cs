namespace AllSky.Timelapse;

/// <summary>Costruisce un MP4 timelapse da una sequenza di JPEG.</summary>
public interface ITimelapseBuilder
{
    /// <summary>
    /// Crea il file <paramref name="outputMp4"/> dai JPEG nell'ordine dato.
    /// Il file viene scritto prima in un temporaneo e poi spostato in modo atomico.
    /// </summary>
    /// <param name="jpegPaths">Percorsi dei JPEG, nell'ordine di riproduzione. Tutti della stessa dimensione.</param>
    /// <param name="outputMp4">Percorso del file MP4 finale.</param>
    /// <param name="options">Parametri del video.</param>
    /// <param name="progress">Avanzamento da 0 a 1, riportato dopo ogni frame inviato. Può essere null.</param>
    /// <param name="ct">Cancellazione: termina ffmpeg e rimuove il temporaneo.</param>
    /// <exception cref="TimelapseException">ffmpeg assente, terminato con errore o senza output.</exception>
    /// <exception cref="OperationCanceledException">Operazione annullata.</exception>
    Task BuildAsync(
        IReadOnlyList<string> jpegPaths,
        string outputMp4,
        TimelapseOptions options,
        IProgress<double>? progress,
        CancellationToken ct);
}
