using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AllSky.Core;

namespace AllSky.Desktop.Views.Nights;

/// <summary>
/// Riproduttore del timelapse di una notte: <see cref="MediaElement"/> con Play, Pausa e Stop, e un messaggio al posto
/// del video quando non c'è un file o quando il lettore non riesce a riprodurlo (di solito per un codec mancante).
/// </summary>
/// <remarks>
/// <para>Il file viene aperto solo mentre la vista lo mostra. <see cref="Release"/> ferma, chiude e stacca la sorgente:
/// la vista lo chiama allo scarico, prima di una rigenerazione (il file verrà sostituito) e quando il timelapse
/// non è pronto. Così nessun handle resta aperto sul file mentre il motore lo riscrive.</para>
/// <para>Il lettore non si avvia da solo: mostra il primo fotogramma e attende il Play.</para>
/// </remarks>
internal sealed class NightsTimelapsePlayer : Grid
{
    private const double PlayerHeight = 280;

    private readonly DesktopTexts _texts;
    private readonly MediaElement _media = new();
    private readonly TextBlock _message = new();
    private readonly Button _playPause = new();
    private readonly Button _stop = new();
    private string? _path;
    private bool _playing;

    /// <summary>Crea il riproduttore vuoto. Va creato sul thread dell'interfaccia.</summary>
    /// <param name="texts">Testi della lingua scelta.</param>
    public NightsTimelapsePlayer(DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        _texts = texts;

        Height = PlayerHeight;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _media.LoadedBehavior = MediaState.Manual;
        _media.UnloadedBehavior = MediaState.Close;
        _media.Stretch = System.Windows.Media.Stretch.Uniform;
        _media.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(_media, texts.NightsPlayerName);
        _media.MediaOpened += OnMediaOpened;
        _media.MediaEnded += OnMediaEnded;
        _media.MediaFailed += OnMediaFailed;

        _message.TextWrapping = TextWrapping.Wrap;
        _message.TextAlignment = TextAlignment.Center;
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.VerticalAlignment = VerticalAlignment.Center;
        _message.MaxWidth = 480;
        _message.Margin = new Thickness(24);
        _message.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _message.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        _message.Visibility = Visibility.Collapsed;

        var mediaArea = new Grid();
        mediaArea.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface1");
        mediaArea.Children.Add(_media);
        mediaArea.Children.Add(_message);
        Grid.SetRow(mediaArea, 0);
        Children.Add(mediaArea);

        _playPause.Content = texts.NightsTimelapsePlay;
        _playPause.MinWidth = 96;
        _playPause.MinHeight = 32;
        _playPause.Margin = new Thickness(0, 0, 8, 0);
        _playPause.Click += (_, _) => TogglePlayback();

        _stop.Content = texts.NightsTimelapseStop;
        _stop.MinWidth = 72;
        _stop.MinHeight = 32;
        _stop.Click += (_, _) => StopPlayback();

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0),
        };
        controls.Children.Add(_playPause);
        controls.Children.Add(_stop);
        Grid.SetRow(controls, 1);
        Children.Add(controls);

        UpdateButtons();
    }

    /// <summary>Vero se un file è caricato nel lettore.</summary>
    public bool HasSource => _path is not null;

    /// <summary>
    /// Carica il file del timelapse. Non fa nulla se è già caricato, così una visualizzazione in corso non riparte.
    /// </summary>
    /// <param name="path">Percorso assoluto del file MP4.</param>
    public void Show(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (string.Equals(_path, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Release();
        _path = path;
        _media.Source = new Uri(path, UriKind.Absolute);
        _media.Visibility = Visibility.Visible;
        _message.Visibility = Visibility.Collapsed;
        UpdateButtons();
    }

    /// <summary>Rilascia il file e mostra un messaggio al posto del video.</summary>
    /// <param name="text">Messaggio già nella lingua scelta.</param>
    public void ShowPlaceholder(string text)
    {
        Release();
        ShowMessage(text, null);
    }

    /// <summary>Ferma la riproduzione, chiude il file e stacca la sorgente. Ripetibile.</summary>
    public void Release()
    {
        _playing = false;
        if (_path is not null)
        {
            _media.Stop();
            _media.Close();
            _media.Source = null;
            _path = null;
        }

        _media.Visibility = Visibility.Collapsed;
        UpdateButtons();
    }

    private void TogglePlayback()
    {
        if (_path is null)
        {
            return;
        }

        if (_playing)
        {
            _media.Pause();
            _playing = false;
        }
        else
        {
            _media.Play();
            _playing = true;
        }

        UpdateButtons();
    }

    private void StopPlayback()
    {
        if (_path is null)
        {
            return;
        }

        _media.Stop();
        _media.Position = TimeSpan.Zero;
        _playing = false;
        UpdateButtons();
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        // Play seguito da Pause mostra il primo fotogramma senza avviare la riproduzione.
        if (!_playing && _path is not null)
        {
            _media.Play();
            _media.Pause();
        }
    }

    private void OnMediaEnded(object sender, RoutedEventArgs e)
    {
        _media.Stop();
        _media.Position = TimeSpan.Zero;
        _playing = false;
        UpdateButtons();
    }

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        // Il lettore non riesce ad aprire il file: si rilascia la sorgente e si spiega il problema.
        Release();
        ShowMessage(_texts.NightsPlaybackError, e.ErrorException?.Message);
    }

    private void ShowMessage(string text, string? detail)
    {
        _message.Text = text;
        _message.ToolTip = detail;
        _message.Visibility = Visibility.Visible;
    }

    private void UpdateButtons()
    {
        bool hasSource = _path is not null;
        _playPause.IsEnabled = hasSource;
        _stop.IsEnabled = hasSource;
        _playPause.Content = _playing ? _texts.NightsTimelapsePause : _texts.NightsTimelapsePlay;
    }
}
