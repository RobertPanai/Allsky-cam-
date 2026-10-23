using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllSky.Studio;

/// <summary>Stati di un lavoro di esportazione.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExportJobState
{
    /// <summary>In attesa di un posto libero nella coda.</summary>
    Queued,

    /// <summary>In corso.</summary>
    Running,

    /// <summary>Sospeso dall'utente: non riparte da solo.</summary>
    Paused,

    /// <summary>Completato. Non riparte mai.</summary>
    Succeeded,

    /// <summary>Fallito. Può essere riprovato solo se <see cref="ExportJob.Retryable"/> è vero.</summary>
    Failed,

    /// <summary>Annullato dall'utente.</summary>
    Cancelled,
}

/// <summary>
/// Un lavoro della coda. È un valore immutabile: ogni cambiamento produce una nuova copia (<c>with</c>).
/// Contiene solo dati non segreti, così il file di coda può essere letto senza rischi.
/// </summary>
/// <param name="Id">Identificativo deterministico: vedi <see cref="ExportJobFactory.JobId"/>.</param>
/// <param name="ProjectName">Nome del progetto, per i messaggi.</param>
/// <param name="ProjectPath">Percorso del file di progetto, per ricaricarne il piano. Non mostrato all'utente.</param>
/// <param name="PlanFingerprint">Impronta del piano al momento dell'accodamento.</param>
/// <param name="Sequence">Posizione della voce nell'elenco del progetto, da 1 (valore di <c>{n}</c>).</param>
/// <param name="Preset">Preset usato.</param>
/// <param name="Destination">Destinazione.</param>
/// <param name="State">Stato corrente.</param>
/// <param name="Progress">Avanzamento da 0 a 1. Non decresce durante un tentativo.</param>
/// <param name="Error">Messaggio di errore in italiano, o null.</param>
/// <param name="Retryable">Vero se «Riprova» è disponibile.</param>
/// <param name="Attempts">Quante volte il lavoro è partito.</param>
/// <param name="ConsecutiveFailures">Tentativi falliti di seguito. Con 3 il lavoro si ferma.</param>
/// <param name="InterruptedRuns">Quante volte un'esecuzione è stata interrotta da un riavvio.</param>
/// <param name="OutputFileName">Nome del file finale per le destinazioni locali, solo il nome.</param>
/// <param name="CreatedUtc">Istante di accodamento.</param>
public sealed record ExportJob(
    string Id,
    string ProjectName,
    string ProjectPath,
    string PlanFingerprint,
    int Sequence,
    ExportPreset Preset,
    ExportDestination? Destination,
    ExportJobState State,
    double Progress,
    string? Error,
    bool Retryable,
    int Attempts,
    int ConsecutiveFailures,
    int InterruptedRuns,
    string? OutputFileName,
    DateTimeOffset CreatedUtc);

/// <summary>Costruzione dei lavori a partire da un progetto e dal suo piano.</summary>
public static class ExportJobFactory
{
    /// <summary>
    /// Un lavoro per ogni voce di <see cref="StudioProject.Exports"/>. Chiamato due volte sullo stesso progetto
    /// (anche dopo un salvataggio e un ricaricamento) dà gli stessi lavori.
    /// </summary>
    public static IReadOnlyList<ExportJob> FromProject(StudioProject project, string projectPath, MontagePlan plan,
        DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(plan);
        string fingerprint = plan.Fingerprint();
        var jobs = new List<ExportJob>(project.Exports.Count);
        for (int i = 0; i < project.Exports.Count; i++)
        {
            jobs.Add(Create(project.Name, projectPath, fingerprint, i + 1, project.Exports[i], createdUtc));
        }

        return jobs;
    }

    /// <summary>Un lavoro da una singola voce. La voce deve essere già valida.</summary>
    public static ExportJob Create(string projectName, string projectPath, string planFingerprint, int sequence,
        ExportTarget target, DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new ExportJob(
            Id: JobId(planFingerprint, sequence, target),
            ProjectName: projectName,
            ProjectPath: projectPath,
            PlanFingerprint: planFingerprint,
            Sequence: sequence,
            Preset: target.Preset,
            Destination: target.Destination,
            State: ExportJobState.Queued,
            Progress: 0,
            Error: null,
            Retryable: false,
            Attempts: 0,
            ConsecutiveFailures: 0,
            InterruptedRuns: 0,
            OutputFileName: null,
            CreatedUtc: createdUtc);
    }

    /// <summary>
    /// Identificativo: SHA-256 di impronta del piano, numero della voce e JSON canonico della voce, troncato a 24
    /// caratteri esadecimali. Non dipende dall'istante di creazione né dalla cartella del progetto.
    /// </summary>
    public static string JobId(string planFingerprint, int sequence, ExportTarget target)
    {
        string json = JsonSerializer.Serialize(target, StudioProjectStore.JsonOptions);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{planFingerprint}|{sequence}|{json}"));
        return Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }
}

/// <summary>
/// Rimuove dai messaggi e dai log i segreti riconoscibili (token, chiavi, password, token di bot Telegram)
/// e i percorsi completi. È una rete di sicurezza: la coda non mette segreti nei messaggi per costruzione.
/// </summary>
public static class ExportRedactor
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    private static readonly System.Text.RegularExpressions.Regex KeyValueSecret = new(
        @"(?i)\b(access[_-]?token|refresh[_-]?token|client[_-]?secret|api[_-]?key|token|secret|password|bearer)\b([\s""':=]+)([^\s""'&,;]+)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        Timeout);

    private static readonly System.Text.RegularExpressions.Regex BotToken = new(
        @"(?<!\d)\d{5,}:[A-Za-z0-9_-]{16,}",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        Timeout);

    private static readonly System.Text.RegularExpressions.Regex FullPath = new(
        @"(?i)([a-z]:[\\/][^\s""']*|\\\\[^\s""']+|/(?:home|users|tmp|var|mnt|root|opt|srv|etc|media|data)/[^\s""']*)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant,
        Timeout);

    /// <summary>Testo ripulito. Null diventa stringa vuota.</summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string result = KeyValueSecret.Replace(text, m => m.Groups[1].Value + m.Groups[2].Value + "[segreto]");
        result = BotToken.Replace(result, "[segreto]");
        result = FullPath.Replace(result, "[percorso]");
        return result;
    }
}
