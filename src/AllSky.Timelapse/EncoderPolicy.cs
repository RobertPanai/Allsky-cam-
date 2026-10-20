namespace AllSky.Timelapse;

/// <summary>
/// Regole per scegliere l'encoder H.264 tra quelli disponibili in ffmpeg.
/// <list type="bullet">
/// <item><see cref="Preferred"/>: ordine di preferenza. Il primo disponibile vince.</item>
/// <item><see cref="AllowGpl"/>: libx264 è GPL. Senza questo consenso libx264 non viene mai scelto,
/// anche se è in <see cref="Preferred"/> ed è disponibile.</item>
/// <item><see cref="AllowFallbackMpeg4"/>: se nessun H.264 è disponibile, usa mpeg4 (MPEG-4 Parte 2, non H.264).
/// Il risultato lo segnala nel messaggio.</item>
/// </list>
/// </summary>
public sealed record EncoderPolicy
{
    /// <summary>Encoder da provare, in ordine di preferenza.</summary>
    public IReadOnlyList<string> Preferred { get; init; } =
        ["h264_mf", "h264_nvenc", "h264_qsv", "h264_amf", "libx264"];

    /// <summary>Consente l'uso di libx264 (licenza GPL). Default false.</summary>
    public bool AllowGpl { get; init; } = false;

    /// <summary>Consente il fallback a mpeg4 quando non c'è nessun H.264. Default false.</summary>
    public bool AllowFallbackMpeg4 { get; init; } = false;
}

/// <summary>Esito della scelta dell'encoder.</summary>
/// <param name="Encoder">Nome dell'encoder di ffmpeg (es. "h264_mf"), oppure null se nessun encoder è utilizzabile.</param>
/// <param name="IsH264">True se l'encoder produce H.264.</param>
/// <param name="Message">Messaggio in italiano: motivo dell'indisponibilità oppure avviso (fallback, encoder non H.264). Null se tutto è regolare.</param>
public sealed record EncoderSelection(string? Encoder, bool IsH264, string? Message)
{
    /// <summary>True se è stato scelto un encoder.</summary>
    public bool IsAvailable => Encoder is not null;
}
