using AllSky.YouTube;

namespace AllSky.Studio;

/// <summary>
/// Produce un file video dal piano di montaggio. L'implementazione reale (ffmpeg, o altro) sta nell'host;
/// nei test è finta. Il file va scritto al percorso <paramref name="outputPath"/>: la coda gli passa un file
/// temporaneo e lo rinomina solo a rendering finito. In caso di annullamento il file può restare parziale:
/// la coda lo cancella comunque.
/// </summary>
public interface IVideoRenderer
{
    /// <param name="plan">Piano di montaggio già validato.</param>
    /// <param name="preset">Preset con il frame rate già risolto (mai 0).</param>
    /// <param name="outputPath">File temporaneo da scrivere.</param>
    /// <param name="progress">Avanzamento da 0 a 1. Può essere chiamata molte volte.</param>
    /// <param name="cancellationToken">Annullamento.</param>
    Task RenderAsync(MontagePlan plan, ExportPreset preset, string outputPath, IProgress<double> progress,
        CancellationToken cancellationToken);
}

/// <summary>Dati di un caricamento su YouTube. Titolo e descrizione sono già resi dai modelli.</summary>
/// <param name="FilePath">File MP4 completo, già pronto.</param>
/// <param name="Title">Titolo finale.</param>
/// <param name="Description">Descrizione finale.</param>
/// <param name="Privacy">Privacy scelta.</param>
public sealed record YouTubeUploadRequest(string FilePath, string Title, string Description, YouTubePrivacy Privacy);

/// <summary>
/// Caricamento su YouTube. L'implementazione reale riusa il flusso OAuth e l'upload di P7 (<c>NightUploader</c>,
/// <c>ResumableUploadClient</c>) e vive nell'host. Restituisce l'identificativo del video.
/// Un errore di rete va segnalato con <see cref="ExportTransportException"/>.
/// </summary>
public interface IYouTubeUploader
{
    Task<string> UploadAsync(YouTubeUploadRequest request, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Invio di un video a una chat Telegram. Implementato nell'host con <c>TelegramSender</c>, che fa i controlli
/// di autorizzazione e di limite. Un errore di rete va segnalato con <see cref="ExportTransportException"/>.
/// </summary>
public interface ITelegramPublisher
{
    Task SendVideoAsync(long chatId, string filePath, string? caption, IProgress<double>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Ricarica il piano di un progetto quando la coda lo riprende dopo un riavvio (il piano in memoria non c'è più).
/// Restituisce null se il progetto non esiste più.
/// </summary>
public interface IExportPlanResolver
{
    Task<MontagePlan?> ResolveAsync(ExportJob job, CancellationToken cancellationToken);
}

/// <summary>
/// Errore di un passo dell'esportazione. Il messaggio è in italiano, senza percorsi completi; <see cref="Retryable"/>
/// dice se ha senso il pulsante «Riprova».
/// </summary>
public class ExportStepException : Exception
{
    /// <summary>Crea l'eccezione.</summary>
    /// <param name="userMessage">Messaggio per l'utente, in italiano.</param>
    /// <param name="retryable">Vero se un nuovo tentativo può riuscire.</param>
    public ExportStepException(string userMessage, bool retryable = false)
        : base(userMessage)
    {
        Retryable = retryable;
    }

    /// <summary>Crea l'eccezione con la causa originale (solo per la diagnostica: il messaggio resta quello dell'utente).</summary>
    /// <param name="userMessage">Messaggio per l'utente, in italiano.</param>
    /// <param name="inner">Eccezione originale.</param>
    public ExportStepException(string userMessage, Exception inner)
        : base(userMessage, inner)
    {
        Retryable = false;
    }

    /// <summary>Vero se un nuovo tentativo può riuscire.</summary>
    public bool Retryable { get; }
}

/// <summary>
/// Caduta della connessione durante un caricamento o un invio. È l'unico errore che propone «Riprova».
/// Il messaggio di questa eccezione serve solo al registro: la coda mostra il proprio testo.
/// </summary>
public sealed class ExportTransportException : ExportStepException
{
    /// <summary>Crea l'eccezione di rete.</summary>
    /// <param name="message">Dettaglio per il registro (verrà ripulito dai segreti).</param>
    public ExportTransportException(string message)
        : base(message, retryable: true)
    {
    }
}
