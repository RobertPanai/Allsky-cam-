namespace AllSky.Detection;

/// <summary>Classe assegnata a una scia dal <see cref="StreakClassifier"/>.</summary>
public enum StreakClass
{
    /// <summary>Scia singola, non attesa come satellite e non ripetuta: candidata meteora.</summary>
    Meteor,

    /// <summary>Meteora con picco saturo, luminosita' integrata molto alta o lunghezza molto grande.</summary>
    Bolide,

    /// <summary>Coincide con la traccia prevista di un satellite.</summary>
    Satellite,

    /// <summary>Oggetto allineato che si ripete su frame consecutivi: aereo o satellite non previsto.</summary>
    AereoOSatellite,
}

/// <summary>Una scia con la sua classe.</summary>
/// <param name="Streak">Scia rilevata dal <see cref="StreakDetector"/>.</param>
/// <param name="Class">Classe assegnata.</param>
public sealed record ClassifiedStreak(Streak Streak, StreakClass Class);

/// <summary>Esito della classificazione di un frame.</summary>
/// <param name="TimestampUtc">Istante del frame.</param>
/// <param name="FrameDiscarded">True se il frame e' stato scartato per variazione globale: in quel caso <see cref="Streaks"/> e' vuoto.</param>
/// <param name="Streaks">Scie classificate, nello stesso ordine di punteggio ricevuto dal rilevatore.</param>
public sealed record FrameClassification(
    DateTimeOffset TimestampUtc,
    bool FrameDiscarded,
    IReadOnlyList<ClassifiedStreak> Streaks);
