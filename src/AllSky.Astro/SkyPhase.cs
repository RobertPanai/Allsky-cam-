namespace AllSky.Astro;

/// <summary>
/// Fase del cielo in base all'altezza del Sole.
/// </summary>
public enum SkyPhase
{
    Day,
    CivilTwilight,
    NauticalTwilight,
    AstronomicalTwilight,
    Night,
}

public static class SkyPhases
{
    /// <summary>
    /// Classifica la fase del cielo dall'altezza del Sole (gradi).
    /// Soglie con estremo inferiore incluso:
    /// h >= 0 giorno; -6 &lt;= h &lt; 0 crepuscolo civile; -12 &lt;= h &lt; -6 nautico;
    /// -18 &lt;= h &lt; -12 astronomico; h &lt; -18 notte.
    /// </summary>
    public static SkyPhase Classify(double sunAltitudeDeg)
    {
        if (double.IsNaN(sunAltitudeDeg))
        {
            throw new ArgumentException("L'altezza del Sole non puo' essere NaN.", nameof(sunAltitudeDeg));
        }

        if (sunAltitudeDeg >= 0.0)
        {
            return SkyPhase.Day;
        }
        if (sunAltitudeDeg >= -6.0)
        {
            return SkyPhase.CivilTwilight;
        }
        if (sunAltitudeDeg >= -12.0)
        {
            return SkyPhase.NauticalTwilight;
        }
        if (sunAltitudeDeg >= -18.0)
        {
            return SkyPhase.AstronomicalTwilight;
        }
        return SkyPhase.Night;
    }

    /// <summary>
    /// Riduce la fase alle tre categorie usate dall'API locale: "day", "twilight" o "night".
    /// Tutti e tre i crepuscoli diventano "twilight".
    /// </summary>
    public static string Simplify(SkyPhase phase)
    {
        return phase switch
        {
            SkyPhase.Day => "day",
            SkyPhase.CivilTwilight => "twilight",
            SkyPhase.NauticalTwilight => "twilight",
            SkyPhase.AstronomicalTwilight => "twilight",
            SkyPhase.Night => "night",
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Fase sconosciuta."),
        };
    }
}
