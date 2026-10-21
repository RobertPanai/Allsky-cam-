using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AllSky.Core;
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
    private readonly DesktopTexts _texts;
    private readonly TextBlock _statusText;
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
        _texts = texts;

        Title = texts.WindowTitle;
        Background = Brushes.Black;
        Width = 1100;
        Height = 700;
        MinWidth = 1100;
        MinHeight = 700;

        _statusText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(32),
        };
        Content = _statusText;

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

    /// <summary>Scrive lo stato nella finestra. Va chiamato dal thread dell'interfaccia.</summary>
    private void Render(StatusDto status)
    {
        var lines = new List<string>
        {
            $"{_texts.StateLabel}: {_texts.StateName(status.State)}",
            $"{_texts.CameraLabel}: {CameraText(status.Camera)}",
            $"{_texts.LastFrameLabel}: {_texts.FormatLastFrame(status.LastFrame?.Id, status.LastFrame?.Timestamp)}",
        };

        if (!string.IsNullOrWhiteSpace(status.Error))
        {
            lines.Add($"{_texts.ErrorLabel}: {status.Error}");
        }

        _statusText.Text = string.Join(Environment.NewLine, lines);
    }

    /// <summary>Nome della camera, o il testo "non collegata" se la sorgente non è connessa.</summary>
    private string CameraText(CameraStatusDto camera)
    {
        if (!camera.Connected)
        {
            return _texts.CameraNotConnected;
        }

        return string.IsNullOrWhiteSpace(camera.Name) ? camera.Id : camera.Name;
    }
}
