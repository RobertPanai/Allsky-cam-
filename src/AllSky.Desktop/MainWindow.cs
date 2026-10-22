using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AllSky.Core;
using AllSky.Desktop.Shell;
using AllSky.Desktop.Theme;
using AllSky.Engine;

namespace AllSky.Desktop;

/// <summary>
/// Finestra principale: sfondo nero, stato del servizio, camera e ultimo frame. Si aggiorna con
/// <see cref="IAllSkyService.StatusChanged"/>, che il servizio emette anche a ogni frame archiviato.
/// Chiudere la finestra con la X la nasconde soltanto: la cattura continua e l'app resta nell'area di notifica.
/// </summary>
/// <remarks>
/// Gli eventi del servizio arrivano dai thread di cattura: ogni aggiornamento della UI passa da <c>Dispatcher.BeginInvoke</c>.
/// </remarks>
internal sealed class MainWindow : System.Windows.Window
{
    private readonly IAllSkyService _service;
    private readonly ShellControl _shell;
    private bool _exiting;
    private bool _detached;

    /// <summary>Crea la finestra e legge subito lo stato corrente. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="service">Servizio applicativo dell'host, in-process.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public MainWindow(IAllSkyService service, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(texts);

        _service = service;

        Title = texts.WindowTitle;
        // Colori dal tema (Brush.Background), così la modalità notte rossa li cambia senza ricreare la finestra.
        SetResourceReference(BackgroundProperty, "Brush.Background");
        Width = 1100;
        Height = 700;
        MinWidth = 1100;
        MinHeight = 700;

        // La finestra ospita la shell (barra laterale, viste, barra di stato). Il tema è quello dell'applicazione.
        ThemeManager theme = ((App)System.Windows.Application.Current).Theme;
        _shell = new ShellControl(texts, theme, service);
        Content = _shell;
        PreviewKeyDown += OnPreviewKeyDown;

        Closing += OnClosing;
        service.StatusChanged += OnStatusChanged;

        Render(service.GetStatus());
    }

    /// <summary>
    /// Smette di ascoltare il servizio e consente la chiusura vera della finestra. Va chiamato prima dell'arresto dell'host.
    /// </summary>
    public void PrepareForExit()
    {
        _exiting = true;
        if (_detached)
        {
            return;
        }

        _detached = true;
        _service.StatusChanged -= OnStatusChanged;
    }

    /// <summary>Scorciatoie e frecce della shell. Gira in anteprima (tunnel), così Ctrl+1..6 funziona con qualunque fuoco.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_shell.TryHandleKey(e))
        {
            e.Handled = true;
        }
    }

    /// <summary>Chiusura dalla X: la finestra si nasconde. Durante l'uscita dall'app lascia chiudere davvero.</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    /// <summary>Evento dal servizio (thread di cattura): rimanda l'aggiornamento al thread dell'interfaccia.</summary>
    private void OnStatusChanged(StatusDto status)
    {
        PostToUi(() => Render(status));
    }

    /// <summary>Esegue l'aggiornamento sul thread dell'interfaccia, se la finestra è ancora collegata al servizio.</summary>
    private void PostToUi(Action update)
    {
        if (_detached)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_detached)
            {
                update();
            }
        }));
    }

    /// <summary>Scrive lo stato nella shell. Va chiamato dal thread dell'interfaccia.</summary>
    private void Render(StatusDto status)
    {
        _shell.Update(status);
    }
}
