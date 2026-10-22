using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AllSky.Core;
using AllSky.Core.Passes;
using AllSky.Core.Tonight;
using AllSky.Desktop.Views.Live;
using AllSky.Engine;

namespace AllSky.Desktop.Views.Tonight;

/// <summary>
/// Vista "Stanotte", la schermata iniziale: immagine del cielo dal vivo, linea temporale da tramonto ad alba, carte
/// "Condizioni", "Prossimi eventi" e "Stato della cattura".
/// </summary>
/// <remarks>
/// <para>Il calcolo della notte (fasi, altezze, glifo lunare), la lettura dello stato e la decodifica del JPEG avvengono
/// in un task in background. Ogni 30 secondi, e a ogni frame prodotto, la vista si aggiorna. Le letture non si
/// sovrappongono: se ne arriva una nuova durante una lettura, si esegue l'ultima alla fine (come <c>LiveView</c>).</para>
/// <para>Meteo e indice di cielo sereno sono predisposti come "non disponibili": arriveranno con P5 e P6. Il transito
/// e l'ultima meteora mostrano un segnaposto finché P4 e P5 non forniscono i dati.</para>
/// <para>Allo scarico la vista ferma il timer, si disiscrive dagli eventi e rilascia l'immagine.</para>
/// </remarks>
public sealed class TonightView : UserControl
{
    /// <summary>Intervallo di aggiornamento periodico.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IAllSkyService _service;
    private readonly DesktopTexts _texts;
    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };

    private readonly Image _image = new();
    private readonly TextBlock _imageStatus = new();
    private readonly TonightTimelineCanvas _timelineCanvas = new();
    private readonly StackPanel _segmentList = new();
    private readonly TextBlock _timelineStatus = new();
    private readonly TextBlock _nowLine = new();

    private readonly MoonGlyphView _moonGlyph = new();
    private readonly TextBlock _moonName = new();
    private readonly TextBlock _sunAltitude = new();
    private readonly TextBlock _moonAltitude = new();
    private readonly TextBlock _skyClarity = new();
    private readonly TextBlock _weather = new();

    private readonly TextBlock _nextChange = new();
    private readonly StackPanel _passRows = new();
    private readonly TextBlock _passHint = new();
    private readonly TextBlock _lastMeteor = new();
    private readonly TextBlock _meteorHint = new();

    private readonly StackPanel _captureRows = new();

    private bool _attached;
    private bool _busy;
    private bool _again;

    /// <summary>Crea la vista con i testi in italiano.</summary>
    /// <param name="service">Servizio dell'Engine, già avviato dall'host.</param>
    public TonightView(IAllSkyService service)
        : this(service, DesktopTexts.Italian)
    {
    }

    /// <summary>Crea la vista con i testi nella lingua scelta. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="service">Servizio dell'Engine.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public TonightView(IAllSkyService service, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(texts);

        _service = service;
        _texts = texts;
        AutomationProperties.SetName(this, texts.TonightTitle);

        _timer.Tick += (_, _) => RefreshAsync();
        Content = BuildLayout();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private UIElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock { Text = _texts.TonightTitle, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(TextBlock.FontSizeProperty, "Size.Title");
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

        UIElement imageArea = BuildImageArea();
        Grid.SetColumn(imageArea, 0);
        body.Children.Add(imageArea);

        var side = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        side.Children.Add(FramePanel(BuildConditions()));
        side.Children.Add(FramePanel(BuildEvents(), top: 12));
        side.Children.Add(FramePanel(BuildCapture(), top: 12));
        Grid.SetColumn(side, 1);
        body.Children.Add(side);

        Grid.SetRow(body, 1);
        root.Children.Add(body);

        UIElement timeline = BuildTimeline();
        Grid.SetRow(timeline, 2);
        root.Children.Add(timeline);

        return root;
    }

    private UIElement BuildImageArea()
    {
        _image.Stretch = System.Windows.Media.Stretch.Uniform;
        _image.Visibility = Visibility.Collapsed;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        AutomationProperties.SetName(_image, _texts.TonightImageName);

        _imageStatus.Text = _texts.TonightNoImage;
        _imageStatus.TextWrapping = TextWrapping.Wrap;
        _imageStatus.TextAlignment = TextAlignment.Center;
        _imageStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _imageStatus.VerticalAlignment = VerticalAlignment.Center;
        _imageStatus.MaxWidth = 420;
        _imageStatus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _imageStatus.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        var viewport = new Grid();
        viewport.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface1");
        viewport.Children.Add(_image);
        viewport.Children.Add(_imageStatus);

        return new Border { Style = PanelStyle(), Child = viewport };
    }

    private UIElement BuildConditions()
    {
        var stack = new StackPanel();
        stack.Children.Add(CreatePanelTitle(_texts.TonightConditionsTitle));

        var moon = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        moon.Children.Add(_moonGlyph);
        _moonName.Margin = new Thickness(12, 0, 0, 0);
        _moonName.VerticalAlignment = VerticalAlignment.Center;
        _moonName.TextWrapping = TextWrapping.Wrap;
        _moonName.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        _moonName.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        moon.Children.Add(_moonName);
        stack.Children.Add(moon);

        stack.Children.Add(Row(_texts.TonightSunAltitude, _sunAltitude));
        stack.Children.Add(Row(_texts.TonightMoonAltitude, _moonAltitude));
        stack.Children.Add(Row(_texts.TonightSkyClarity, _skyClarity));
        stack.Children.Add(Row(_texts.TonightWeather, _weather));
        _skyClarity.Text = _texts.TonightSkyClarityPending;
        _weather.Text = _texts.TonightWeatherPending;
        return stack;
    }

    private UIElement BuildEvents()
    {
        var stack = new StackPanel();
        stack.Children.Add(CreatePanelTitle(_texts.TonightEventsTitle));
        stack.Children.Add(Row(_texts.TonightNextChange, _nextChange));

        // Prossimi passaggi dei satelliti (P4.3): sostituisce il segnaposto del transito.
        var passCaption = new TextBlock { Text = _texts.PassListTitle, Margin = new Thickness(0, 12, 0, 4) };
        passCaption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        passCaption.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        stack.Children.Add(passCaption);
        stack.Children.Add(_passRows);
        stack.Children.Add(Hint(_passHint));

        _lastMeteor.Text = _texts.TonightNoMeteor;
        stack.Children.Add(Row(_texts.TonightLastMeteor, _lastMeteor));
        _meteorHint.Text = _texts.TonightMeteorPending;
        stack.Children.Add(Hint(_meteorHint));
        return stack;
    }

    private UIElement BuildCapture()
    {
        var stack = new StackPanel();
        stack.Children.Add(CreatePanelTitle(_texts.TonightCaptureTitle));
        stack.Children.Add(_captureRows);
        return stack;
    }

    private UIElement BuildTimeline()
    {
        var stack = new StackPanel();
        stack.Children.Add(CreatePanelTitle(_texts.TonightTimelineTitle));

        AutomationProperties.SetName(_timelineCanvas, _texts.TonightTimelineName);
        AutomationProperties.SetHelpText(_timelineCanvas, _texts.TonightCurveName);
        stack.Children.Add(_timelineCanvas);

        // Legenda: il colore non basta da solo, quindi ogni curva ha il suo nome.
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        legend.Children.Add(LegendItem(_texts.TonightSunCurve, "Brush.Sun"));
        legend.Children.Add(LegendItem(_texts.TonightMoonCurve, "Brush.Moon"));
        legend.Children.Add(LegendItem(_texts.TonightHorizon, "Brush.TextTertiary"));
        stack.Children.Add(legend);

        _timelineStatus.TextWrapping = TextWrapping.Wrap;
        _timelineStatus.Margin = new Thickness(0, 8, 0, 0);
        _timelineStatus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _timelineStatus.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        stack.Children.Add(_timelineStatus);

        _nowLine.Margin = new Thickness(0, 4, 0, 0);
        _nowLine.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _nowLine.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        _nowLine.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        stack.Children.Add(_nowLine);

        stack.Children.Add(_segmentList);

        var note = new TextBlock { Text = _texts.TonightGeometricNote, Margin = new Thickness(0, 6, 0, 0) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        note.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        stack.Children.Add(note);

        return FramePanel(stack, top: 12);
    }

    private TextBlock LegendItem(string text, string brushKey)
    {
        var item = new TextBlock { Text = text, Margin = new Thickness(0, 0, 16, 0) };
        item.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        item.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        return item;
    }

    private Border FramePanel(UIElement content, double top = 0)
    {
        return new Border
        {
            Style = PanelStyle(),
            Margin = new Thickness(0, top, 0, 0),
            Child = content,
        };
    }

    private TextBlock CreatePanelTitle(string text)
    {
        var title = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(FrameworkElement.StyleProperty, "PanelTitle");
        return title;
    }

    private Grid Row(string label, TextBlock value)
    {
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 4) };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        caption.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        value.Margin = new Thickness(12, 4, 0, 4);
        value.TextAlignment = TextAlignment.Right;
        value.TextWrapping = TextWrapping.Wrap;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        value.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        value.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        AutomationProperties.SetName(value, label);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(caption, 0);
        Grid.SetColumn(value, 1);
        row.Children.Add(caption);
        row.Children.Add(value);
        return row;
    }

    private TextBlock Hint(TextBlock block)
    {
        block.TextWrapping = TextWrapping.Wrap;
        block.Margin = new Thickness(0, 0, 0, 4);
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        block.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        return block;
    }

    private static Style PanelStyle() => (Style)System.Windows.Application.Current.FindResource("Panel");

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _service.FrameProduced += OnFrameProduced;
        _timer.Start();
        RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _timer.Stop();
        _service.FrameProduced -= OnFrameProduced;
        _image.Source = null;
        _image.Visibility = Visibility.Collapsed;
    }

    // Chiamato sul thread dell'Engine: si torna sul thread dell'interfaccia prima di toccare la vista.
    private void OnFrameProduced(AllSky.Engine.FrameEvent frame)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(RefreshAsync));
    }

    private async void RefreshAsync()
    {
        if (_busy)
        {
            _again = true;
            return;
        }

        _busy = true;
        try
        {
            do
            {
                _again = false;
                TonightLoad load = await Task.Run(Load);
                if (!_attached)
                {
                    break;
                }

                Apply(load);
            }
            while (_again && _attached);
        }
        finally
        {
            _busy = false;
        }
    }

    // Eseguito fuori dal thread dell'interfaccia: calcoli, letture dello stato e decodifica del JPEG.
    private TonightLoad Load()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        try
        {
            StatusDto status = _service.GetStatus();
            AppConfig config = _service.GetConfig();
            TimeZoneInfo tz = TonightSite.ResolveTimeZone(config.Site.Timezone);
            PassPanelData passes = PassPanel(now, tz);

            TonightSnapshot? snapshot = null;
            string? error = null;
            try
            {
                snapshot = TonightCalculator.Compute(now, config.Site.Latitude, config.Site.Longitude, tz);
            }
            catch (ArgumentOutOfRangeException)
            {
                error = _texts.TonightSiteInvalid;
            }

            byte[]? jpeg = _service.GetLatestJpeg(false);
            BitmapSource? image = jpeg is null ? null : FrameDecoder.Decode(jpeg);
            return new TonightLoad(now, tz, snapshot, error, status, image, passes);
        }
        catch (Exception ex)
        {
            // Un errore di lettura non deve chiudere la vista: si mostra nella linea di stato.
            string message = string.Format(_texts.Culture, _texts.TonightLoadError, ex.Message);
            return new TonightLoad(now, TimeZoneInfo.Utc, null, message, null, null, null);
        }
    }

    private void Apply(TonightLoad load)
    {
        ApplyImage(load.Image);
        ApplyCapture(load.Status, load.Now);
        ApplyPasses(load.Passes);

        if (load.Snapshot is not { } snapshot)
        {
            _timelineStatus.Text = load.Error ?? _texts.TonightComputing;
            _timelineStatus.Visibility = Visibility.Visible;
            _nowLine.Text = string.Empty;
            _segmentList.Children.Clear();
            return;
        }

        TonightTimeline timeline = snapshot.Timeline;
        _timelineCanvas.Show(timeline, load.Now, load.Tz, _texts);
        _timelineStatus.Text = timeline.Available ? string.Empty : _texts.TonightNoWindow;
        _timelineStatus.Visibility = timeline.Available ? Visibility.Collapsed : Visibility.Visible;
        _nowLine.Text = NowLine(timeline, load.Now, load.Tz);
        ApplySegments(timeline, load.Tz);

        ApplyConditions(snapshot);
        ApplyEvents(snapshot.Timeline, load.Now, load.Tz);
    }

    private void ApplyImage(BitmapSource? image)
    {
        if (image is null)
        {
            _image.Source = null;
            _image.Visibility = Visibility.Collapsed;
            _imageStatus.Visibility = Visibility.Visible;
            return;
        }

        _image.Source = image;
        _image.Visibility = Visibility.Visible;
        _imageStatus.Visibility = Visibility.Collapsed;
    }

    private void ApplyConditions(TonightSnapshot snapshot)
    {
        CultureInfo culture = _texts.Culture;
        string phase = TonightFormatter.MoonName(snapshot.Moon.Name, _texts);
        string illumination = TonightFormatter.FormatIllumination(snapshot.Moon.IlluminatedFraction, culture);

        _moonGlyph.Show(snapshot.Moon);
        _moonName.Text = phase + " · " + illumination;
        AutomationProperties.SetName(
            _moonGlyph, string.Format(culture, _texts.TonightMoonGlyphName, phase, illumination));
        _sunAltitude.Text = TonightFormatter.FormatAltitude(snapshot.SunAltitudeNowDeg, culture);
        _moonAltitude.Text = TonightFormatter.FormatAltitude(snapshot.MoonAltitudeNowDeg, culture);
    }

    private void ApplyEvents(TonightTimeline timeline, DateTimeOffset now, TimeZoneInfo tz)
    {
        // Il prossimo cambio di fase è calcolato; i passaggi arrivano dalla previsione (P4.3); la meteora arriva con P5.
        TonightUpcoming upcoming = new(timeline.NextChangeAfter(now), null, null);

        _nextChange.Text = upcoming.NextChange is { } change
            ? TonightFormatter.PhaseName(change.To, _texts) + ", " + TonightFormatter.FormatCountdown(now, change.AtUtc, _texts)
            : _texts.NotAvailable;

        if (upcoming.LastMeteorUtc is { } meteor)
        {
            _lastMeteor.Text = TonightFormatter.FormatClock(meteor, tz, _texts.Culture);
            _meteorHint.Visibility = Visibility.Collapsed;
        }
        else
        {
            _lastMeteor.Text = _texts.TonightNoMeteor;
            _meteorHint.Text = _texts.TonightMeteorPending;
            _meteorHint.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Previsione dei passaggi in forma di carta (P4.3). Senza servizio di previsione, un messaggio di spegnimento.</summary>
    private PassPanelData PassPanel(DateTimeOffset now, TimeZoneInfo tz)
    {
        if (_service is not AllSky.Core.Passes.IPassForecastService forecast)
        {
            return new PassPanelData(_texts.PassStatusDisabled, Array.Empty<PassRowData>());
        }

        return PassFormatter.Panel(forecast.GetForecast(), now, tz, _texts);
    }

    private void ApplyPasses(PassPanelData? passes)
    {
        _passRows.Children.Clear();
        if (passes is null)
        {
            _passHint.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (PassRowData row in passes.Rows)
        {
            _passRows.Children.Add(PassRowView(row));
        }

        _passHint.Text = passes.Hint ?? string.Empty;
        _passHint.Visibility = string.IsNullOrEmpty(passes.Hint) ? Visibility.Collapsed : Visibility.Visible;
    }

    private UIElement PassRowView(PassRowData row)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

        var title = new TextBlock { TextWrapping = TextWrapping.Wrap };
        title.Inlines.Add(new System.Windows.Documents.Run(row.Target + " · "));
        title.Inlines.Add(new System.Windows.Documents.Run(row.When) { FontWeight = row.InProgress ? FontWeights.SemiBold : FontWeights.Normal });
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        title.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        stack.Children.Add(title);

        var times = new TextBlock { Text = row.Times, TextWrapping = TextWrapping.Wrap };
        times.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        times.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        stack.Children.Add(times);

        var details = new TextBlock { Text = row.Details, TextWrapping = TextWrapping.Wrap };
        details.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextTertiary");
        details.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        stack.Children.Add(details);

        AutomationProperties.SetName(stack, row.Target + ", " + row.When);
        return stack;
    }

    private void ApplySegments(TonightTimeline timeline, TimeZoneInfo tz)
    {
        _segmentList.Children.Clear();
        foreach (TonightSegment segment in timeline.Segments)
        {
            var line = new TextBlock
            {
                Text = string.Format(
                    _texts.Culture,
                    _texts.TonightSegmentLine,
                    TonightFormatter.PhaseName(segment.Phase, _texts),
                    TonightFormatter.FormatClock(segment.From, tz, _texts.Culture),
                    TonightFormatter.FormatClock(segment.To, tz, _texts.Culture)),
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap,
            };
            line.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            line.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
            line.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
            _segmentList.Children.Add(line);
        }
    }

    private string NowLine(TonightTimeline timeline, DateTimeOffset now, TimeZoneInfo tz)
    {
        if (timeline.ProgressAt(now) is null)
        {
            return _texts.TonightNotInWindow;
        }

        string clock = TonightFormatter.FormatClock(now, tz, _texts.Culture);
        string phase = _texts.TonightPhaseDay;
        foreach (TonightSegment segment in timeline.Segments)
        {
            if (now >= segment.From && now < segment.To)
            {
                phase = TonightFormatter.PhaseName(segment.Phase, _texts);
                break;
            }
        }
        return string.Format(_texts.Culture, _texts.TonightNowMarker, clock + " · " + phase);
    }

    private void ApplyCapture(StatusDto? status, DateTimeOffset now)
    {
        _captureRows.Children.Clear();
        if (status is null)
        {
            return;
        }

        var input = new TonightCaptureInput(
            status.State,
            status.Camera.Connected,
            status.Camera.Name,
            status.LastFrame?.Id,
            status.LastFrame?.Timestamp,
            status.Error);

        foreach (TonightStatusLine line in TonightFormatter.CaptureLines(input, now, _texts))
        {
            var value = new TextBlock { Text = line.Value, TextWrapping = TextWrapping.Wrap };
            value.SetResourceReference(TextBlock.ForegroundProperty, line.Problem ? "Brush.Error" : "Brush.Text");
            value.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
            value.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
            value.FontWeight = line.Problem ? FontWeights.SemiBold : FontWeights.Normal;
            _captureRows.Children.Add(Row(line.Label, value));
        }
    }

    /// <summary>Caricamento in background: stato, notte calcolata e immagine.</summary>
    private sealed record TonightLoad(
        DateTimeOffset Now,
        TimeZoneInfo Tz,
        TonightSnapshot? Snapshot,
        string? Error,
        StatusDto? Status,
        BitmapSource? Image,
        PassPanelData? Passes);
}
