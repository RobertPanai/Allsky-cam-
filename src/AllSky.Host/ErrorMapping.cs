using AllSky.Engine;

namespace AllSky.Host;

/// <summary>Corpo di ogni risposta d'errore: <c>{ "error": "messaggio" }</c>.</summary>
/// <param name="Error">Messaggio in italiano che dice cosa e' successo e cosa fare.</param>
internal sealed record ErrorBody(string Error);

/// <summary>Traduce le eccezioni in codici HTTP e messaggi per l'utente.</summary>
internal static class ErrorMapping
{
    /// <summary>Restituisce il codice HTTP e il messaggio per un'eccezione non gestita dall'endpoint.</summary>
    /// <param name="ex">Eccezione sollevata.</param>
    /// <param name="host">Contesto dell'Host, per il riferimento al log.</param>
    public static (int Status, string Message) Map(Exception ex, HostContext host)
    {
        switch (ex)
        {
            case ArgumentException argument:
                // I messaggi di NightArchive e del motore sono gia' in italiano e dicono cosa correggere.
                return (400, CleanParameterSuffix(argument.Message));
            case EngineException engine:
                return (500, engine.Message);
            case FileNotFoundException or DirectoryNotFoundException:
                return (404, "File non trovato. Riprovare o scegliere un'altra notte dall'elenco.");
            case InvalidOperationException invalid:
                return (409, invalid.Message);
            default:
                host.Log.Write("Errore non gestito durante una richiesta HTTP", ex);
                return (500, $"Errore interno del motore: {ex.Message}. Dettagli nel file di log in '{host.LogFolder}'.");
        }
    }

    /// <summary>Toglie il suffisso "(Parameter 'x')" che .NET aggiunge ai messaggi degli argomenti.</summary>
    private static string CleanParameterSuffix(string message)
    {
        int index = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return index > 0 ? message[..index] : message;
    }
}
