using AllSky.Detection;

namespace AllSky.Events;

/// <summary>Tipo di evento salvato.</summary>
public enum EventKind
{
    /// <summary>Meteora: scia singola, non attesa come satellite.</summary>
    Meteor,

    /// <summary>Bolide: meteora molto luminosa o molto lunga.</summary>
    Bolide,

    /// <summary>Transito di un satellite identificato (lo produce P4.5).</summary>
    Transit,
}

/// <summary>
/// Stato di un evento. Un evento scartato resta su disco con tutti i suoi file: lo stato serve a
/// nascondere il record nelle viste e a tarare i filtri, non a cancellare.
/// </summary>
public enum EventStatus
{
    /// <summary>Evento valido.</summary>
    Active,

    /// <summary>Evento scartato, per esempio perche' il frame successivo lo ha riclassificato come aereo o satellite.</summary>
    Discarded,
}

/// <summary>Etichetta data dall'utente nella vista Eventi. Serve a tarare le soglie di <see cref="AllSky.Detection.StreakClassifierOptions"/>.</summary>
public enum UserLabel
{
    /// <summary>Nessuna etichetta.</summary>
    None,

    /// <summary>Confermato: l'evento e' reale.</summary>
    Confirmed,

    /// <summary>Falso positivo.</summary>
    FalsePositive,
}

/// <summary>Posizione sul cielo in gradi: altezza sopra l'orizzonte e azimut da nord verso est.</summary>
public sealed record AltAz
{
    /// <summary>Altezza in gradi.</summary>
    public double AltDeg { get; init; }

    /// <summary>Azimut in gradi, da nord verso est.</summary>
    public double AzDeg { get; init; }
}

/// <summary>Segmento della scia nelle coordinate del frame ridotto del rilevatore (x verso destra, y verso il basso).</summary>
public sealed record PixelSegment
{
    /// <summary>Coordinata x del primo estremo.</summary>
    public double X1 { get; init; }

    /// <summary>Coordinata y del primo estremo.</summary>
    public double Y1 { get; init; }

    /// <summary>Coordinata x del secondo estremo.</summary>
    public double X2 { get; init; }

    /// <summary>Coordinata y del secondo estremo.</summary>
    public double Y2 { get; init; }
}

/// <summary>
/// Nomi dei file dell'evento, relativi alla sua cartella. Null se il file non e' stato prodotto
/// (per esempio la clip, se ffmpeg non era disponibile).
/// </summary>
public sealed record EventFiles
{
    /// <summary>Ritaglio ingrandito attorno alla scia (JPEG).</summary>
    public string? Crop { get; init; }

    /// <summary>Frame intero con la scia evidenziata (JPEG).</summary>
    public string? Frame { get; init; }

    /// <summary>Mini-clip dei frame vicini (MP4).</summary>
    public string? Clip { get; init; }
}

/// <summary>
/// Un evento salvato su disco (file <c>event.json</c>). Versionato con <see cref="SchemaVersion"/>:
/// un file con versione piu' recente di quella letta viene ignorato, non interpretato a metà.
/// </summary>
/// <remarks>
/// Lunghezza, luminosita' e segmento sono nelle unita' del frame ridotto del rilevatore (lato lungo 960 px di norma).
/// Gli angoli sono in gradi. <see cref="StartAltAz"/> e <see cref="EndAltAz"/> sono null se la camera non e' calibrata.
/// </remarks>
public sealed record EventRecord
{
    /// <summary>Versione dello schema corrente.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Versione dello schema di questo record.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Identificatore stabile, assegnato una volta sola. Vedi <see cref="EventIds"/>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Tipo: meteora, bolide o transito.</summary>
    public EventKind Kind { get; init; }

    /// <summary>Notte di appartenenza, "YYYY-MM-DD".</summary>
    public string NightId { get; init; } = string.Empty;

    /// <summary>Istante del frame in UTC.</summary>
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Identificatore del frame che ha prodotto l'evento ("yyyyMMddTHHmmssZ").</summary>
    public string SourceFrameId { get; init; } = string.Empty;

    /// <summary>Classe assegnata dal classificatore. Per i transiti sara' Satellite.</summary>
    public StreakClass Class { get; init; }

    /// <summary>Punteggio della scia (euristico, vedi <see cref="Streak.Score"/>).</summary>
    public double Score { get; init; }

    /// <summary>Lunghezza della scia, in pixel del frame ridotto.</summary>
    public double LengthPx { get; init; }

    /// <summary>Eccesso di luminanza di picco sopra il fondo, livelli 8 bit.</summary>
    public double PeakLuma { get; init; }

    /// <summary>Luminosita' integrata della scia, livelli 8 bit sommati sui pixel.</summary>
    public double IntegratedLuma { get; init; }

    /// <summary>Segmento della scia nel frame ridotto.</summary>
    public PixelSegment Segment { get; init; } = new();

    /// <summary>Posizione sul cielo del primo estremo, se calibrato.</summary>
    public AltAz? StartAltAz { get; init; }

    /// <summary>Posizione sul cielo del secondo estremo, se calibrato.</summary>
    public AltAz? EndAltAz { get; init; }

    /// <summary>File prodotti, relativi alla cartella dell'evento.</summary>
    public EventFiles Files { get; init; } = new();

    /// <summary>Etichetta dell'utente.</summary>
    public UserLabel Label { get; init; } = UserLabel.None;

    /// <summary>Motivo dell'etichetta, facoltativo.</summary>
    public string? LabelReason { get; init; }

    /// <summary>Stato dell'evento.</summary>
    public EventStatus Status { get; init; } = EventStatus.Active;

    /// <summary>Motivo dello stato (per esempio la riclassificazione), facoltativo.</summary>
    public string? StatusReason { get; init; }
}

/// <summary>Filtro per <see cref="EventArchive.List"/>. Un campo null non filtra.</summary>
public sealed record EventQuery
{
    /// <summary>Notte da elencare, "YYYY-MM-DD". Null = tutte le notti.</summary>
    public string? NightId { get; init; }

    /// <summary>Tipo da elencare. Null = tutti.</summary>
    public EventKind? Kind { get; init; }

    /// <summary>Etichetta da elencare. Null = tutte.</summary>
    public UserLabel? Label { get; init; }

    /// <summary>Se true includa anche gli eventi scartati. Di default sono nascosti.</summary>
    public bool IncludeDiscarded { get; init; }
}
