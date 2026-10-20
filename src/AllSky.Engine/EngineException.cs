namespace AllSky.Engine;

/// <summary>
/// Errore atteso del servizio. Il messaggio e' in italiano e dice cosa e' successo e cosa fare;
/// l'Host lo traduce in una risposta HTTP con <c>{ "error": ... }</c>.
/// </summary>
public sealed class EngineException : Exception
{
    /// <summary>Crea l'eccezione con un messaggio per l'utente.</summary>
    /// <param name="message">Messaggio in italiano.</param>
    public EngineException(string message)
        : base(message)
    {
    }

    /// <summary>Crea l'eccezione con un messaggio e la causa originale.</summary>
    /// <param name="message">Messaggio in italiano.</param>
    /// <param name="inner">Eccezione originale.</param>
    public EngineException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
