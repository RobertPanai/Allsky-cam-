namespace AllSky.Studio;

/// <summary>
/// Cerchio dell'immagine, in pixel: centro e raggio. Il keogram usa il quadrato
/// [cx - r, cx + r) × [cy - r, cy + r), che deve stare tutto dentro l'immagine.
/// </summary>
/// <param name="CenterX">Colonna del centro.</param>
/// <param name="CenterY">Riga del centro.</param>
/// <param name="Radius">Raggio, in pixel (almeno 8 per i cerchi scelti dall'utente o dal modello).</param>
public sealed record CircleSpec(int CenterX, int CenterY, int Radius);

/// <summary>
/// Anteprima veloce: un sottoinsieme di frame, ridotti a media di blocchi. Il rendering finale non la usa.
/// </summary>
public sealed record PreviewOptions
{
    /// <summary>Numero massimo di frame. I frame sono scelti a intervalli regolari (indice floor(k·n/MaxFrames)).</summary>
    public int MaxFrames { get; init; } = 200;

    /// <summary>Fattore intero di riduzione in ogni direzione (1 = nessuna riduzione).</summary>
    public int DownscaleFactor { get; init; } = 4;
}

/// <summary>Opzioni comuni a keogram e startrails.</summary>
public sealed record SeriesProductOptions
{
    /// <summary>Cerchio scelto dall'utente (selettore sull'immagine). Ha la precedenza sul modello ottico.</summary>
    public CircleSpec? Circle { get; init; }

    /// <summary>Cerchio dal modello ottico, se presente. Usato solo se <see cref="Circle"/> è null.</summary>
    public CircleSpec? FromOpticalModel { get; init; }

    /// <summary>Angolo del diametro del keogram, in gradi. 0 = colonna N-S.</summary>
    public double KeogramAngleDegrees { get; init; }

    /// <summary>Se vero, gli startrails usano solo il quadrato del cerchio invece dell'intero frame.</summary>
    public bool RestrictToCircle { get; init; }

    /// <summary>Se vero, le operazioni sui pixel del piano (ritaglio, rotazione, specchio) si applicano a ogni frame.</summary>
    public bool ApplyPixelOps { get; init; }

    /// <summary>Operazioni sui pixel del piano, nell'ordine dato. Usate solo con <see cref="ApplyPixelOps"/>.</summary>
    public IReadOnlyList<PixelOp> PixelOperations { get; init; } = [];

    /// <summary>Decadimento degli startrails, in 0..1 (1 = nessuno).</summary>
    public double Decay { get; init; } = 1.0;

    /// <summary>Frame da saltare senza decodificarli (per esempio già esclusi a mano). Null = nessuno.</summary>
    public Func<StudioFrame, bool>? SkipFrame { get; init; }

    /// <summary>Anteprima. Null = rendering finale a piena risoluzione su tutti i frame.</summary>
    public PreviewOptions? Preview { get; init; }
}

/// <summary>
/// Esito di un prodotto. Con errore o annullamento <see cref="Pixels"/> è null: non esistono immagini parziali.
/// </summary>
/// <param name="Pixels">Buffer RGB8 row-major, o null.</param>
/// <param name="Width">Larghezza in pixel.</param>
/// <param name="Height">Altezza in pixel.</param>
/// <param name="FramesUsed">Frame entrati nel prodotto.</param>
/// <param name="FramesSkipped">Frame saltati (illeggibili, non compatibili con ritaglio o cerchio, filtrati).</param>
/// <param name="Warnings">Avvisi in italiano, senza percorsi.</param>
/// <param name="Cancelled">Vero se l'operazione è stata annullata.</param>
/// <param name="Error">Errore in italiano, o null.</param>
public sealed record ProductResult(
    byte[]? Pixels,
    int Width,
    int Height,
    int FramesUsed,
    int FramesSkipped,
    IReadOnlyList<string> Warnings,
    bool Cancelled,
    string? Error)
{
    /// <summary>Vero se il prodotto è completo: nessun errore, nessun annullamento, buffer presente.</summary>
    public bool IsSuccess => Error is null && !Cancelled && Pixels is not null;
}
