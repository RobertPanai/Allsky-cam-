namespace AllSky.Timelapse;

/// <summary>Errore nella costruzione del timelapse (ffmpeg assente, terminato con errore, file non prodotto).</summary>
public sealed class TimelapseException : Exception
{
    /// <summary>Crea l'eccezione con un messaggio.</summary>
    /// <param name="message">Messaggio in italiano.</param>
    public TimelapseException(string message)
        : base(message)
    {
        StderrTail = string.Empty;
    }

    /// <summary>Crea l'eccezione con un messaggio e le ultime righe di stderr di ffmpeg.</summary>
    /// <param name="message">Messaggio in italiano.</param>
    /// <param name="stderrTail">Ultime righe di stderr di ffmpeg (vuoto se non disponibili).</param>
    public TimelapseException(string message, string stderrTail)
        : base(message)
    {
        StderrTail = stderrTail;
    }

    /// <summary>Crea l'eccezione con un messaggio e un'eccezione interna.</summary>
    /// <param name="message">Messaggio in italiano.</param>
    /// <param name="inner">Eccezione originale.</param>
    public TimelapseException(string message, Exception inner)
        : base(message, inner)
    {
        StderrTail = string.Empty;
    }

    /// <summary>Ultime righe di stderr di ffmpeg, se il processo è stato eseguito.</summary>
    public string StderrTail { get; }
}
