namespace AllSky.Core.Events;

/// <summary>Tipo di evento mostrato nella vista Eventi.</summary>
public enum EventCategory
{
    /// <summary>Meteora: scia singola.</summary>
    Meteor,

    /// <summary>Bolide: meteora molto luminosa o molto lunga.</summary>
    Bolide,

    /// <summary>Transito di un satellite identificato o previsto.</summary>
    Transit,
}

/// <summary>Classe assegnata dal classificatore a un evento. Indipendente dal modulo di archivio.</summary>
public enum EventClassKind
{
    /// <summary>Scia non classificata o non nota.</summary>
    Unknown,

    /// <summary>Meteora.</summary>
    Meteor,

    /// <summary>Bolide.</summary>
    Bolide,

    /// <summary>Satellite: coincide con una traccia prevista.</summary>
    Satellite,

    /// <summary>Aereo o satellite non previsto, che si ripete su frame consecutivi.</summary>
    AircraftOrSatellite,
}

/// <summary>Etichetta data dall'utente. Serve a tarare le soglie di rilevamento; non cancella l'evento.</summary>
public enum EventLabelChoice
{
    /// <summary>Nessuna etichetta.</summary>
    None,

    /// <summary>Confermato: l'evento è reale.</summary>
    Confirmed,

    /// <summary>Falso positivo.</summary>
    FalsePositive,
}

/// <summary>Esito della conferma di un transito nelle immagini.</summary>
public enum TransitOutcome
{
    /// <summary>Traccia confermata in almeno un frame.</summary>
    Identified,

    /// <summary>Nessuna scia lungo la traccia prevista.</summary>
    NotIdentified,

    /// <summary>Passaggio previsto, senza calibrazione: non verificabile.</summary>
    PredictedNotVerified,
}

/// <summary>Posizione sul cielo in gradi.</summary>
/// <param name="AltDeg">Altezza sopra l'orizzonte, in gradi.</param>
/// <param name="AzDeg">Azimut da nord verso est, in gradi.</param>
public sealed record SkyPoint(double AltDeg, double AzDeg);

/// <summary>Filtro della vista Eventi. Un campo null non filtra.</summary>
/// <param name="NightId">Notte "YYYY-MM-DD", oppure null per tutte.</param>
/// <param name="Category">Tipo, oppure null per tutti.</param>
public sealed record EventFilter(string? NightId = null, EventCategory? Category = null);

/// <summary>Riga della galleria: un evento senza i dettagli.</summary>
/// <param name="NightId">Notte di appartenenza.</param>
/// <param name="Id">Identificatore stabile dell'evento.</param>
/// <param name="Category">Tipo di evento.</param>
/// <param name="TimestampUtc">Istante del frame, in UTC.</param>
/// <param name="Score">Punteggio della scia (per i transiti, quello della conferma).</param>
/// <param name="Label">Etichetta dell'utente.</param>
/// <param name="ThumbnailPath">Immagine da usare come miniatura (percorso assoluto), o null se manca.</param>
public sealed record EventListItem(
    string NightId,
    string Id,
    EventCategory Category,
    DateTimeOffset TimestampUtc,
    double Score,
    EventLabelChoice Label,
    string? ThumbnailPath);

/// <summary>Dati di un passaggio di satellite, presenti solo per i transiti.</summary>
/// <param name="SatelliteName">Nome leggibile del satellite.</param>
/// <param name="NoradId">Numero di catalogo NORAD.</param>
/// <param name="StartUtc">Inizio del passaggio (o della finestra), in UTC.</param>
/// <param name="CulminationUtc">Culminazione, in UTC.</param>
/// <param name="EndUtc">Fine del passaggio (o della finestra), in UTC.</param>
/// <param name="MaxElevationDeg">Altezza massima prevista, in gradi.</param>
/// <param name="ConfirmationScore">Punteggio della conferma nelle immagini, tra 0 e 1.</param>
/// <param name="Outcome">Esito della conferma.</param>
/// <param name="FramesEvaluated">Frame della finestra valutati dal confermatore.</param>
public sealed record TransitInfo(
    string SatelliteName,
    int NoradId,
    DateTimeOffset StartUtc,
    DateTimeOffset CulminationUtc,
    DateTimeOffset EndUtc,
    double MaxElevationDeg,
    double ConfirmationScore,
    TransitOutcome Outcome,
    int FramesEvaluated);

/// <summary>Dettaglio di un evento: dati e percorsi dei file, già risolti e verificati.</summary>
/// <param name="NightId">Notte di appartenenza.</param>
/// <param name="Id">Identificatore dell'evento.</param>
/// <param name="Category">Tipo di evento.</param>
/// <param name="TimestampUtc">Istante del frame, in UTC.</param>
/// <param name="Class">Classe assegnata dal classificatore.</param>
/// <param name="Score">Punteggio della scia.</param>
/// <param name="LengthPx">Lunghezza della scia, in pixel del frame ridotto.</param>
/// <param name="PeakLuma">Eccesso di luminanza di picco, livelli 8 bit.</param>
/// <param name="IntegratedLuma">Luminosità integrata della scia.</param>
/// <param name="Label">Etichetta dell'utente.</param>
/// <param name="Start">Posizione del primo estremo sul cielo, o null se la camera non è calibrata.</param>
/// <param name="End">Posizione del secondo estremo sul cielo, o null se la camera non è calibrata.</param>
/// <param name="CropPath">Ritaglio ingrandito, o null.</param>
/// <param name="FramePath">Frame intero, o null.</param>
/// <param name="ClipPath">Mini-clip MP4, o null se non è stata prodotta.</param>
/// <param name="CompositePath">Composita del transito, o null.</param>
/// <param name="Transit">Dati del passaggio: presenti solo per i transiti.</param>
public sealed record EventDetail(
    string NightId,
    string Id,
    EventCategory Category,
    DateTimeOffset TimestampUtc,
    EventClassKind Class,
    double Score,
    double LengthPx,
    double PeakLuma,
    double IntegratedLuma,
    EventLabelChoice Label,
    SkyPoint? Start,
    SkyPoint? End,
    string? CropPath,
    string? FramePath,
    string? ClipPath,
    string? CompositePath,
    TransitInfo? Transit);
