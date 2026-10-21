using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using AllSky.Host;
using AllSky.Updates;
using Microsoft.Win32;

namespace AllSky.Tray;

/// <summary>
/// Contesto senza finestre: icona nell'area di notifica con menu, apertura del pannello nel browser,
/// avvio automatico con Windows (chiave HKCU Run) e chiusura ordinata dell'host.
/// </summary>
/// <remarks>
/// Scelta: "Avvia con Windows" registra <c>Environment.ProcessPath</c>. Se l'app è lanciata con
/// <c>dotnet AllSkyBrain.dll</c> quel percorso è dotnet.exe, quindi la voce è utile solo per l'exe pubblicato.
/// Le etichette della lingua sono fissate all'avvio: un cambio di lingua in config.json richiede un riavvio.
/// </remarks>
public sealed class TrayContext : ApplicationContext
{
    /// <summary>Chiave di avvio automatica dell'utente corrente.</summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Nome del valore di avvio automatico.</summary>
    private const string RunValueName = "AllSkyBrain";

    /// <summary>Nome del valore di avvio automatico usato prima del cambio di nome in "AllSky Brain". Va rimosso se presente.</summary>
    private const string LegacyRunValueName = "AllSky";

    /// <summary>Tempo massimo concesso all'arresto dell'host prima di uscire comunque.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly AllSkyHostRuntime _runtime;
    private readonly AppPaths _paths;
    private readonly MenuTexts _texts;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _updateItem;
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

        _updateItem = new ToolStripMenuItem(_texts.CheckForUpdates);
        _updateItem.Click += OnCheckForUpdatesClick;

        var exitItem = new ToolStripMenuItem(_texts.Exit);
        exitItem.Click += (_, _) => ExitApplication();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(openItem);
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(_updateItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "AllSky Brain",
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
            MessageBox.Show(string.Format(_texts.BrowserError, _runtime.Url), "AllSky Brain",
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

            // Voce creata dalle versioni col vecchio nome: la rimuoviamo, così non resta un doppio avvio.
            key.DeleteValue(LegacyRunValueName, throwOnMissingValue: false);

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
            MessageBox.Show(string.Format(_texts.AutostartError, ex.Message), "AllSky Brain",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Voce "Controlla aggiornamenti". Il controllo è async: la UI resta libera e la risposta torna sul thread dell'interfaccia.
    /// Un aggiornamento si installa solo se l'utente sceglie "Riavvia per aggiornare".
    /// </summary>
    private async void OnCheckForUpdatesClick(object? sender, EventArgs e)
    {
        _updateItem.Enabled = false;
        try
        {
            UpdateStatus status = await _runtime.Updates.CheckNowAsync();
            switch (status.State)
            {
                case UpdateState.Available:
                    if (ChooseWithButtons(string.Format(_texts.UpdateAvailable, status.AvailableVersion), _texts.Download, _texts.Later))
                    {
                        await DownloadUpdateAsync();
                    }

                    break;
                case UpdateState.UpToDate:
                    ShowMessage(string.Format(_texts.UpdateUpToDate, status.CurrentVersion), MessageBoxIcon.Information);
                    break;
                case UpdateState.Disabled:
                    ShowMessage(_texts.UpdateDisabled, MessageBoxIcon.Information);
                    break;
                case UpdateState.Error:
                    ShowMessage(ErrorText(status), MessageBoxIcon.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Il servizio non lancia eccezioni: questa rete evita che un errore imprevisto chiuda la UI. Nel log solo il tipo.
            AppLog.Append(_paths.CrashLog, $"Controllo degli aggiornamenti non riuscito: {ex.GetType().Name}");
            ShowMessage(_texts.UpdateGenericError, MessageBoxIcon.Error);
        }
        finally
        {
            if (!_stopped)
            {
                _updateItem.Enabled = true;
            }
        }
    }

    /// <summary>Scarica l'aggiornamento e, a download completato, chiede se installarlo subito.</summary>
    private async Task DownloadUpdateAsync()
    {
        UpdateStatus status = await _runtime.Updates.DownloadAsync();
        if (status.State == UpdateState.Error)
        {
            ShowMessage(ErrorText(status), MessageBoxIcon.Warning);
            return;
        }

        if (status.State == UpdateState.ReadyToInstall
            && ChooseWithButtons(string.Format(_texts.UpdateReady, status.AvailableVersion), _texts.Restart, _texts.Later))
        {
            InstallAndRestart();
        }
    }

    /// <summary>
    /// Programma l'installazione e chiude AllSky. Il programma di aggiornamento attende l'uscita, poi installa e riavvia.
    /// </summary>
    private void InstallAndRestart()
    {
        UpdateStatus applied = _runtime.Updates.ApplyLater();
        if (applied.State == UpdateState.Error)
        {
            ShowMessage(ErrorText(applied), MessageBoxIcon.Warning);
            return;
        }

        ExitApplication();
    }

    /// <summary>Testo dell'errore di aggiornamento nella lingua del menu. Il codice sceglie il testo: il messaggio del servizio è in italiano.</summary>
    private string ErrorText(UpdateStatus status) =>
        status.ErrorCode == UpdateErrorCodes.Network ? _texts.UpdateNetworkError : _texts.UpdateGenericError;

    private static void ShowMessage(string text, MessageBoxIcon icon) =>
        MessageBox.Show(text, "AllSky Brain", MessageBoxButtons.OK, icon);

    /// <summary>
    /// Finestra con due pulsanti dal testo scelto: MessageBox non permette etichette personalizzate.
    /// Restituisce true se si preme il pulsante di conferma.
    /// </summary>
    private static bool ChooseWithButtons(string text, string confirmText, string cancelText)
    {
        var confirm = new TaskDialogButton(confirmText);
        var cancel = new TaskDialogButton(cancelText);
        var page = new TaskDialogPage
        {
            Caption = "AllSky Brain",
            Text = text,
            Buttons = { confirm, cancel },
        };

        return TaskDialog.ShowDialog(page) == confirm;
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
    /// <param name="CheckForUpdates">Voce "Controlla aggiornamenti".</param>
    /// <param name="UpdateUpToDate">Messaggio "AllSky Brain è aggiornato"; {0} è la versione installata.</param>
    /// <param name="UpdateAvailable">Domanda con la versione disponibile; {0} è la versione.</param>
    /// <param name="UpdateReady">Domanda di riavvio dopo il download; {0} è la versione.</param>
    /// <param name="UpdateDisabled">Messaggio per le installazioni non aggiornabili.</param>
    /// <param name="UpdateNetworkError">Errore di rete durante il controllo o il download.</param>
    /// <param name="UpdateGenericError">Errore di aggiornamento non classificato.</param>
    /// <param name="Download">Pulsante "Scarica".</param>
    /// <param name="Later">Pulsante "Più tardi".</param>
    /// <param name="Restart">Pulsante "Riavvia per aggiornare".</param>
    private sealed record MenuTexts(
        string OpenPanel,
        string StartWithWindows,
        string Exit,
        string BrowserError,
        string AutostartError,
        string CheckForUpdates,
        string UpdateUpToDate,
        string UpdateAvailable,
        string UpdateReady,
        string UpdateDisabled,
        string UpdateNetworkError,
        string UpdateGenericError,
        string Download,
        string Later,
        string Restart)
    {
        /// <summary>Testi in italiano (lingua predefinita).</summary>
        public static MenuTexts Italian { get; } = new(
            OpenPanel: "Apri pannello",
            StartWithWindows: "Avvia con Windows",
            Exit: "Esci",
            BrowserError: "Impossibile aprire il browser. Apri questo indirizzo a mano: {0}",
            AutostartError: "Impossibile modificare l'avvio automatico con Windows.\n\n{0}",
            CheckForUpdates: "Controlla aggiornamenti",
            UpdateUpToDate: "AllSky Brain è aggiornato (versione {0}).",
            UpdateAvailable: "È disponibile la versione {0} di AllSky Brain. Scaricarla ora?",
            UpdateReady: "La versione {0} è pronta. Riavviare AllSky Brain per installarla?",
            UpdateDisabled: "Gli aggiornamenti automatici non sono disponibili in questa installazione.",
            UpdateNetworkError: "Impossibile contattare il server degli aggiornamenti. Controllare la connessione e riprovare.",
            UpdateGenericError: "Aggiornamento non riuscito. Riprovare più tardi.",
            Download: "Scarica",
            Later: "Più tardi",
            Restart: "Riavvia per aggiornare");

        /// <summary>Testi in inglese.</summary>
        public static MenuTexts English { get; } = new(
            OpenPanel: "Open panel",
            StartWithWindows: "Start with Windows",
            Exit: "Exit",
            BrowserError: "Could not open the browser. Open this address manually: {0}",
            AutostartError: "Could not change the start-with-Windows setting.\n\n{0}",
            CheckForUpdates: "Check for updates",
            UpdateUpToDate: "AllSky Brain is up to date (version {0}).",
            UpdateAvailable: "Version {0} of AllSky Brain is available. Download it now?",
            UpdateReady: "Version {0} is ready. Restart AllSky Brain to install it?",
            UpdateDisabled: "Automatic updates are not available in this installation.",
            UpdateNetworkError: "Could not reach the update server. Check your connection and try again.",
            UpdateGenericError: "Update failed. Try again later.",
            Download: "Download",
            Later: "Later",
            Restart: "Restart to update");
    }
}
