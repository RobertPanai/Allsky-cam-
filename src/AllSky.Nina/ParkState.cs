namespace AllSky.Nina;

/// <summary>
/// Stato di park del telescopio, derivato da <see cref="NinaState"/>.
/// </summary>
public enum ParkStatus
{
    /// <summary>Il mount risulta parcheggiato (<c>AtPark</c> = true).</summary>
    Parked,
    /// <summary>Il mount insegue il cielo (<c>TrackingMode</c> indica un inseguimento attivo).</summary>
    Tracking,
    /// <summary>Il mount si sta spostando (<c>Slewing</c> = true).</summary>
    Slewing,
    /// <summary>Il mount e' noto, non parcheggiato, e non insegue ne' si muove.</summary>
    Stopped,
    /// <summary>Stato non determinabile: manca il dato di park, o il mount non ha risposto.</summary>
    Unknown,
    /// <summary>NINA non e' raggiungibile (<c>Connected</c> = false).</summary>
    Disconnected,
}

/// <summary>
/// Calcola <see cref="ParkStatus"/> dallo stato NINA. Funzione pura: nessun I/O, nessun orologio.
/// </summary>
/// <remarks>
/// [NV] Il nome del campo <c>AtPark</c> e i valori di <c>TrackingMode</c> non sono verificati su NINA reale
/// (vedi docs/NINA-API.md e <see cref="NinaFieldNames"/>, da verificare in P11.13).
/// Un valore assente non e' mai "parcheggiato": <c>AtPark</c> null produce <see cref="ParkStatus.Unknown"/>.
/// Per <c>TrackingMode</c> si considera "in inseguimento" qualunque testo non vuoto diverso da "Off" o "None"
/// (confronto senza distinzione di maiuscole). Il significato dei valori e' [NV].
/// </remarks>
public static class ParkState
{
    /// <summary>Ordine di valutazione: Disconnected, Unknown, Parked, Slewing, Tracking, altrimenti Stopped.</summary>
    public static ParkStatus Compute(NinaState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.Connected)
            return ParkStatus.Disconnected;

        NinaMountState? mount = state.Mount;
        if (mount is null)
            return ParkStatus.Unknown;

        if (mount.AtPark is null)
        {
            // Senza dato di park, l'unica informazione certa e' il movimento: un mount che si muove non e' parcheggiato.
            return mount.Slewing == true ? ParkStatus.Slewing : ParkStatus.Unknown;
        }

        if (mount.AtPark == true)
            return ParkStatus.Parked;

        if (mount.Slewing == true)
            return ParkStatus.Slewing;

        if (IsTracking(mount.TrackingMode))
            return ParkStatus.Tracking;

        return ParkStatus.Stopped;
    }

    /// <summary>True se il testo di <c>TrackingMode</c> indica un inseguimento attivo. [NV] sui valori.</summary>
    internal static bool IsTracking(string? trackingMode)
    {
        if (string.IsNullOrWhiteSpace(trackingMode))
            return false;

        string v = trackingMode.Trim();
        return !v.Equals("Off", StringComparison.OrdinalIgnoreCase)
            && !v.Equals("None", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Una transizione di stato di park, con l'istante UTC in cui il nuovo stato e' stato osservato.</summary>
public sealed record ParkTransition(ParkStatus From, ParkStatus To, DateTimeOffset AtUtc);

/// <summary>
/// Registra le transizioni di <see cref="ParkStatus"/> da un flusso di osservazioni.
/// </summary>
/// <remarks>
/// Regole: un'osservazione uguale alla precedente non produce eventi; la prima osservazione fissa lo stato
/// iniziale senza produrre una transizione (non esiste un "da"); Unknown verso Unknown non viene mai emesso
/// (coincide con il caso "uguale alla precedente"); ogni altra variazione, compresa quella verso Disconnected, e' registrata.
/// Le osservazioni devono arrivare in ordine temporale; l'istante viene normalizzato in UTC.
/// </remarks>
public sealed class ParkTransitionLog
{
    private ParkStatus? _last;

    /// <summary>Stato corrente noto, o null se non e' ancora arrivata alcuna osservazione.</summary>
    public ParkStatus? Current => _last;

    /// <summary>
    /// Registra un'osservazione. Restituisce la transizione se lo stato e' cambiato, altrimenti null.
    /// </summary>
    public ParkTransition? Observe(ParkStatus status, DateTimeOffset atUtc)
    {
        ParkStatus? previous = _last;
        _last = status;

        if (previous is null || previous.Value == status)
            return null;

        return new ParkTransition(previous.Value, status, atUtc.ToUniversalTime());
    }

    /// <summary>Applica <see cref="Observe"/> a una sequenza e restituisce le transizioni in ordine.</summary>
    public static IReadOnlyList<ParkTransition> FromSequence(IEnumerable<(ParkStatus Status, DateTimeOffset AtUtc)> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var log = new ParkTransitionLog();
        var result = new List<ParkTransition>();
        foreach (var (status, atUtc) in observations)
        {
            ParkTransition? t = log.Observe(status, atUtc);
            if (t is not null)
                result.Add(t);
        }
        return result;
    }
}
