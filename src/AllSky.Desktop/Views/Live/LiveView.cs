using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AllSky.Core;
using AllSky.Core.Live;
using AllSky.Engine;

namespace AllSky.Desktop.Views.Live;

/// <summary>
/// Vista "Live": l'immagine corrente del cielo, grande, con zoom, trascinamento, versione grezza e pannello dati del frame.
/// </summary>
/// <remarks>
/// <para>Si aggiorna su <see cref="IAllSkyService.FrameProduced"/>. L'evento arriva dal thread dell'Engine: la vista
/// lo riporta sul thread UI, poi legge il JPEG e lo stato in un task in background e decodifica lì.</para>
/// <para>La sottoscrizione all'evento è attiva solo tra <see cref="FrameworkElement.Loaded"/> e
/// <see cref="FrameworkElement.Unloaded"/>; allo scarico l'immagine viene rilasciata. Se più aggiornamenti arrivano
/// durante una lettura, se ne esegue uno solo alla fine.</para>
/// <para>Overlay (griglia alt/az, punti cardinali, tracce satelliti) sono predisposti ma disabilitati fino alla
/// calibrazione. Logica pura in <see cref="ZoomPan"/> e <see cref="LiveFrameDataFormatter"/>.</para>
/// </remarks>
public sealed class LiveView : UserControl
{
    private readonly IAllSkyService _service;
    private readonly DesktopTexts _texts;
    private readonly Grid _viewport = new() { ClipToBounds = true };
    private readonly Image _image = new();
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _translate = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _zoomLabel = new();
    private readonly CheckBox _rawToggle = new();
    private readonly Dictionary<FrameDataField, TextBlock> _dataValues = new();

    private ZoomPanState _zoomPan = ZoomPan.Reset;
    private double _imageWidth;
    private double _imageHeight;
    private double _contentWidth;
    private double _contentHeight;
    private Point? _dragLast;
    private bool _attached;
    private bool _busy;
    private bool _again;

    /// <summary>Crea la vista. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="service">Servizio dell'Engine, già avviato o da avviare.</param>
    /// <param name="texts">Testi nella lingua scelta; predefinito: italiano.</param>
    public LiveView(IAllSkyService service, DesktopTexts? texts = null)
    {
        ArgumentNullException.ThrowIfNull(service);

        _service = service;
        _texts = texts ?? DesktopTexts.Italian;
        AutomationProperties.SetName(this, _texts.NavLive);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildToolbar());

        var body = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        UIElement imageArea = BuildImageArea();
        Grid.SetColumn(imageArea, 0);
        body.Children.Add(imageArea);
        var side = BuildSidePanel();
        Grid.SetColumn(side, 1);
        body.Children.Add(side);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        Content = root;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private UIElement BuildToolbar()
    {
        _rawToggle.Content = _texts.LiveRaw;
        _rawToggle.ToolTip = _texts.LiveRawTooltip;
        _rawToggle.VerticalAlignment = VerticalAlignment.Center;
        _rawToggle.Margin = new Thickness(0, 0, 16, 0);
        _rawToggle.Checked += (_, _) => OnRawChanged();
        _rawToggle.Unchecked += (_, _) => OnRawChanged();

        Button zoomOut = CreateButton("−", _texts.LiveZoomOut, () => ZoomBy(1.0 / ZoomPan.ButtonStep));
        Button zoomIn = CreateButton("+", _texts.LiveZoomIn, () => ZoomBy(ZoomPan.ButtonStep));
        Button reset = CreateButton(_texts.LiveZoomReset, _texts.LiveZoomReset, ResetView);

        _zoomLabel.Text = "100%";
        _zoomLabel.MinWidth = 56;
        _zoomLabel.TextAlignment = TextAlignment.Center;
        _zoomLabel.VerticalAlignment = VerticalAlignment.Center;
        _zoomLabel.Margin = new Thickness(8, 0, 8, 0);
        _zoomLabel.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        _zoomLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        AutomationProperties.SetName(_zoomLabel, _texts.LiveZoomReset);

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Children.Add(_rawToggle);
        bar.Children.Add(zoomOut);
        bar.Children.Add(_zoomLabel);
        bar.Children.Add(zoomIn);
        bar.Children.Add(reset);
        return bar;
    }

    private UIElement BuildImageArea()
    {
        _image.Stretch = System.Windows.Media.Stretch.Fill;
        _image.HorizontalAlignment = HorizontalAlignment.Left;
        _image.VerticalAlignment = VerticalAlignment.Top;
        _image.Visibility = Visibility.Collapsed;
        _image.RenderTransform = new TransformGroup { Children = { _scale, _translate } };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        AutomationProperties.SetName(_image, _texts.LiveImageName);

        _status.Text = _texts.EmptyLive;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.TextAlignment = TextAlignment.Center;
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(24);
        _status.MaxWidth = 480;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _status.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        _viewport.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface1");
        _viewport.ToolTip = _texts.LiveZoomHint;
        _viewport.Children.Add(_image);
        _viewport.Children.Add(_status);
        _viewport.SizeChanged += (_, _) => ApplyTransform();
        _viewport.MouseWheel += OnMouseWheel;
        _viewport.MouseLeftButtonDown += OnMouseDown;
        _viewport.MouseMove += OnMouseMove;
        _viewport.MouseLeftButtonUp += (_, _) => EndDrag();
        _viewport.LostMouseCapture += (_, _) => _dragLast = null;

        var frame = new Border
        {
            Style = PanelStyle(),
            Padding = new Thickness(0),
            Child = _viewport,
        };
        return frame;
    }

    private UIElement BuildSidePanel()
    {
        var stack = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };

        var dataStack = new StackPanel();
        dataStack.Children.Add(CreatePanelTitle(_texts.LiveDataTitle));
        foreach (FrameDataItem item in LiveFrameDataFormatter.Format(LiveFrameData.Empty, _texts))
        {
            dataStack.Children.Add(CreateDataRow(item, out TextBlock value));
            _dataValues[item.Field] = value;
        }
        stack.Children.Add(new Border { Style = PanelStyle(), Child = dataStack });

        var overlayStack = new StackPanel();
        overlayStack.Children.Add(CreatePanelTitle(_texts.LiveOverlaysTitle));
        overlayStack.Children.Add(CreateOverlayToggle(_texts.LiveOverlayGrid));
        overlayStack.Children.Add(CreateOverlayToggle(_texts.LiveOverlayCardinal));
        overlayStack.Children.Add(CreateOverlayToggle(_texts.LiveOverlaySatellites));
        var overlayPanel = new Border { Style = PanelStyle(), Margin = new Thickness(0, 16, 0, 0), Child = overlayStack };
        stack.Children.Add(overlayPanel);

        return stack;
    }

    private Button CreateButton(string content, string name, Action onClick)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = name,
            MinWidth = 32,
            MinHeight = 32,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => onClick();
        return button;
    }

    private TextBlock CreatePanelTitle(string text)
    {
        var title = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(FrameworkElement.StyleProperty, "PanelTitle");
        return title;
    }

    private Grid CreateDataRow(FrameDataItem item, out TextBlock value)
    {
        var label = new TextBlock { Text = item.Label, Margin = new Thickness(0, 4, 0, 4) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        label.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        value = new TextBlock
        {
            Text = item.Value,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(12, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        value.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        value.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        value.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        AutomationProperties.SetName(value, item.Label);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(label, 0);
        Grid.SetColumn(value, 1);
        row.Children.Add(label);
        row.Children.Add(value);
        return row;
    }

    private CheckBox CreateOverlayToggle(string text)
    {
        // Predisposizione: disabilitato fino alla calibrazione. ShowOnDisabled mostra la spiegazione anche così.
        var toggle = new CheckBox
        {
            Content = text,
            IsEnabled = false,
            ToolTip = _texts.LiveOverlayPending,
            Margin = new Thickness(0, 4, 0, 4),
        };
        ToolTipService.SetShowOnDisabled(toggle, true);
        AutomationProperties.SetName(toggle, text);
        AutomationProperties.SetHelpText(toggle, _texts.LiveOverlayPending);
        return toggle;
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
        RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _service.FrameProduced -= OnFrameProduced;
        // Rilascia l'immagine: lo stato di zoom resta, il prossimo caricamento mostrerà il frame aggiornato.
        _image.Source = null;
        _image.Visibility = Visibility.Collapsed;
    }

    // Chiamato sul thread dell'Engine: si torna sul thread UI prima di toccare la vista.
    private void OnFrameProduced(FrameEvent frame)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(RefreshAsync));
    }

    private void OnRawChanged() => RefreshAsync();

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
                bool raw = _rawToggle.IsChecked == true;
                LiveLoad load = await Task.Run(() => Load(raw));
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

    // Eseguito fuori dal thread UI. Nessun oggetto WPF creato qui tranne il BitmapSource congelato.
    private LiveLoad Load(bool raw)
    {
        try
        {
            StatusDto status = _service.GetStatus();
            byte[]? jpeg = _service.GetLatestJpeg(raw);
            BitmapSource? image = jpeg is null ? null : FrameDecoder.Decode(jpeg);
            return new LiveLoad(image, ToFrameData(status.LastFrame), null);
        }
        catch (Exception ex)
        {
            // Un errore di lettura o decodifica non deve chiudere la vista: si mostra nel riquadro.
            return new LiveLoad(null, LiveFrameData.Empty, ex.Message);
        }
    }

    private void Apply(LiveLoad load)
    {
        if (load.Image is { } image)
        {
            _image.Source = image;
            _imageWidth = image.PixelWidth;
            _imageHeight = image.PixelHeight;
            _image.Visibility = Visibility.Visible;
            _status.Visibility = Visibility.Collapsed;
        }
        else
        {
            _image.Source = null;
            _image.Visibility = Visibility.Collapsed;
            _imageWidth = 0;
            _imageHeight = 0;
            _status.Text = load.Error is null
                ? _texts.EmptyLive
                : string.Format(_texts.Culture, _texts.LiveLoadError, load.Error);
            _status.Visibility = Visibility.Visible;
        }

        ApplyTransform();

        foreach (FrameDataItem item in LiveFrameDataFormatter.Format(load.Data, _texts))
        {
            TextBlock value = _dataValues[item.Field];
            value.Text = item.Value;
            value.ToolTip = item.Tooltip;
        }
    }

    private static LiveFrameData ToFrameData(LastFrameDto? last)
    {
        if (last is null)
        {
            return LiveFrameData.Empty;
        }

        return new LiveFrameData(last.Id, last.Timestamp, last.ExposureMs, last.Gain, last.MedianAdu, last.SensorTempC);
    }

    private void ApplyTransform()
    {
        double viewWidth = _viewport.ActualWidth;
        double viewHeight = _viewport.ActualHeight;
        (_contentWidth, _contentHeight) = ZoomPan.FitContent(_imageWidth, _imageHeight, viewWidth, viewHeight);
        _zoomPan = ZoomPan.Clamp(_zoomPan, viewWidth, viewHeight, _contentWidth, _contentHeight);

        _image.Width = _contentWidth;
        _image.Height = _contentHeight;
        _scale.ScaleX = _zoomPan.Zoom;
        _scale.ScaleY = _zoomPan.Zoom;
        _translate.X = _zoomPan.OffsetX;
        _translate.Y = _zoomPan.OffsetY;
        _zoomLabel.Text = Math.Round(_zoomPan.Zoom * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        _viewport.Cursor = _zoomPan.Zoom > ZoomPan.MinZoom ? Cursors.Hand : Cursors.Arrow;
    }

    private void ZoomBy(double factor)
    {
        _zoomPan = ZoomPan.ZoomAt(
            _zoomPan, factor, _viewport.ActualWidth / 2, _viewport.ActualHeight / 2,
            _viewport.ActualWidth, _viewport.ActualHeight, _contentWidth, _contentHeight);
        ApplyTransform();
    }

    private void ResetView()
    {
        _zoomPan = ZoomPan.Reset;
        ApplyTransform();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_image.Source is null)
        {
            return;
        }

        Point point = e.GetPosition(_viewport);
        _zoomPan = ZoomPan.ZoomAt(
            _zoomPan, ZoomPan.WheelFactor(e.Delta), point.X, point.Y,
            _viewport.ActualWidth, _viewport.ActualHeight, _contentWidth, _contentHeight);
        ApplyTransform();
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ResetView();
            e.Handled = true;
            return;
        }

        _dragLast = e.GetPosition(_viewport);
        _viewport.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragLast is not { } last || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point point = e.GetPosition(_viewport);
        _zoomPan = ZoomPan.PanBy(
            _zoomPan, point.X - last.X, point.Y - last.Y,
            _viewport.ActualWidth, _viewport.ActualHeight, _contentWidth, _contentHeight);
        _dragLast = point;
        ApplyTransform();
    }

    private void EndDrag()
    {
        _dragLast = null;
        _viewport.ReleaseMouseCapture();
    }

    /// <summary>Esito di un caricamento in background.</summary>
    private sealed record LiveLoad(BitmapSource? Image, LiveFrameData Data, string? Error);
}
