namespace AllSky.Nina;

/// <summary>
/// Collegamento tra eventi NINA e frame per istante UTC. Le liste devono essere ordinate per istante
/// (<see cref="NinaEventLog.ReadAll"/> le restituisce ordinate; per i frame usare l'ordine cronologico di
/// <c>NightArchive.ListFrames</c>). Con liste non ordinate il risultato non e' affidabile.
/// La finestra e' ±<c>windowSeconds</c>, estremi inclusi, intervallo 0..900 secondi.
/// </summary>
public static class NinaEventLinking
{
    /// <summary>Finestra predefinita, in secondi.</summary>
    public const int DefaultWindowSeconds = 60;

    /// <summary>Finestra massima, in secondi.</summary>
    public const int MaxWindowSeconds = 900;

    /// <summary>Frame piu' vicino a un evento.</summary>
    /// <param name="Index">Posizione nella lista dei frame.</param>
    /// <param name="FrameUtc">Istante del frame.</param>
    /// <param name="DistanceSeconds">Distanza assoluta in secondi.</param>
    public sealed record NearestFrame(int Index, DateTimeOffset FrameUtc, double DistanceSeconds);

    /// <summary>Eventi entro ±<paramref name="windowSeconds"/> dall'istante del frame, estremi inclusi. Lista vuota se nessuno.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Finestra fuori da 0..900.</exception>
    public static IReadOnlyList<NinaEvent> EventsAroundFrame(
        IReadOnlyList<NinaEvent> sortedEvents,
        DateTimeOffset frameUtc,
        int windowSeconds = DefaultWindowSeconds)
    {
        ArgumentNullException.ThrowIfNull(sortedEvents);
        ValidateWindow(windowSeconds);

        DateTimeOffset centro = frameUtc.ToUniversalTime();
        DateTimeOffset da = centro.AddSeconds(-windowSeconds);
        DateTimeOffset a = centro.AddSeconds(windowSeconds);

        var risultato = new List<NinaEvent>();
        for (int i = LowerBound(sortedEvents, e => e.AtUtc, da); i < sortedEvents.Count && sortedEvents[i].AtUtc <= a; i++)
        {
            risultato.Add(sortedEvents[i]);
        }

        return risultato;
    }

    /// <summary>
    /// Frame piu' vicino all'istante dell'evento, se distante al massimo <paramref name="windowSeconds"/>.
    /// Null se la lista e' vuota o se nessun frame e' abbastanza vicino (l'evento non si collega a nulla).
    /// A parita' di distanza vince il frame precedente.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Finestra fuori da 0..900.</exception>
    public static NearestFrame? NearestFrameIndex(
        IReadOnlyList<DateTimeOffset> sortedFrameInstants,
        DateTimeOffset eventUtc,
        int windowSeconds = DefaultWindowSeconds)
    {
        ArgumentNullException.ThrowIfNull(sortedFrameInstants);
        ValidateWindow(windowSeconds);

        if (sortedFrameInstants.Count == 0)
        {
            return null;
        }

        DateTimeOffset evento = eventUtc.ToUniversalTime();
        int primo = LowerBound(sortedFrameInstants, f => f, evento);

        int migliore = -1;
        double distanzaMigliore = double.PositiveInfinity;

        // Candidato precedente (istante < evento) e successivo (istante >= evento).
        if (primo - 1 >= 0)
        {
            migliore = primo - 1;
            distanzaMigliore = Distance(sortedFrameInstants[migliore], evento);
        }

        if (primo < sortedFrameInstants.Count)
        {
            double d = Distance(sortedFrameInstants[primo], evento);
            if (d < distanzaMigliore)
            {
                migliore = primo;
                distanzaMigliore = d;
            }
        }

        if (migliore < 0 || distanzaMigliore > windowSeconds)
        {
            return null;
        }

        return new NearestFrame(migliore, sortedFrameInstants[migliore].ToUniversalTime(), distanzaMigliore);
    }

    /// <summary>
    /// Correzione dell'orologio applicata a un istante di NINA. Alias di <see cref="NinaClock.ToUtc"/>, per chi legge il collegamento.
    /// </summary>
    public static DateTimeOffset ApplyClockOffset(DateTimeOffset ninaInstant, double clockOffsetSeconds) =>
        NinaClock.ToUtc(ninaInstant, clockOffsetSeconds);

    private static double Distance(DateTimeOffset a, DateTimeOffset b) => Math.Abs((a.ToUniversalTime() - b.ToUniversalTime()).TotalSeconds);

    /// <summary>Primo indice con chiave >= valore (ricerca binaria su lista ordinata).</summary>
    private static int LowerBound<T>(IReadOnlyList<T> lista, Func<T, DateTimeOffset> chiave, DateTimeOffset valore)
    {
        int basso = 0;
        int alto = lista.Count;
        while (basso < alto)
        {
            int medio = basso + ((alto - basso) / 2);
            if (chiave(lista[medio]) < valore)
            {
                basso = medio + 1;
            }
            else
            {
                alto = medio;
            }
        }

        return basso;
    }

    private static void ValidateWindow(int windowSeconds)
    {
        if (windowSeconds is < 0 or > MaxWindowSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSeconds), windowSeconds,
                "La finestra deve essere compresa tra 0 e 900 secondi.");
        }
    }
}
