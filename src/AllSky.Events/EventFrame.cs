namespace AllSky.Events;

/// <summary>Frame da cui salvare gli eventi: pixel a piena risoluzione e dimensioni del frame ridotto del rilevatore.</summary>
public sealed record EventFrame
{
    /// <summary>Notte di appartenenza, "YYYY-MM-DD".</summary>
    public string NightId { get; init; } = string.Empty;

    /// <summary>Identificatore del frame ("yyyyMMddTHHmmssZ"), come in <see cref="AllSky.Archive.NightArchive"/>.</summary>
    public string FrameId { get; init; } = string.Empty;

    /// <summary>Istante del frame in UTC.</summary>
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Pixel RGB 8 bit a piena risoluzione, riga per riga, R,G,B. Lunghezza esatta Width*Height*3.</summary>
    public byte[] Rgb8 { get; init; } = Array.Empty<byte>();

    /// <summary>Larghezza di <see cref="Rgb8"/>.</summary>
    public int Width { get; init; }

    /// <summary>Altezza di <see cref="Rgb8"/>.</summary>
    public int Height { get; init; }

    /// <summary>Larghezza del frame ridotto su cui il rilevatore ha trovato le scie.</summary>
    public int DetectionWidth { get; init; }

    /// <summary>Altezza del frame ridotto su cui il rilevatore ha trovato le scie.</summary>
    public int DetectionHeight { get; init; }

    /// <summary>
    /// Frame vicini da usare per la clip, in ordine: di norma [N−2, N+2]. Quelli non archiviati vengono saltati.
    /// </summary>
    public IReadOnlyList<string> WindowFrameIds { get; init; } = Array.Empty<string>();
}

/// <summary>Posizione sul cielo di un punto del frame ridotto. Restituisce null se la camera non e' calibrata.</summary>
public interface IPixelToSky
{
    /// <summary>Alt/az del punto (x, y) del frame ridotto, o null.</summary>
    AltAz? ToAltAz(double x, double y);
}

/// <summary>Parametri del salvataggio. Valori iniziali da tarare.</summary>
public sealed record EventSaverOptions
{
    /// <summary>Qualita' JPEG di ritaglio e frame, 1..100.</summary>
    public int JpegQuality { get; init; } = 90;

    /// <summary>Lato minimo del ritaglio prima dell'ingrandimento, in pixel a piena risoluzione.</summary>
    public int MinCropPx { get; init; } = 96;

    /// <summary>Margine attorno alla scia nel ritaglio, in pixel a piena risoluzione.</summary>
    public int CropMarginPx { get; init; } = 24;

    /// <summary>Lato desiderato del ritaglio ingrandito, in pixel. L'ingrandimento lo porta vicino a questo valore.</summary>
    public int CropTargetPx { get; init; } = 512;

    /// <summary>Ingrandimento massimo intero del ritaglio.</summary>
    public int MaxCropMagnification { get; init; } = 4;

    /// <summary>Frame al secondo della mini-clip.</summary>
    public int ClipFps { get; init; } = 25;

    /// <summary>Verifica i valori. Lancia <see cref="ArgumentOutOfRangeException"/>.</summary>
    public void Validate()
    {
        Require(JpegQuality is >= 1 and <= 100, nameof(JpegQuality));
        Require(MinCropPx >= 1, nameof(MinCropPx));
        Require(CropMarginPx >= 0, nameof(CropMarginPx));
        Require(CropTargetPx >= 1, nameof(CropTargetPx));
        Require(MaxCropMagnification >= 1, nameof(MaxCropMagnification));
        Require(ClipFps is >= 1 and <= 120, nameof(ClipFps));
    }

    private static void Require(bool ok, string name)
    {
        if (!ok)
        {
            throw new ArgumentOutOfRangeException(name, "Valore fuori intervallo.");
        }
    }
}

/// <summary>Esito di un evento salvato.</summary>
public sealed record EventSaveResult
{
    /// <summary>Evento salvato, come letto da disco.</summary>
    public EventRecord Event { get; init; } = new();

    /// <summary>Motivo per cui la clip manca, se doveva esserci. Null se la clip e' riuscita o non era richiesta.</summary>
    public string? ClipError { get; init; }
}
