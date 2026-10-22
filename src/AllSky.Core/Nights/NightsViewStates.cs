namespace AllSky.Core.Nights;

/// <summary>Stato della colonna dell'elenco notti.</summary>
public enum NightsPanelState
{
    /// <summary>Prima lettura dell'archivio in corso.</summary>
    Loading,

    /// <summary>Nessuna notte da mostrare e nessun errore.</summary>
    Empty,

    /// <summary>Nessuna notte da mostrare perché la lettura è fallita.</summary>
    Error,

    /// <summary>Almeno una notte da mostrare.</summary>
    Ready,
}

/// <summary>Sceglie lo stato della vista "Notti". Logica pura.</summary>
public static class NightsViewStates
{
    /// <summary>
    /// Risolve lo stato. Con notte presenti vince sempre "pronto", anche se un aggiornamento successivo è fallito:
    /// l'errore si mostra come avviso sopra l'elenco invece di cancellarlo.
    /// </summary>
    /// <param name="loaded">Vero dopo la prima lettura, riuscita o no.</param>
    /// <param name="nightCount">Numero di notti in elenco.</param>
    /// <param name="hasError">Vero se l'ultima lettura è fallita.</param>
    public static NightsPanelState Resolve(bool loaded, int nightCount, bool hasError)
    {
        if (!loaded)
        {
            return NightsPanelState.Loading;
        }

        if (nightCount > 0)
        {
            return NightsPanelState.Ready;
        }

        return hasError ? NightsPanelState.Error : NightsPanelState.Empty;
    }
}
