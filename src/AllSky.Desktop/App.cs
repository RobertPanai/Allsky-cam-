using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AllSky.Core;
using AllSky.Desktop.Theme;
using AllSky.Desktop.Views;
using AllSky.Host;

namespace AllSky.Desktop;

/// <summary>
/// Applicazione WPF senza XAML. Il processo resta in vita mentre l'host gira: chiudere la finestra la nasconde soltanto,
/// l'uscita vera passa dal menu dell'icona ("Esci") e arresta l'host in modo ordinato.
/// </summary>
/// <remarks>
/// Il ciclo di vita segue quello del Tray (TrayContext): arresto dell'host con timeout, idempotente, in più punti
/// (uscita dal menu, <see cref="OnExit"/> come rete di sicurezza).
/// </remarks>
public sealed class App : System.Windows.Application
{
    /// <summary>Tempo massimo concesso all'arresto dell'host prima di uscire comunque.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly AllSkyHostRuntime _runtime;
    private readonly AppPaths _paths;
    private readonly DesktopTexts _texts;
    private readonly bool _showGallery;
    private MainWindow? _window;
    private ComponentGalleryWindow? _gallery;
    private TrayIcon? _tray;
    private bool _exiting;
    private bool _hostStopped;

    /// <summary>Crea l'applicazione. La finestra e l'icona si creano solo all'avvio (<see cref="OnStartup"/>).</summary>
    /// <param name="runtime">Host già avviato, da arrestare all'uscita.</param>
    /// <param name="paths">Cartelle e file usati per i log.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    /// <param name="showGallery">Se true, al posto della finestra principale si apre la "Galleria componenti" (argomento <c>--gallery</c>).</param>
    public App(AllSkyHostRuntime runtime, AppPaths paths, DesktopTexts texts, bool showGallery = false)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(texts);

        _runtime = runtime;
        _paths = paths;
        _texts = texts;
        _showGallery = showGallery;
    }

    /// <summary>Gestore del tema (colori, luminosità, modalità notte). Creato all'avvio, prima di qualsiasi finestra.</summary>
    public ThemeManager Theme { get; private set; } = null!;

    /// <inheritdoc />
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // Senza questa impostazione la chiusura dell'ultima finestra terminerebbe l'app: qui la finestra si nasconde.
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        // Rete di sicurezza come nel Tray (UnhandledExceptionMode.CatchException): l'eccezione viene registrata e l'interfaccia resta viva.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Tema prima delle finestre: le risorse di colore e font devono esistere quando i controlli le cercano.
        Theme = new ThemeManager(Resources);
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/AllSkyBrain;component/Theme/Styles.xaml", UriKind.Relative),
        });
        ApplySavedTheme();

        _window = new MainWindow(_runtime.Service, _texts, _runtime, _paths);
        _tray = new TrayIcon(_runtime, _paths, _texts, ShowMainWindow, ExitApplication);

        if (_showGallery)
        {
            // Con --gallery la galleria sostituisce la finestra principale. La finestra principale resta creata
            // (non mostrata) così che l'icona nell'area di notifica continui a funzionare.
            _gallery = new ComponentGalleryWindow(Theme);
            _gallery.Show();
        }
        else
        {
            _window.Show();
        }
    }

    /// <summary>Applica la modalità notte rossa salvata in <c>ui.theme</c>. Un errore di lettura lascia il tema normale.</summary>
    private void ApplySavedTheme()
    {
        try
        {
            Theme.SetNightMode(_runtime.Service.GetConfig().Ui.Theme == AllSky.Core.Theme.Red);
        }
        catch (Exception)
        {
            // Il tema è solo una preferenza: in caso di problema si parte con quello normale.
        }
    }

    /// <summary>Mostra la finestra principale: dall'icona o dal doppio clic. Ripristina la finestra se è ridotta a icona.</summary>
    public void ShowMainWindow()
    {
        if (_window is null || _exiting)
        {
            return;
        }

        if (_window.WindowState == System.Windows.WindowState.Minimized)
        {
            _window.WindowState = System.Windows.WindowState.Normal;
        }

        _window.Show();
        _window.Activate();
    }

    /// <summary>Esce davvero dal menu "Esci": rimuove l'icona, arresta l'host (con timeout) e chiude l'applicazione.</summary>
    public void ExitApplication()
    {
        _exiting = true;
        _window?.PrepareForExit();
        StopAll();
        Shutdown();
    }

    /// <inheritdoc />
    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        // Rete di sicurezza: se l'applicazione si chiude per altri motivi, l'host viene comunque arrestato.
        _exiting = true;
        _window?.PrepareForExit();
        StopAll();
        base.OnExit(e);
    }

    /// <summary>Rimuove l'icona e arresta l'host. È idempotente: può essere chiamato più volte.</summary>
    private void StopAll()
    {
        if (_hostStopped)
        {
            return;
        }

        _hostStopped = true;

        // Prima l'icona, così l'utente vede subito che l'applicazione si sta chiudendo.
        _tray?.Dispose();
        _tray = null;

        StopHost();
    }

    /// <summary>
    /// Ferma e libera l'host. Il lavoro gira su un thread del pool per evitare che le continuazioni dell'host
    /// tentino di tornare sul thread dell'interfaccia, che in questo momento è bloccato in attesa.
    /// </summary>
    private void StopHost()
    {
        try
        {
            Task.Run(async () =>
            {
                await _runtime.StopAsync().ConfigureAwait(false);
                await _runtime.DisposeAsync().ConfigureAwait(false);
            }).WaitAsync(StopTimeout).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Arresto scaduto o fallito: l'app esce comunque, ma il motivo resta nel log.
            AppLog.Append(_paths.CrashLog, $"Arresto dell'host non completato: {ex}");
        }
    }

    /// <summary>Registra l'eccezione dell'interfaccia e impedisce la chiusura immediata.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Program.ReportCrash(_paths, _texts, e.Exception);
        e.Handled = true;
    }
}
