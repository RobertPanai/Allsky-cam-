namespace AllSky.Studio;

/// <summary>
/// Preset di esportazione: risoluzione, frame rate e qualità del file finale.
/// I valori predefiniti sono in <see cref="ExportPresets"/>. Il controllo dei valori è in <see cref="ExportValidator"/>.
/// </summary>
/// <remarks>
/// Convenzioni [S-scelta]:
/// <list type="bullet">
/// <item><see cref="Width"/> o <see cref="Height"/> a 0 = dimensione della sorgente.</item>
/// <item><see cref="Fps"/> a 0 = frame rate del montaggio.</item>
/// <item><see cref="Quality"/> è un CRF in scala 0..51, default 20. La scala è una scelta del progetto; la resa
/// con ffmpeg reale non è verificata [NV].</item>
/// </list>
/// Nessun preset è un requisito di una piattaforma: i nomi descrivono solo il formato.
/// </remarks>
public sealed record ExportPreset
{
    /// <summary>Nome mostrato all'utente, in italiano.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Larghezza in pixel, 0 = come la sorgente.</summary>
    public int Width { get; init; }

    /// <summary>Altezza in pixel, 0 = come la sorgente.</summary>
    public int Height { get; init; }

    /// <summary>Frame rate, 0 = come il montaggio.</summary>
    public int Fps { get; init; }

    /// <summary>Qualità come CRF, 0..51 (più basso = più qualità e file più grande).</summary>
    public int Quality { get; init; } = ExportPresets.DefaultQuality;

    /// <summary>Contenitore. In questa versione solo "mp4".</summary>
    public string Container { get; init; } = ExportPresets.Mp4;
}

/// <summary>Preset predefiniti e limiti dei loro valori.</summary>
public static class ExportPresets
{
    /// <summary>Contenitore unico supportato.</summary>
    public const string Mp4 = "mp4";

    /// <summary>Qualità predefinita (CRF) [S-scelta].</summary>
    public const int DefaultQuality = 20;

    /// <summary>Qualità minima accettata (CRF).</summary>
    public const int MinQuality = 0;

    /// <summary>Qualità massima accettata (CRF).</summary>
    public const int MaxQuality = 51;

    /// <summary>Dimensione minima di un lato, se non è 0. Scelta prudente [S-scelta].</summary>
    public const int MinDimension = 16;

    /// <summary>Dimensione massima di un lato [S-scelta].</summary>
    public const int MaxDimension = 8192;

    /// <summary>Frame rate massimo accettato [S-scelta].</summary>
    public const int MaxFps = 120;

    /// <summary>Lunghezza massima del nome di un preset.</summary>
    public const int MaxNameLength = 60;

    /// <summary>Originale: risoluzione e frame rate della sorgente, qualità predefinita.</summary>
    public static ExportPreset Originale => new()
    {
        Name = "Originale",
        Width = 0,
        Height = 0,
        Fps = 0,
        Quality = DefaultQuality,
    };

    /// <summary>Full HD 1920x1080.</summary>
    public static ExportPreset FullHd => new()
    {
        Name = "Full HD 1920x1080",
        Width = 1920,
        Height = 1080,
        Fps = 0,
        Quality = DefaultQuality,
    };

    /// <summary>HD 1280x720.</summary>
    public static ExportPreset Hd => new()
    {
        Name = "HD 1280x720",
        Width = 1280,
        Height = 720,
        Fps = 0,
        Quality = DefaultQuality,
    };

    /// <summary>Quadrato 1080x1080, solo formato di comodo: non è il requisito di nessuna piattaforma.</summary>
    public static ExportPreset Social => new()
    {
        Name = "Social 1080x1080",
        Width = 1080,
        Height = 1080,
        Fps = 0,
        Quality = DefaultQuality,
    };

    /// <summary>Tutti i preset predefiniti, nell'ordine di visualizzazione.</summary>
    public static IReadOnlyList<ExportPreset> All { get; } = [Originale, FullHd, Hd, Social];
}
