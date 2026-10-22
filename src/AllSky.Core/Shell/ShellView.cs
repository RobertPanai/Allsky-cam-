namespace AllSky.Core.Shell;

/// <summary>
/// Viste della shell principale, nell'ordine della barra laterale (docs/ralph/IMPLEMENTATION_PLAN.md §5.2).
/// </summary>
public enum ShellView
{
    /// <summary>Schermata iniziale "Stanotte" (placeholder fino a P1.11).</summary>
    Tonight,

    /// <summary>Immagine del cielo in tempo reale (placeholder fino a P1.4).</summary>
    Live,

    /// <summary>Connessione e ricerca delle camere (placeholder fino a P1.5).</summary>
    Camera,

    /// <summary>Archivio delle notti, keogram e timelapse (placeholder fino a P1.6).</summary>
    Nights,

    /// <summary>Meteore, bolidi e transiti rilevati, con clip e conferma (P5.4).</summary>
    Events,

    /// <summary>Uplink e YouTube (placeholder fino a P1.7).</summary>
    Publication,

    /// <summary>Impostazioni dell'applicazione (placeholder fino a P1.8).</summary>
    Settings,

    /// <summary>Ultime righe del registro (placeholder fino a P1.8).</summary>
    Log,
}
