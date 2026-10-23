using System.Text.Json;

namespace AllSky.Studio;

/// <summary>
/// Nomi dei tipi di operazione (campo <c>type</c> di <see cref="StudioOperation"/>).
/// Il confronto è case-sensitive, come negli altri campi del progetto.
/// </summary>
public static class StudioOperationTypes
{
    /// <summary>Intervallo di tempo UTC (<c>from</c>, <c>to</c>).</summary>
    public const string Interval = "interval";

    /// <summary>Rimozione manuale di frame per nome file (<c>fileNames</c>).</summary>
    public const string RemoveFrames = "removeFrames";

    /// <summary>Rimozione automatica: frame vuoti, esposizione anomala, nuvole.</summary>
    public const string AutoRemove = "autoRemove";

    /// <summary>Velocità e fps di uscita (<c>factor</c>, <c>fps</c>).</summary>
    public const string Speed = "speed";

    /// <summary>Ritaglio in pixel (<c>x</c>, <c>y</c>, <c>width</c>, <c>height</c>).</summary>
    public const string Crop = "crop";

    /// <summary>Rotazione in gradi (<c>degrees</c>): 0, 90, 180 o 270.</summary>
    public const string Rotate = "rotate";

    /// <summary>Specchio orizzontale e/o verticale (<c>horizontal</c>, <c>vertical</c>).</summary>
    public const string Mirror = "mirror";

    /// <summary>Scheda di testo (<c>text</c>, <c>durationSeconds</c>, <c>position</c>).</summary>
    public const string TitleCard = "titleCard";

    /// <summary>Dissolvenza (<c>kind</c>, <c>durationSeconds</c>).</summary>
    public const string Fade = "fade";

    /// <summary>Maschera informativa. Solo parser in questa versione.</summary>
    public const string InfoMask = "infoMask";

    /// <summary>Correzione del colore. Solo parser in questa versione.</summary>
    public const string ColorCorrection = "colorCorrection";
}

/// <summary>
/// Operazione di montaggio tipizzata. Si ottiene da <see cref="OperationParser"/> a partire da
/// <see cref="StudioOperation"/>; il formato JSON del progetto non cambia.
/// </summary>
public abstract record MontageOperation;

/// <summary>
/// Tiene i frame con <c>From &lt;= istante &lt;= To</c> (estremi inclusi). Ogni estremo è facoltativo.
/// </summary>
public sealed record IntervalOperation(DateTimeOffset? From, DateTimeOffset? To) : MontageOperation;

/// <summary>Rimuove a mano i frame con questi nomi file (confronto senza distinzione di maiuscole).</summary>
public sealed record RemoveFramesOperation(IReadOnlyList<string> FileNames) : MontageOperation;

/// <summary>
/// Rimozione automatica. <see cref="BlankStdDevMax"/> è la soglia sulla deviazione standard della
/// luminanza (0..255); <see cref="MadFactor"/> è il k di <c>mediana ± k·MAD</c>.
/// <see cref="Clouds"/> è accettato ma non ancora eseguito: l'esecutore lo segnala.
/// </summary>
public sealed record AutoRemoveOperation(
    bool BlankFrames,
    bool ExposureOutliers,
    bool Clouds,
    double BlankStdDevMax,
    double MadFactor) : MontageOperation;

/// <summary>
/// Velocità: <see cref="Factor"/> 2 tiene 1 frame ogni 2, 0,5 ripete ogni frame due volte.
/// <see cref="Fps"/> è il frame rate di uscita del montaggio.
/// </summary>
public sealed record SpeedOperation(double Factor, int Fps) : MontageOperation;

/// <summary>Ritaglio in pixel. Il controllo sui limiti avviene quando l'immagine è nota.</summary>
public sealed record CropOperation(int X, int Y, int Width, int Height) : MontageOperation;

/// <summary>Rotazione oraria in gradi: 0, 90, 180 o 270.</summary>
public sealed record RotateOperation(int Degrees) : MontageOperation;

/// <summary>Specchio. Almeno uno dei due assi è vero.</summary>
public sealed record MirrorOperation(bool Horizontal, bool Vertical) : MontageOperation;

/// <summary>Posizione di una scheda di testo nel montaggio.</summary>
public enum TitleCardPosition
{
    /// <summary>Prima dei frame.</summary>
    Start,

    /// <summary>Dopo i frame.</summary>
    End,
}

/// <summary>Scheda di testo di durata fissa.</summary>
public sealed record TitleCardOperation(string Text, double DurationSeconds, TitleCardPosition Position) : MontageOperation;

/// <summary>Tipo di dissolvenza.</summary>
public enum FadeKind
{
    /// <summary>All'inizio del montaggio.</summary>
    In,

    /// <summary>Alla fine del montaggio.</summary>
    Out,

    /// <summary>Sul bordo tra ogni scheda titolo e il montaggio.</summary>
    Crossfade,
}

/// <summary>Dissolvenza di durata fissa.</summary>
public sealed record FadeOperation(FadeKind Kind, double DurationSeconds) : MontageOperation;

/// <summary>Maschera informativa: solo parser. I parametri sono conservati così come sono.</summary>
public sealed record InfoMaskOperation(IReadOnlyDictionary<string, JsonElement> Parameters) : MontageOperation;

/// <summary>Correzione del colore: solo parser. I parametri sono conservati così come sono.</summary>
public sealed record ColorCorrectionOperation(IReadOnlyDictionary<string, JsonElement> Parameters) : MontageOperation;
