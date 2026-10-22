using AllSky.Transits;

namespace AllSky.Events;

/// <summary>
/// Metadati di un transito salvato (<see cref="EventKind.Transit"/>). Gli orari sono in UTC; l'altezza massima
/// e' quella del passaggio previsto, il punteggio e il verdetto vengono dalla conferma nelle immagini.
/// </summary>
public sealed record TransitDetails
{
    /// <summary>Nome leggibile del satellite.</summary>
    public string SatelliteName { get; init; } = string.Empty;

    /// <summary>Numero di catalogo NORAD del satellite.</summary>
    public int NoradId { get; init; }

    /// <summary>Inizio del passaggio (AOS), o inizio della finestra se il passaggio era gia' in corso.</summary>
    public DateTimeOffset StartUtc { get; init; }

    /// <summary>Culminazione del passaggio.</summary>
    public DateTimeOffset CulminationUtc { get; init; }

    /// <summary>Fine del passaggio (LOS), o fine della finestra se il passaggio continua oltre.</summary>
    public DateTimeOffset EndUtc { get; init; }

    /// <summary>Elevazione massima del passaggio, in gradi.</summary>
    public double MaxElevationDeg { get; init; }

    /// <summary>Punteggio della conferma nelle immagini, tra 0 e 1.</summary>
    public double Score { get; init; }

    /// <summary>Verdetto della conferma nelle immagini.</summary>
    public TransitVerdict Verdict { get; init; }

    /// <summary>Numero di frame della finestra del passaggio valutati dal confermatore.</summary>
    public int FramesEvaluated { get; init; }

    /// <summary>Lunghezza della traccia visibile prevista, in pixel del frame di calibrazione.</summary>
    public double VisibleTrackLengthPx { get; init; }
}
