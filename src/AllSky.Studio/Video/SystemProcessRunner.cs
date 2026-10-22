using System.ComponentModel;
using System.Diagnostics;

namespace AllSky.Studio;

/// <summary>
/// Runner reale basato su <see cref="Process"/>. Su annullamento termina l'intero albero di processi.
/// Non usa la shell e non apre finestre.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    private const int StderrTailLines = 20;

    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ct.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new StudioException("impossibile avviare ffmpeg.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new StudioException("impossibile avviare ffmpeg.", ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }

        await Task.WhenAll(stdoutTask, stderrTask);
        return new ProcessRunResult(process.ExitCode, Tail(stderrTask.Result));
    }

    private static string Tail(string text)
    {
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('\n', lines.TakeLast(StderrTailLines));
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch (InvalidOperationException)
        {
            // Già terminato o non avviato.
        }
        catch (Win32Exception)
        {
            // Terminazione non riuscita: il processo è già in uscita.
        }
    }
}
