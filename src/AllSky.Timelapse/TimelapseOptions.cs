namespace AllSky.Timelapse;

/// <summary>Parametri del timelapse MP4.</summary>
public sealed record TimelapseOptions
{
    /// <summary>Frame al secondo del video di output (e della sequenza in ingresso). Intervallo 1..120.</summary>
    public int Fps { get; init; } = 25;

    /// <summary>
    /// Nome dell'encoder di ffmpeg (es. "h264_mf"). Se impostato, viene usato senza prova e senza retry.
    /// Se null, il builder sceglie l'encoder con <see cref="EncoderProbe.GetWorkingAsync"/>.
    /// </summary>
    public string? Encoder { get; init; }

    /// <summary>
    /// Larghezza massima del video. Se il JPEG è più largo, viene scalato in giù mantenendo le proporzioni.
    /// Null = nessuno scale.
    /// </summary>
    public int? MaxWidth { get; init; }

    /// <summary>Percorso dell'eseguibile ffmpeg, normalmente trovato con <see cref="FfmpegLocator"/>.</summary>
    public string? FfmpegPath { get; init; }
}
