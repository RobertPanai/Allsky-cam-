using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using AllSky.Core;
using AllSky.Host;
using AllSky.Tray.Updates;
using Velopack;

namespace AllSky.Tray;

/// <summary>Cartelle e file usati dal Tray. Tutti i percorsi derivano da <see cref="DataFolder"/> e dalla cartella dell'eseguibile.</summary>
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

/// <summary>
/// Punto di ingresso del Tray di AllSky. Garantisce una sola istanza, avvia l'host in background
/// e mostra l'icona nell'area di notifica tramite <see cref="TrayContext"/>.
/// </summary>
internal static class Program
{
    /// <summary>Tempo massimo concesso all'avvio dell'host prima di considerarlo fallito.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Porta predefinita, usata se config.json manca o non la specifica.</summary>
    private const int DefaultPort = 5810;

    /// <summary>Impedisce di mostrare più finestre di crash se un'eccezione ne genera altre.</summary>
    private static int _inCrashReport;

    /// <summary>Avvia All Sky Brain. Restituisce 0 in uscita normale, 1 se l'host non parte.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // Velopack deve essere la prima istruzione: gestisce qui installazione, disinstallazione e riavvio dopo un aggiornamento.
        VelopackApp.Build().Run();

        // Cartella dati "AllSky" (non "AllSkyBrain"): il nome del prodotto è cambiato, ma il percorso di default resta
        // quello delle installazioni precedenti, così config.json, log e dati non vanno migrati.
        // Sovrascrivibile con --data-folder o con la variabile ALLSKY_DATA_FOLDER (vedi DataFolderResolver).
        string defaultDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllSky");
        string dataFolder = DataFolderResolver.Resolve(
            args, Environment.GetEnvironmentVariable(DataFolderResolver.EnvironmentVariableName), defaultDataFolder);

        // Mutex senza proprietà: serve solo a sapere se un'altra istanza esiste già per la stessa cartella dati.
        // Con cartelle diverse le istanze possono coesistere, perché il nome del mutex dipende dalla cartella.
        string mutexName = DataFolderResolver.SingleInstanceMutexName(dataFolder, defaultDataFolder);
        using var mutex = new Mutex(initiallyOwned: false, mutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("All Sky Brain è già in esecuzione (icona nella barra in basso).", "All Sky Brain",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        var paths = new AppPaths(
            DataFolder: dataFolder,
            ConfigPath: Path.Combine(dataFolder, "config.json"),
            WebRoot: Path.Combine(AppContext.BaseDirectory, "web"));

        // Crea anche DataFolder (CreateDirectory crea i genitori mancanti).
        Directory.CreateDirectory(paths.LogsFolder);

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(paths, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportCrash(paths, e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject.ToString()));

        // Da leggere prima dell'avvio: l'host può creare config.json e falsare la verifica di prima esecuzione.
        bool firstRun = !File.Exists(paths.ConfigPath);
        (bool english, int port) = PeekConfig(paths.ConfigPath);

        AllSkyHostRuntime runtime;
        try
        {
            if (!Directory.Exists(paths.WebRoot))
            {
                throw new DirectoryNotFoundException($"Cartella web non trovata: {paths.WebRoot}");
            }

            var options = new HostOptions(paths.ConfigPath, paths.DataFolder, paths.WebRoot)
            {
                UpdateBackend = VelopackBackend.Create(),
            };
            using var startCts = new CancellationTokenSource(StartTimeout);

            // Nessun SynchronizationContext è ancora attivo su questo thread: il blocco non può bloccare l'UI.
            runtime = AllSkyHost.StartAsync(options, startCts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.Append(paths.StartupLog, ex.ToString());
            MessageBox.Show(BuildStartupMessage(port, paths.StartupLog, ex), "All Sky Brain",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        Application.Run(new TrayContext(runtime, paths, english, openPanelOnStart: firstRun));
        return 0;
    }

    /// <summary>Testo dell'errore mostrato quando l'host non parte. La causa più comune è la porta occupata.</summary>
    private static string BuildStartupMessage(int port, string logPath, Exception ex)
    {
        return $"Impossibile avviare All Sky Brain: la porta {port} potrebbe essere occupata. " +
               "Chiudi l'altro programma o cambia la porta in Impostazioni.\n\n" +
               $"Dettagli: {ex.Message}\n\nRegistro: {logPath}";
    }

    /// <summary>
    /// Legge da config.json solo lingua e porta, senza passare dal modello completo di AllSky.Core.
    /// Se il file manca o non è leggibile restituisce i default (italiano, porta 5810).
    /// </summary>
    /// <param name="configPath">Percorso di config.json.</param>
    /// <returns>True se la lingua è "en"; la porta letta da ui.port.</returns>
    private static (bool English, int Port) PeekConfig(string configPath)
    {
        bool english = false;
        int port = DefaultPort;

        try
        {
            if (!File.Exists(configPath))
            {
                return (false, DefaultPort);
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (false, DefaultPort);
            }

            if (root.TryGetProperty("language", out JsonElement languageElement)
                && languageElement.ValueKind == JsonValueKind.String)
            {
                english = string.Equals(languageElement.GetString(), "en", StringComparison.OrdinalIgnoreCase);
            }

            if (root.TryGetProperty("ui", out JsonElement uiElement)
                && uiElement.ValueKind == JsonValueKind.Object
                && uiElement.TryGetProperty("port", out JsonElement portElement)
                && portElement.ValueKind == JsonValueKind.Number
                && portElement.TryGetInt32(out int configuredPort))
            {
                port = configuredPort;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // File illeggibile: restano i default. Il file JSON non valido lo rinomina l'host in ".bad".
        }

        return (english, port);
    }

    /// <summary>Registra un'eccezione non gestita nel crash log e mostra un messaggio (una sola volta alla volta).</summary>
    private static void ReportCrash(AppPaths paths, Exception ex)
    {
        if (Interlocked.Exchange(ref _inCrashReport, 1) == 1)
        {
            return;
        }

        AppLog.Append(paths.CrashLog, ex.ToString());
        MessageBox.Show(
            $"All Sky Brain ha riscontrato un errore imprevisto e potrebbe chiudersi.\n\nDettagli: {ex.Message}\n\nRegistro: {paths.CrashLog}",
            "All Sky Brain", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

/// <summary>Scrittura dei log di AllSky Tray. Un errore di scrittura non deve mai far fallire l'app.</summary>
internal static class AppLog
{
    /// <summary>Aggiunge una voce con data e ora al file indicato, creando le cartelle se mancano.</summary>
    /// <param name="filePath">File di log completo.</param>
    /// <param name="text">Testo della voce.</param>
    public static void Append(string filePath, string text)
    {
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(filePath, entry, Encoding.UTF8);
        }
        catch (Exception)
        {
            // Il log è best-effort: se non si può scrivere, l'app continua o si chiude comunque.
        }
    }
}
