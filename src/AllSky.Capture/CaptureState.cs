namespace AllSky.Capture;

/// <summary>Stato del <see cref="CaptureLoop"/>.</summary>
public enum CaptureState
{
    /// <summary>Fermo, oppure in attesa perché è giorno e la cattura diurna è disattivata.</summary>
    Idle,

    /// <summary>In esecuzione, con l'ultima cattura riuscita (o nessun errore consecutivo).</summary>
    Capturing,

    /// <summary>In esecuzione, con almeno un errore di cattura consecutivo.</summary>
    Error,
}
