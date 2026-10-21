using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using AllSky.Core;
using AllSky.Host;
using AllSky.Updates;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace AllSky.Desktop;

/// <summary>
/// Icona nell'area di notifica con menu: apri la finestra, apri il pannello nel browser, controlla gli aggiornamenti,
/// avvio automatico con Windows, esci. Con WinForms solo per <see cref="WinForms.NotifyIcon"/>; il resto è WPF.
/// </summary>
/// <remarks>
/// Logica di aggiornamento e avvio automatico replicata da TrayContext del Tray (legacy). Non ci sono altre copie:
/// quando il Tray verrà rimosso, questa sarà l'unica versione.
/// Scelta: "Avvia con Windows" registra <c>Environment.ProcessPath</c>, quindi funziona con l'exe pubblicato.
/// </remarks>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>Chiave di avvio automatica dell'utente corrente.</summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Nome del valore di avvio automatico.</summary>
    private const string RunValueName = "AllSkyBrain";

    /// <summary>Valore di avvio automatico usato prima del cambio di nome. Va rimosso se presente, per non avere un doppio avvio.</summary>
    private const string LegacyRunValueName = "AllSky";

    private readonly AllSkyHostRuntime _runtime;
    private readonly AppPaths _paths;
    private readonly DesktopTexts _texts;
    private readonly Action _showWindow;
    private readonly Action _exit;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly WinForms.ToolStripMenuItem _autostartItem;
    private readonly WinForms.ToolStripMenuItem _updateItem;
    private bool _disposed;

    /// <summary>Crea l'icona e il menu. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="runtime">Host già avviato: serve per l'indirizzo del pannello e per gli aggiornamenti.</param>
    /// <param name="paths">Cartelle e file usati per i log.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    /// <param name="showWindow">Azione che mostra la finestra principale.</param>
    /// <param name="exit">Azione che esce davvero dall'applicazione.</param>
    public TrayIcon(AllSkyHostRuntime runtime, AppPaths paths, DesktopTexts texts, Action showWindow, Action exit)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(exit);

        _runtime = runtime;
        _paths = paths;
        _texts = texts;
        _showWindow = showWindow;
        _exit = exit;

        var openWindowItem = new WinForms.ToolStripMenuItem(texts.OpenWindow);
        openWindowItem.Click += (_, _) => _showWindow();

        var openPanelItem = new WinForms.ToolStripMenuItem(texts.OpenPanel);
        openPanelItem.Click += (_, _) => OpenPanel();

        _autostartItem = new WinForms.ToolStripMenuItem(texts.StartWithWindows)
        {
            CheckOnClick = true,
            Checked = IsAutostartEnabled(),
        };
        _autostartItem.Click += (_, _) => SetAutostart(_autostartItem.Checked);

        _updateItem = new WinForms.ToolStripMenuItem(texts.CheckForUpdates);
        _updateItem.Click += OnCheckForUpdatesClick;

        var exitItem = new WinForms.ToolStripMenuItem(texts.Exit);
        exitItem.Click += (_, _) => _exit();

        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.Add(openWindowItem);
        _menu.Items.Add(openPanelItem);
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(_updateItem);
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = texts.WindowTitle,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => _showWindow();
    }

    /// <summary>Rimuove l'icona dall'area di notifica. È idempotente.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Dispose esplicito: altrimenti l'icona resta "fantasma" fino al passaggio del mouse.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
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
            ShowMessage(string.Format(_texts.BrowserError, _runtime.Url), System.Windows.MessageBoxImage.Warning);
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
            ShowMessage(string.Format(_texts.AutostartError, ex.Message), System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Voce "Controlla aggiornamenti". Il controllo è async: l'interfaccia resta libera e la risposta torna sul thread dell'interfaccia.
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
                    ShowMessage(string.Format(_texts.UpdateUpToDate, status.CurrentVersion), System.Windows.MessageBoxImage.Information);
                    break;
                case UpdateState.Disabled:
                    ShowMessage(_texts.UpdateDisabled, System.Windows.MessageBoxImage.Information);
                    break;
                case UpdateState.Error:
                    ShowMessage(ErrorText(status), System.Windows.MessageBoxImage.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Il servizio non lancia eccezioni: questa rete evita che un errore imprevisto chiuda l'interfaccia. Nel log solo il tipo.
            AppLog.Append(_paths.CrashLog, $"Controllo degli aggiornamenti non riuscito: {ex.GetType().Name}");
            ShowMessage(_texts.UpdateGenericError, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            if (!_disposed)
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
            ShowMessage(ErrorText(status), System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (status.State == UpdateState.ReadyToInstall
            && ChooseWithButtons(string.Format(_texts.UpdateReady, status.AvailableVersion), _texts.Restart, _texts.Later))
        {
            InstallAndRestart();
        }
    }

    /// <summary>Programma l'installazione e chiude l'applicazione. Il programma di aggiornamento attende l'uscita, poi installa e riavvia.</summary>
    private void InstallAndRestart()
    {
        UpdateStatus applied = _runtime.Updates.ApplyLater();
        if (applied.State == UpdateState.Error)
        {
            ShowMessage(ErrorText(applied), System.Windows.MessageBoxImage.Warning);
            return;
        }

        _exit();
    }

    /// <summary>Testo dell'errore di aggiornamento nella lingua scelta. Il codice sceglie il testo: il messaggio del servizio è in italiano.</summary>
    private string ErrorText(UpdateStatus status) =>
        status.ErrorCode == UpdateErrorCodes.Network ? _texts.UpdateNetworkError : _texts.UpdateGenericError;

    /// <summary>Messaggio con il solo pulsante OK, con il titolo dell'app.</summary>
    private void ShowMessage(string text, System.Windows.MessageBoxImage icon)
    {
        System.Windows.MessageBox.Show(text, _texts.WindowTitle, System.Windows.MessageBoxButton.OK, icon);
    }

    /// <summary>
    /// Finestra con due pulsanti dal testo scelto: MessageBox non permette etichette personalizzate.
    /// Restituisce true se si preme il pulsante di conferma.
    /// </summary>
    private bool ChooseWithButtons(string text, string confirmText, string cancelText)
    {
        var confirm = new WinForms.TaskDialogButton(confirmText);
        var cancel = new WinForms.TaskDialogButton(cancelText);
        var page = new WinForms.TaskDialogPage
        {
            Caption = _texts.WindowTitle,
            Text = text,
            Buttons = { confirm, cancel },
        };

        return WinForms.TaskDialog.ShowDialog(page) == confirm;
    }
}
