namespace AllSky.Timelapse;

/// <summary>
/// Cerca l'eseguibile ffmpeg in quest'ordine: percorso configurato, cartella <c>tools/ffmpeg</c> accanto
/// all'eseguibile, PATH di sistema. Non avvia ffmpeg: controlla solo che il file esista.
/// <list type="bullet">
/// <item>Un percorso configurato che non esiste viene ignorato e la ricerca prosegue, senza errori.</item>
/// <item>Il nome del file è <c>ffmpeg.exe</c> su Windows e <c>ffmpeg</c> sugli altri sistemi.</item>
/// <item>Restituisce null se ffmpeg non si trova in nessuna delle tre posizioni.</item>
/// </list>
/// </summary>
public static class FfmpegLocator
{
    /// <summary>Nome dell'eseguibile per il sistema operativo corrente.</summary>
    public static string ExecutableName { get; } = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>Cartella relativa a quella dell'eseguibile in cui cercare la copia distribuita con l'app.</summary>
    public static string BundledRelativeDirectory { get; } = Path.Combine("tools", "ffmpeg");

    /// <summary>
    /// Restituisce il percorso completo di ffmpeg, oppure null se non è stato trovato.
    /// </summary>
    /// <param name="configuredPath">Percorso impostato dall'utente, oppure null.</param>
    /// <param name="baseDirectory">Cartella dell'eseguibile. Null = <see cref="AppContext.BaseDirectory"/>.</param>
    /// <param name="pathVariable">Valore della variabile PATH. Null = quella del processo.</param>
    public static string? Locate(string? configuredPath = null, string? baseDirectory = null, string? pathVariable = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        string bundled = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, BundledRelativeDirectory, ExecutableName);
        if (File.Exists(bundled))
        {
            return Path.GetFullPath(bundled);
        }

        string? path = pathVariable ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (string entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string dir = entry.Trim().Trim('"');
            if (dir.Length == 0)
            {
                continue;
            }

            try
            {
                string candidate = Path.Combine(dir, ExecutableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (ArgumentException)
            {
                // Voce di PATH non valida come percorso: si ignora.
            }
        }

        return null;
    }
}
