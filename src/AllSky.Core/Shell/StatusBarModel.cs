namespace AllSky.Core.Shell;

/// <summary>Fase del cielo mostrata nella barra di stato. "Unknown" se il servizio non la conosce.</summary>
public enum StatusPhase
{
    /// <summary>Fase non disponibile.</summary>
    Unknown,

    /// <summary>Sole sopra l'orizzonte.</summary>
    Day,

    /// <summary>Crepuscolo: Sole sotto l'orizzonte, ma non ancora notte.</summary>
    Twilight,

    /// <summary>Notte.</summary>
    Night,
}

/// <summary>Stato delle pubblicazioni (Uplink e YouTube). "Unknown" finché il servizio non lo espone.</summary>
public enum PublicationStatus
{
    /// <summary>Stato non disponibile.</summary>
    Unknown,

    /// <summary>Ultima pubblicazione riuscita.</summary>
    Ok,

    /// <summary>Pubblicazione in coda o in corso.</summary>
    Pending,

    /// <summary>Ultima pubblicazione fallita.</summary>
    Error,
}

/// <summary>
/// Dati della barra di stato così come arrivano dal servizio. Ogni valore mancante è null (o Unknown):
/// la formattazione li mostra come "n/d", mai come zero.
/// </summary>
/// <param name="Phase">"day", "twilight", "night" oppure null.</param>
/// <param name="SunAltitudeDeg">Altezza del Sole in gradi; null se non calcolabile.</param>
/// <param name="MoonAltitudeDeg">Altezza della Luna in gradi; null se non calcolabile.</param>
/// <param name="CameraConnected">Vero se la camera è collegata, falso se non lo è, null se non si sa.</param>
/// <param name="CameraName">Nome della camera; null o vuoto se non disponibile.</param>
/// <param name="LastFrameTimestamp">Istante in UTC dell'ultimo frame archiviato; null se non c'è.</param>
/// <param name="DiskFreeGb">Spazio libero sul volume dell'archivio, in GB; null se non leggibile.</param>
/// <param name="Publication">Stato delle pubblicazioni.</param>
/// <param name="PassTarget">Satellite del passaggio in corso o del prossimo, o null se la previsione non ne ha (P4.3).</param>
/// <param name="PassStartUtc">Inizio di quel passaggio in UTC, o null.</param>
/// <param name="PassInProgress">Vero se quel passaggio è in corso.</param>
public sealed record StatusBarSnapshot(
    string? Phase,
    double? SunAltitudeDeg,
    double? MoonAltitudeDeg,
    bool? CameraConnected,
    string? CameraName,
    DateTimeOffset? LastFrameTimestamp,
    double? DiskFreeGb,
    PublicationStatus Publication,
    string? PassTarget = null,
    DateTimeOffset? PassStartUtc = null,
    bool PassInProgress = false);

/// <summary>
/// Una voce della barra di stato, già formattata nella lingua scelta.
/// </summary>
/// <param name="Label">Etichetta breve, per esempio "Sole".</param>
/// <param name="Value">Valore da mostrare; "n/d" se manca.</param>
/// <param name="Available">Falso se il dato manca.</param>
/// <param name="Tooltip">Spiegazione mostrata al passaggio del mouse, solo se il dato manca; altrimenti null.</param>
public sealed record StatusBarItem(string Label, string Value, bool Available, string? Tooltip);

/// <summary>
/// Barra di stato completa, nell'ordine in cui compare in basso nella finestra.
/// </summary>
/// <param name="Camera">Stato della camera.</param>
/// <param name="Phase">Fase del cielo.</param>
/// <param name="Sun">Altezza del Sole.</param>
/// <param name="Moon">Altezza della Luna.</param>
/// <param name="LastFrame">Ora dell'ultimo frame.</param>
/// <param name="Disk">Spazio libero su disco.</param>
/// <param name="Publications">Stato delle pubblicazioni.</param>
/// <param name="PhaseKind">Fase in forma tipizzata, per scegliere l'icona.</param>
/// <param name="NextPass">Prossimo transito di satellite ("Transito"). Null solo nei test che costruiscono il testo a mano.</param>
public sealed record StatusBarText(
    StatusBarItem Camera,
    StatusBarItem Phase,
    StatusBarItem Sun,
    StatusBarItem Moon,
    StatusBarItem LastFrame,
    StatusBarItem Disk,
    StatusBarItem Publications,
    StatusPhase PhaseKind,
    StatusBarItem? NextPass = null);
