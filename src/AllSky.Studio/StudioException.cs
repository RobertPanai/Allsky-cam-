namespace AllSky.Studio;

/// <summary>
/// Errore del modulo Studio. Il messaggio è in italiano, senza stack trace e senza percorsi completi:
/// al massimo contiene il nome di un file.
/// </summary>
public sealed class StudioException : Exception
{
    /// <summary>Crea l'eccezione con un messaggio in italiano.</summary>
    /// <param name="message">Messaggio per l'utente.</param>
    public StudioException(string message)
        : base(message)
    {
    }

    /// <summary>Crea l'eccezione con un messaggio e la causa originale (solo per la diagnostica).</summary>
    /// <param name="message">Messaggio per l'utente.</param>
    /// <param name="inner">Eccezione originale.</param>
    public StudioException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>Ultime righe di stderr di ffmpeg, solo per i log. Non entrano nel messaggio.</summary>
    public string StderrTail { get; init; } = string.Empty;
}
