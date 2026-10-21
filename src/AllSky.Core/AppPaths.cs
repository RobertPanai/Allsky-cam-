namespace AllSky.Core;

/// <summary>
/// Cartelle e file usati dalle app Windows (Tray e app desktop). Tutti i percorsi derivano da <see cref="DataFolder"/>
/// e dalla cartella dell'eseguibile. Sta in AllSky.Core perché è logica pura, senza dipendenze da WinForms o WPF.
/// </summary>
/// <param name="DataFolder">Cartella dati utente (%LOCALAPPDATA%\AllSky).</param>
/// <param name="ConfigPath">File di configurazione (config.json dentro la cartella dati).</param>
/// <param name="WebRoot">Cartella web servita dall'host (accanto all'eseguibile).</param>
public sealed record AppPaths(string DataFolder, string ConfigPath, string WebRoot)
{
    /// <summary>Cartella dei log, dentro la cartella dati.</summary>
    public string LogsFolder => Path.Combine(DataFolder, "logs");

    /// <summary>Log degli errori di avvio dell'host.</summary>
    public string StartupLog => Path.Combine(LogsFolder, "startup.log");

    /// <summary>Log delle eccezioni non gestite e degli errori di arresto.</summary>
    public string CrashLog => Path.Combine(LogsFolder, "crash.log");
}
