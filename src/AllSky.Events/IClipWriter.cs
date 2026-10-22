using AllSky.Timelapse;

namespace AllSky.Events;

/// <summary>
/// Scrive una mini-clip MP4 da una sequenza di JPEG gia' archiviati. Iniettata in <see cref="EventSaver"/>:
/// in produzione <see cref="TimelapseClipWriter"/>, nei test un finto.
/// </summary>
public interface IClipWriter
{
    /// <summary>Crea <paramref name="outputMp4"/> dai JPEG nell'ordine dato.</summary>
    /// <param name="jpegPaths">Percorsi dei JPEG, in ordine di riproduzione.</param>
    /// <param name="outputMp4">File MP4 di destinazione, dentro la cartella dell'evento.</param>
    /// <param name="fps">Frame al secondo della clip.</param>
    /// <param name="ct">Cancellazione.</param>
    Task WriteAsync(IReadOnlyList<string> jpegPaths, string outputMp4, int fps, CancellationToken ct);
}

/// <summary>Implementazione di <see cref="IClipWriter"/> sopra il builder del timelapse (ffmpeg).</summary>
public sealed class TimelapseClipWriter : IClipWriter
{
    private readonly ITimelapseBuilder _builder;
    private readonly TimelapseOptions _baseOptions;

    /// <summary>Crea lo scrittore.</summary>
    /// <param name="builder">Builder del timelapse.</param>
    /// <param name="baseOptions">Opzioni di base (encoder, percorso ffmpeg, larghezza massima). Il fps viene sostituito da quello della chiamata.</param>
    public TimelapseClipWriter(ITimelapseBuilder builder, TimelapseOptions baseOptions)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _baseOptions = baseOptions ?? throw new ArgumentNullException(nameof(baseOptions));
    }

    /// <inheritdoc />
    public Task WriteAsync(IReadOnlyList<string> jpegPaths, string outputMp4, int fps, CancellationToken ct)
    {
        return _builder.BuildAsync(jpegPaths, outputMp4, _baseOptions with { Fps = fps }, null, ct);
    }
}
