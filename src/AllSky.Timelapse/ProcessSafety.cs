using System.ComponentModel;
using System.Diagnostics;

namespace AllSky.Timelapse;

/// <summary>Utilità per terminare i processi ffmpeg senza eccezioni.</summary>
internal static class ProcessSafety
{
    /// <summary>
    /// Termina l'intero albero di processi se il processo non è ancora uscito, e attende la fine.
    /// Non lancia eccezioni: un processo già terminato o non avviato viene ignorato.
    /// </summary>
    internal static void KillTree(Process process)
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
            // Terminazione non riuscita: il processo è già in uscita o non accessibile.
        }
    }

    /// <summary>Richiede la terminazione senza attendere. Usata dalle callback di cancellazione.</summary>
    internal static void RequestKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
