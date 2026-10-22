using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllSky.Core;
using AllSky.Desktop.Overlay;
using AllSky.Host;
using AllSky.Tray.Updates;
using Velopack;

namespace AllSky.Desktop;

/// <summary>
/// Punto di ingresso di All Sky Brain (app WPF). Garantisce una sola istanza, avvia l'host in-process
/// e poi l'applicazione (<see cref="App"/>) con finestra e icona nell'area di notifica.
/// </summary>
/// <remarks>
/// Condivisione col Tray, senza copie:
/// <list type="bullet">
/// <item>AppPaths, AppLog e ConfigPeek stanno in AllSky.Core (logica pura, condivisa e testata qui).</item>
/// <item>VelopackBackend viene riusato tramite <c>Compile Include</c> nel csproj.</item>
/// </list>
/// La sequenza di avvio replica AllSky.Tray.Program: il Tray è ora legacy, il suo codice resta come riferimento.
/// Il mutex usa lo stesso nome del Tray: i due programmi non possono girare insieme sulla stessa cartella dati.
/// </remarks>
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
        // Velopack deve essere la prima istruzione: gestisce installazione, disinstallazione e riavvio dopo un aggiornamento.
        VelopackApp.Build().Run();

        // Cartella dati "AllSky" (non "AllSkyBrain"): il percorso di default resta quello delle installazioni precedenti,
        // così config.json, log e dati non vanno migrati. Sovrascrivibile con --data-folder o ALLSKY_DATA_FOLDER.
        string defaultDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllSky");
        string dataFolder = DataFolderResolver.Resolve(
            args, Environment.GetEnvironmentVariable(DataFolderResolver.EnvironmentVariableName), defaultDataFolder);

        var paths = new AppPaths(
            DataFolder: dataFolder,
            ConfigPath: Path.Combine(dataFolder, "config.json"),
            WebRoot: Path.Combine(AppContext.BaseDirectory, "web"));

        // La lingua si legge prima del mutex, così anche il messaggio di seconda istanza è nella lingua giusta.
        (bool english, int port) = ConfigPeek.Read(paths.ConfigPath);
        DesktopTexts texts = english ? DesktopTexts.English : DesktopTexts.Italian;

        // Mutex senza proprietà: serve solo a sapere se un'altra istanza esiste già per la stessa cartella dati.
        string mutexName = DataFolderResolver.SingleInstanceMutexName(dataFolder, defaultDataFolder);
        using var mutex = new Mutex(initiallyOwned: false, mutexName, out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(texts.AlreadyRunning, texts.WindowTitle,
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return 0;
        }

        // Crea anche DataFolder (CreateDirectory crea i genitori mancanti).
        Directory.CreateDirectory(paths.LogsFolder);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportCrash(paths, texts, e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject.ToString()));

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
                FrameOverlayFactory = WpfFrameOverlay.CreateFactory(paths.DataFolder),
                TleFetcher = PassForecastWiring.CreateTleFetcher(paths.DataFolder),
                OrbitPropagators = new AllSky.Satellites.Sgp4.SgpNetPropagatorFactory(),
                ObservingConditions = new AllSky.Weather.ComObservingConditionsFactory(),
            };
            using var startCts = new CancellationTokenSource(StartTimeout);

            // Nessun SynchronizationContext è ancora attivo su questo thread: il blocco non può bloccare l'interfaccia.
            runtime = AllSkyHost.StartAsync(options, startCts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            AppLog.Append(paths.StartupLog, ex.ToString());
            System.Windows.MessageBox.Show(string.Format(texts.StartupError, port, ex.Message, paths.StartupLog),
                texts.WindowTitle, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return 1;
        }

        // --gallery apre la "Galleria componenti" al posto della finestra principale (solo prova dell'interfaccia).
        bool showGallery = args is not null && args.Any(a => string.Equals(a, GalleryArgument, StringComparison.OrdinalIgnoreCase));
        var app = new App(runtime, paths, texts, showGallery);
        return app.Run();
    }

    /// <summary>Argomento da riga di comando che apre la galleria dei componenti.</summary>
    internal const string GalleryArgument = "--gallery";

    /// <summary>Registra un'eccezione non gestita nel crash log e mostra un messaggio (una sola volta alla volta).</summary>
    /// <param name="paths">Cartelle e file dell'app.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    /// <param name="ex">Eccezione da registrare.</param>
    internal static void ReportCrash(AppPaths paths, DesktopTexts texts, Exception ex)
    {
        if (Interlocked.Exchange(ref _inCrashReport, 1) == 1)
        {
            return;
        }

        AppLog.Append(paths.CrashLog, ex.ToString());
        System.Windows.MessageBox.Show(string.Format(texts.CrashMessage, ex.Message, paths.CrashLog),
            texts.WindowTitle, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }
}
