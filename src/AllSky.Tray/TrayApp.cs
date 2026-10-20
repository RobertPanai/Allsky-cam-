using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using AllSky.Host;
using Microsoft.Win32;

namespace AllSky.Tray;

/// <summary>
/// Contesto senza finestre: icona nell'area di notifica con menu, apertura del pannello nel browser,
/// avvio automatico con Windows (chiave HKCU Run) e chiusura ordinata dell'host.
/// </summary>
/// <remarks>
/// Scelta: "Avvia con Windows" registra <c>Environment.ProcessPath</c>. Se l'app è lanciata con
/// <c>dotnet AllSky.dll</c> quel percorso è dotnet.exe, quindi la voce è utile solo per l'exe pubblicato.
/// Le etichette della lingua sono fissate all'avvio: un cambio di lingua in config.json richiede un riavvio.
/// </remarks>
public sealed class TrayContext : ApplicationContext
{
    /// <summary>Chiave di avvio automatica dell'utente corrente.</summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Nome del valore di avvio automatico.</summary>
    private const string RunValueName = "AllSky";

    /// <summary>Tempo massimo concesso all'arresto dell'host prima di uscire comunque.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly AllSkyHostRuntime _runtime;
    private readonly AppPaths _paths;
    private readonly MenuTexts _texts;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _autostartItem;
    private bool _stopped;

    /// <summary>Crea l'icona nell'area di notifica e il relativo menu.</summary>
    /// <param name="runtime">Host già avviato, da arrestare alla chiusura.</param>
    /// <param name="paths">Cartelle e file usati per i log.</param>
    /// <param name="english">True per le etichette in inglese, false per l'italiano.</param>
    /// <param name="openPanelOnStart">True per aprire subito il pannello (prima esecuzione).</param>
    public TrayContext(AllSkyHostRuntime runtime, AppPaths paths, bool english, bool openPanelOnStart)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(paths);

        _runtime = runtime;
        _paths = paths;
        _texts = english ? MenuTexts.English : MenuTexts.Italian;

        var openItem = new ToolStripMenuItem(_texts.OpenPanel);
        openItem.Click += (_, _) => OpenPanel();

        _autostartItem = new ToolStripMenuItem(_texts.StartWithWindows)
        {
            CheckOnClick = true,
            Checked = IsAutostartEnabled(),
        };
        _autostartItem.Click += (_, _) => SetAutostart(_autostartItem.Checked);

        var exitItem = new ToolStripMenuItem(_texts.Exit);
        exitItem.Click += (_, _) => ExitApplication();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(openItem);
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "AllSky",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenPanel();

        // Rete di sicurezza: se l'applicazione si chiude per altri motivi, l'host viene comunque arrestato.
        Application.ApplicationExit += OnApplicationExit;

        if (openPanelOnStart)
        {
            OpenPanel();
        }
    }

    /// <summary>Apre l'indirizzo del pannello nel browser predefinito. In caso di errore mostra l'indirizzo da aprire a mano.</summary>
    private void OpenPanel()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_runtime.Url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            MessageBox.Show(string.Format(_texts.BrowserError, _runtime.Url), "AllSky",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Restituisce true se la voce di avvio automatico esiste già nel registro.</summary>
    private static bool IsAutostartEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(RunValueName) is string;
        }
        catch (Exception)
        {
            // Se il registro non è leggibile la voce non si può verificare: la mostriamo come disattiva.
            return false;
        }
    }

    /// <summary>Scrive o rimuove la voce di avvio automatico. In caso di errore ripristina lo stato della voce e avvisa.</summary>
    /// <param name="enable">True per attivare l'avvio con Windows.</param>
    private void SetAutostart(bool enable)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("La chiave di avvio di Windows non è accessibile.");

            if (enable)
            {
                string exePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Percorso dell'eseguibile non disponibile.");
                key.SetValue(RunValueName, $"\"{exePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            _autostartItem.Checked = IsAutostartEnabled();
            MessageBox.Show(string.Format(_texts.AutostartError, ex.Message), "AllSky",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Chiude l'applicazione dal menu "Esci".</summary>
    private void ExitApplication()
    {
        StopAll();
        ExitThread();
    }

    /// <summary>Gestisce la chiusura di WinForms (se avviene per altri motivi).</summary>
    private void OnApplicationExit(object? sender, EventArgs e)
    {
        StopAll();
    }

    /// <summary>
    /// Arresta l'host (timeout 10 s) e rimuove l'icona dall'area di notifica. È idempotente:
    /// può essere chiamato più volte senza effetti collaterali.
    /// </summary>
    private void StopAll()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;

        // Prima di tutto nascondiamo l'icona, così l'utente vede subito che AllSky si sta chiudendo.
        _notifyIcon.Visible = false;

        StopHost();

        // Dispose esplicito: altrimenti l'icona resta "fantasma" fino al passaggio del mouse.
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    /// <summary>
    /// Ferma e libera l'host. Il lavoro gira su un thread del pool per evitare che le continuazioni
    /// dell'host tentino di tornare sul thread UI, che in questo momento è bloccato in attesa.
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

    /// <summary>Etichette del menu e messaggi dell'icona, in italiano o in inglese.</summary>
    /// <param name="OpenPanel">Voce "Apri pannello".</param>
    /// <param name="StartWithWindows">Voce "Avvia con Windows".</param>
    /// <param name="Exit">Voce "Esci".</param>
    /// <param name="BrowserError">Messaggio con l'indirizzo da aprire a mano; {0} è l'URL.</param>
    /// <param name="AutostartError">Messaggio di errore sull'avvio automatico; {0} è il dettaglio.</param>
    private sealed record MenuTexts(
        string OpenPanel,
        string StartWithWindows,
        string Exit,
        string BrowserError,
        string AutostartError)
    {
        /// <summary>Testi in italiano (lingua predefinita).</summary>
        public static MenuTexts Italian { get; } = new(
            OpenPanel: "Apri pannello",
            StartWithWindows: "Avvia con Windows",
            Exit: "Esci",
            BrowserError: "Impossibile aprire il browser. Apri questo indirizzo a mano: {0}",
            AutostartError: "Impossibile modificare l'avvio automatico con Windows.\n\n{0}");

        /// <summary>Testi in inglese.</summary>
        public static MenuTexts English { get; } = new(
            OpenPanel: "Open panel",
            StartWithWindows: "Start with Windows",
            Exit: "Exit",
            BrowserError: "Could not open the browser. Open this address manually: {0}",
            AutostartError: "Could not change the start-with-Windows setting.\n\n{0}");
    }
}
