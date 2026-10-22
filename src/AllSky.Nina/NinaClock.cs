namespace AllSky.Nina;

/// <summary>
/// Correzione dell'orologio tra il PC di NINA e All Sky Brain (<c>nina.clockOffsetSeconds</c>, P11.1).
/// Convenzione: il valore si SOMMA all'orario di NINA per ottenere l'orario di All Sky Brain
/// (valore positivo: l'orologio di NINA e' indietro). Da confermare nella UI di P11.1.
/// </summary>
public static class NinaClock
{
    /// <summary>Limite assoluto della correzione, come in <c>ConfigStore</c>.</summary>
    public const double MaxCorrectionSeconds = 600;

    /// <summary>Converte un istante di NINA in UTC corretto.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Correzione non finita o oltre ±600 secondi.</exception>
    public static DateTimeOffset ToUtc(DateTimeOffset ninaInstant, double clockOffsetSeconds)
    {
        if (!double.IsFinite(clockOffsetSeconds) || Math.Abs(clockOffsetSeconds) > MaxCorrectionSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(clockOffsetSeconds), clockOffsetSeconds,
                "La correzione dell'orologio deve essere compresa tra -600 e 600 secondi.");
        }

        return ninaInstant.ToUniversalTime().AddSeconds(clockOffsetSeconds);
    }
}
