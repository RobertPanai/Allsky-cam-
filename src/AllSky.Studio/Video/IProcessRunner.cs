namespace AllSky.Studio;

/// <summary>Esito di un processo esterno.</summary>
/// <param name="ExitCode">Codice di uscita.</param>
/// <param name="StderrTail">Ultime righe di stderr, solo per i log.</param>
public sealed record ProcessRunResult(int ExitCode, string StderrTail);

/// <summary>
/// Esecuzione di un processo esterno. Iniettabile: i test usano una finta che non lancia nulla.
/// Gli argomenti vanno passati come lista, mai come stringa concatenata.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Esegue il processo e attende la fine. Se <paramref name="ct"/> viene annullato, termina il processo.</summary>
    Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct);
}
