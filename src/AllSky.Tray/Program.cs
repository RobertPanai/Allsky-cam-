using System.Windows.Forms;
using AllSky.Core;
using AllSky.Host;
using AllSky.Tray.Updates;
using Velopack;

namespace AllSky.Tray;

/// <summary>
/// Punto di ingresso del Tray di AllSky (legacy: l'exe pubblicato è ora AllSky.Desktop). Garantisce una sola istanza,
/// avvia l'host in background e mostra l'icona nell'area di notifica tramite <see cref="TrayContext"/>.
/// AppPaths, AppLog e la lettura di config.json (ConfigPeek) sono in AllSky.Core, condivisi con l'app desktop.
/// </summary>
internal static class Program
{
    /// <summary>Tempo massimo concesso all'avvio dell'host prima di considerarlo fallito.</summary>
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

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
        (bool english, int port) = ConfigPeek.Read(paths.ConfigPath);

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
