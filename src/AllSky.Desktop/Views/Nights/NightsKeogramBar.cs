using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AllSky.Core;
using AllSky.Core.Nights;

namespace AllSky.Desktop.Views.Nights;

/// <summary>
/// Keogram della notte usato come barra di scorrimento temporale. Il clic o il trascinamento segnala una frazione da
/// 0 a 1; le frecce ← e → chiedono di passare al frame precedente o successivo. La barra non sa nulla dei frame: la
/// vista traduce la frazione in indice con <see cref="KeogramScrubber"/>.
/// </summary>
/// <remarks>
/// Il cursore è un rettangolo sottile su un <see cref="Canvas"/> sovrapposto all'immagine, posizionato in pixel.
/// Il riquadro di fuoco da tastiera è un bordo ciano, perché i pannelli non sono controlli e non ricevono lo stile
/// <c>Focus.Ring</c>.
/// </remarks>
internal sealed class NightsKeogramBar : Grid
{
    private const double BarHeight = 96;
    private const double CursorWidth = 2;

    private readonly Image _image = new();
    private readonly Canvas _overlay = new();
    private readonly Rectangle _cursor = new();
    private readonly TextBlock _message = new();
    private readonly Border _focusRing = new();
    private double? _fraction;
    private bool _dragging;

    /// <summary>Emesso al clic e durante il trascinamento, con una frazione da 0 a 1 della larghezza.</summary>
    public event Action<double>? FractionRequested;

    /// <summary>Emesso con le frecce quando la barra ha il fuoco: -1 indietro, +1 avanti.</summary>
    public event Action<int>? StepRequested;

    /// <summary>Crea la barra. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="texts">Testi della lingua scelta.</param>
    public NightsKeogramBar(DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        Height = BarHeight;
        Focusable = true;
        Cursor = Cursors.Hand;
        AutomationProperties.SetName(this, texts.NightsKeogramName);
        AutomationProperties.SetHelpText(this, texts.NightsKeogramHint);
        ToolTip = texts.NightsKeogramHint;
        SetResourceReference(Panel.BackgroundProperty, "Brush.Surface1");

        _image.Stretch = System.Windows.Media.Stretch.Fill;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _image.Visibility = Visibility.Collapsed;

        _message.TextWrapping = TextWrapping.Wrap;
        _message.TextAlignment = TextAlignment.Center;
        _message.Margin = new Thickness(12);
        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.VerticalAlignment = VerticalAlignment.Center;
        _message.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _message.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        _message.Visibility = Visibility.Collapsed;

        _cursor.Width = CursorWidth;
        _cursor.Height = BarHeight;
        _cursor.SetResourceReference(Shape.FillProperty, "Brush.Accent");
        _cursor.IsHitTestVisible = false;
        _cursor.Visibility = Visibility.Collapsed;
        _overlay.Children.Add(_cursor);
        _overlay.IsHitTestVisible = false;

        _focusRing.BorderThickness = new Thickness(2);
        _focusRing.CornerRadius = new CornerRadius(6);
        _focusRing.IsHitTestVisible = false;
        _focusRing.Visibility = Visibility.Collapsed;
        _focusRing.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");

        Children.Add(_image);
        Children.Add(_message);
        Children.Add(_overlay);
        Children.Add(_focusRing);

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        LostMouseCapture += (_, _) => _dragging = false;
        KeyDown += OnKeyDown;
        SizeChanged += (_, _) => PositionCursor();
        IsKeyboardFocusWithinChanged += (_, _) =>
            _focusRing.Visibility = IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Mostra il keogram, o rilascia l'immagine precedente con <c>null</c>.</summary>
    /// <param name="image">Immagine congelata, o null.</param>
    public void SetImage(BitmapSource? image)
    {
        _image.Source = image;
        _image.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Testo da mostrare al posto dell'immagine, o null per nasconderlo.</summary>
    /// <param name="text">Messaggio di stato.</param>
    public void SetMessage(string? text)
    {
        _message.Text = text ?? string.Empty;
        _message.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Posiziona il cursore su una frazione da 0 a 1, o lo nasconde con <c>null</c>.</summary>
    /// <param name="fraction">Frazione della larghezza.</param>
    public void SetCursor(double? fraction)
    {
        _fraction = fraction is { } f && double.IsFinite(f) ? Math.Clamp(f, 0, 1) : null;
        PositionCursor();
    }

    private void PositionCursor()
    {
        if (_fraction is not { } fraction || ActualWidth <= 0)
        {
            _cursor.Visibility = Visibility.Collapsed;
            return;
        }

        // Math.Clamp lancia se min > max: con una barra più stretta del cursore il massimo può essere negativo.
        double left = fraction * ActualWidth - CursorWidth / 2;
        Canvas.SetLeft(_cursor, Math.Min(Math.Max(left, 0), Math.Max(0, ActualWidth - CursorWidth)));
        Canvas.SetTop(_cursor, 0);
        _cursor.Visibility = Visibility.Visible;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        _dragging = true;
        CaptureMouse();
        Report(e.GetPosition(this).X);
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Report(e.GetPosition(this).X);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        int step = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            _ => 0,
        };

        if (step == 0)
        {
            return;
        }

        StepRequested?.Invoke(step);
        e.Handled = true;
    }

    private void Report(double x)
    {
        FractionRequested?.Invoke(KeogramScrubber.FractionAt(x, ActualWidth));
    }
}
