namespace AllSky.Cameras.Alpaca;

/// <summary>Errore restituito dal dispositivo o dal server Alpaca (ErrorNumber diverso da zero).</summary>
public sealed class AlpacaException : Exception
{
    /// <summary>Crea l'eccezione con il codice di errore Alpaca e il messaggio del dispositivo.</summary>
    /// <param name="errorNumber">Valore di ErrorNumber restituito dal dispositivo (diverso da zero).</param>
    /// <param name="message">Messaggio di ErrorMessage (o testo dell'errore ImageBytes).</param>
    public AlpacaException(int errorNumber, string message)
        : base($"Errore Alpaca {errorNumber}: {message}")
    {
        ErrorNumber = errorNumber;
    }

    /// <summary>Codice di errore ASCOM/Alpaca, sempre diverso da zero.</summary>
    public int ErrorNumber { get; }
}
