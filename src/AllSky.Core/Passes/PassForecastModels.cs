namespace AllSky.Core.Passes;

/// <summary>Stato della previsione dei passaggi, come lo vede la UI.</summary>
public enum PassForecastStatus
{
    /// <summary>Opt-in spento: nessuna richiesta a CelesTrak e nessun calcolo.</summary>
    Disabled,

    /// <summary>Attiva ma senza previsione: primo download in corso, nessun TLE o nessun bersaglio.</summary>
    Waiting,

    /// <summary>Previsione valida.</summary>
    Ready,

    /// <summary>CelesTrak ha sospeso le richieste: si usano i dati in cache, se ci sono.</summary>
    Blocked,

    /// <summary>Dati orbitali rifiutati o errore di calcolo: la previsione può essere incompleta.</summary>
    Failed,
}

/// <summary>Tipo di cambiamento notificato da <see cref="IPassForecastService.PassChanged"/>.</summary>
public enum PassChangeKind
{
    /// <summary>Un passaggio è iniziato.</summary>
    Started,

    /// <summary>Un passaggio è finito (o non è più nella previsione).</summary>
    Ended,

    /// <summary>La previsione è stata ricalcolata.</summary>
    ForecastUpdated,
}

/// <summary>Motivo per cui le tracce previste non sono disponibili.</summary>
public enum PassTrackBlock
{
    /// <summary>Disponibili.</summary>
    None,

    /// <summary>Previsioni satelliti spente nella configurazione.</summary>
    ServiceOff,

    /// <summary>Manca una calibrazione valida del cielo: senza di essa non ci sono pixel.</summary>
    NoCalibration,
}

/// <summary>
/// Un passaggio previsto. Gli istanti sono UTC. Se <see cref="StartTruncated"/> è vero, l'inizio è l'inizio
/// della finestra (il satellite era già sopra l'orizzonte); se <see cref="EndTruncated"/>, la fine è la fine della finestra.
/// </summary>
/// <param name="Target">Nome del satellite.</param>
/// <param name="NoradId">Numero NORAD.</param>
/// <param name="StartUtc">Inizio (AOS o inizio finestra).</param>
/// <param name="CulminationUtc">Culminazione: istante di elevazione massima.</param>
/// <param name="EndUtc">Fine (LOS o fine finestra).</param>
/// <param name="MaxElevationDeg">Elevazione massima, in gradi.</param>
/// <param name="StartAzimuthDeg">Azimut all'inizio, da nord verso est.</param>
/// <param name="EndAzimuthDeg">Azimut alla fine, da nord verso est.</param>
/// <param name="IlluminatedFraction">Frazione del passaggio con il satellite illuminato dal Sole, tra 0 e 1.</param>
/// <param name="VisibleToEye">Vero se il satellite è illuminato mentre il cielo è scuro.</param>
/// <param name="StartTruncated">Vero se l'inizio è quello della finestra.</param>
/// <param name="EndTruncated">Vero se la fine è quella della finestra.</param>
public sealed record PassForecastItem(
    string Target,
    int NoradId,
    DateTimeOffset StartUtc,
    DateTimeOffset CulminationUtc,
    DateTimeOffset EndUtc,
    double MaxElevationDeg,
    double StartAzimuthDeg,
    double EndAzimuthDeg,
    double IlluminatedFraction,
    bool VisibleToEye,
    bool StartTruncated,
    bool EndTruncated);

/// <summary>
/// Previsione corrente. <see cref="Passes"/> contiene i passaggi ancora da finire (anche quello in corso), in ordine di inizio.
/// <see cref="Current"/> è il passaggio in corso; <see cref="Next"/> il primo che inizia dopo l'istante di riferimento.
/// </summary>
/// <param name="Status">Stato.</param>
/// <param name="Message">Motivo per l'utente (blocco, rifiuto dei dati, errore), o null.</param>
/// <param name="ComputedUtc">Istante dell'ultimo calcolo, o null se non c'è stato.</param>
/// <param name="Passes">Passaggi da finire, in ordine di inizio.</param>
/// <param name="Current">Passaggio in corso, o null.</param>
/// <param name="Next">Prossimo passaggio, o null.</param>
public sealed record PassForecastSnapshot(
    PassForecastStatus Status,
    string? Message,
    DateTimeOffset? ComputedUtc,
    IReadOnlyList<PassForecastItem> Passes,
    PassForecastItem? Current,
    PassForecastItem? Next);

/// <summary>Cambiamento della previsione. <see cref="Pass"/> è null per <see cref="PassChangeKind.ForecastUpdated"/> e per i passaggi non più noti.</summary>
/// <param name="Kind">Tipo di cambiamento.</param>
/// <param name="Pass">Passaggio coinvolto, se noto.</param>
/// <param name="AtUtc">Istante del cambiamento.</param>
public sealed record PassChange(PassChangeKind Kind, PassForecastItem? Pass, DateTimeOffset AtUtc);

/// <summary>Punto di una traccia prevista nei pixel del frame di calibrazione.</summary>
/// <param name="Utc">Istante del punto.</param>
/// <param name="X">Coordinata x in pixel.</param>
/// <param name="Y">Coordinata y in pixel.</param>
public sealed record PassTrackPoint(DateTimeOffset Utc, double X, double Y);

/// <summary>Traccia prevista di un passaggio: uno o più tratti visibili nel cielo.</summary>
/// <param name="Target">Nome del satellite.</param>
/// <param name="StartUtc">Inizio del passaggio.</param>
/// <param name="EndUtc">Fine del passaggio.</param>
/// <param name="Runs">Tratti, ciascuno una poligonale in ordine temporale.</param>
public sealed record PassTrackPath(
    string Target,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    IReadOnlyList<IReadOnlyList<PassTrackPoint>> Runs);

/// <summary>
/// Tracce previste per l'immagine Live. Coordinate nel frame di calibrazione, di dimensione
/// <see cref="ReferenceWidthPx"/> x <see cref="ReferenceHeightPx"/> (0 se sconosciuta: la vista usa il frame ricevuto).
/// </summary>
/// <param name="Block">Motivo dell'eventuale indisponibilità; None se disponibili.</param>
/// <param name="ReferenceWidthPx">Larghezza del frame di riferimento, in pixel.</param>
/// <param name="ReferenceHeightPx">Altezza del frame di riferimento, in pixel.</param>
/// <param name="Paths">Tracce dei passaggi in corso o imminenti.</param>
public sealed record PassTrackState(
    PassTrackBlock Block,
    double ReferenceWidthPx,
    double ReferenceHeightPx,
    IReadOnlyList<PassTrackPath> Paths)
{
    /// <summary>Vero se le tracce sono disponibili (anche se l'elenco è vuoto).</summary>
    public bool Available => Block == PassTrackBlock.None;
}
