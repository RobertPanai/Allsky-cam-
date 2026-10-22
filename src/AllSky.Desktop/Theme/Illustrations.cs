using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AllSky.Core;

namespace AllSky.Desktop.Theme;

/// <summary>Stati vuoti che hanno una illustrazione (P1.10).</summary>
public enum IllustrationKind
{
    /// <summary>Nessuna notte ancora da mostrare: il sole sopra l'orizzonte.</summary>
    NoNight,

    /// <summary>Nessun evento rilevato: un'orbita tratteggiata vuota.</summary>
    NoEvents,

    /// <summary>Camera non collegata: un corpo macchina con il collegamento tagliato.</summary>
    CameraNotConnected,

    /// <summary>YouTube non collegato: uno schermo con il collegamento interrotto.</summary>
    YouTubeNotConnected,
}

/// <summary>
/// Illustrazioni vettoriali originali per gli stati vuoti, disegnate con Path e Geometry in una griglia 160×120.
/// Il tratto usa i ruoli del tema (<c>Brush.*</c>) tramite <see cref="FrameworkElement.SetResourceReference"/>, quindi
/// si adatta alla modalità notte rossa e alla luminosità senza essere ricreato.
/// </summary>
/// <remarks>
/// Nessuna immagine di terzi: ogni forma è scritta qui. Le geometrie sono stringhe di percorso SVG-like (Geometry.Parse).
/// Il tratto è di 1.5–2.5 unità con estremità tonde, in linea con il set di icone (§5.3).
/// Il testo sotto il disegno viene da <see cref="DesktopTexts"/> (prefisso Illus).
/// </remarks>
public static class Illustrations
{
    /// <summary>Dimensione di progetto del disegno, in unità.</summary>
    public const double DesignWidth = 160;

    /// <summary>Altezza di progetto del disegno, in unità.</summary>
    public const double DesignHeight = 120;

    /// <summary>
    /// Crea lo stato vuoto: disegno, titolo e testo esplicativo nella lingua di <paramref name="texts"/>.
    /// Il chiamante lo inserisce nel proprio layout; non serve chiamare altro.
    /// </summary>
    /// <param name="kind">Quale illustrazione.</param>
    /// <param name="texts">Testi nella lingua scelta (IT o EN).</param>
    public static UIElement Create(IllustrationKind kind, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var (title, body) = TextsFor(kind, texts);

        var titleText = new TextBlock
        {
            Text = title,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 4),
        };
        titleText.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Ui");
        titleText.SetResourceReference(TextBlock.FontSizeProperty, "Size.Subtitle");
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        titleText.FontWeight = FontWeights.SemiBold;

        var bodyText = new TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 320,
        };
        bodyText.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Ui");
        bodyText.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        bodyText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(new Viewbox
        {
            Width = DesignWidth,
            Height = DesignHeight,
            Child = BuildDrawing(kind),
        });
        panel.Children.Add(titleText);
        panel.Children.Add(bodyText);
        return panel;
    }

    private static (string Title, string Body) TextsFor(IllustrationKind kind, DesktopTexts texts) => kind switch
    {
        IllustrationKind.NoNight => (texts.IllusNoNightTitle, texts.IllusNoNightBody),
        IllustrationKind.NoEvents => (texts.IllusNoEventsTitle, texts.IllusNoEventsBody),
        IllustrationKind.CameraNotConnected => (texts.IllusCameraTitle, texts.IllusCameraBody),
        IllustrationKind.YouTubeNotConnected => (texts.IllusYouTubeTitle, texts.IllusYouTubeBody),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Disegno come Canvas 160×120 con i suoi tratti.</summary>
    private static Canvas BuildDrawing(IllustrationKind kind)
    {
        var canvas = new Canvas { Width = DesignWidth, Height = DesignHeight };
        foreach (DrawStroke stroke in StrokesFor(kind))
        {
            var path = new Path
            {
                Data = Geometry.Parse(stroke.Data),
                StrokeThickness = stroke.Width,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };
            path.SetResourceReference(Shape.StrokeProperty, stroke.Role);
            if (stroke.Dashed)
            {
                path.StrokeDashArray = new DoubleCollection { 3, 3 };
            }

            canvas.Children.Add(path);
        }

        return canvas;
    }

    /// <summary>Un tratto: percorso, ruolo del colore nel tema, spessore e se è tratteggiato.</summary>
    private readonly record struct DrawStroke(string Data, string Role, double Width, bool Dashed = false);

    private static DrawStroke[] StrokesFor(IllustrationKind kind) => kind switch
    {
        IllustrationKind.NoNight =>
        [
            // Orizzonte.
            new("M 16,84 L 144,84", "Brush.TextSecondary", 1.5),
            // Sole sopra l'orizzonte, centro (80,84) raggio 26.
            new("M 54,84 A 26,26 0 0 1 106,84", "Brush.Sun", 2.5),
            // Raggi: da raggio 34 a 42, ogni 30 gradi da 0 a 180.
            new("M 46,84 L 38,84 M 50.56,67 L 43.63,63 M 63,54.56 L 59,47.63 M 80,50 L 80,42 M 97,54.56 L 101,47.63 M 109.44,67 L 116.37,63 M 114,84 L 122,84", "Brush.Sun", 2.0),
        ],
        IllustrationKind.NoEvents =>
        [
            // Orbita esterna, centro (80,58) raggio 28.
            new("M 52,58 A 28,28 0 1 0 108,58 A 28,28 0 1 0 52,58 Z", "Brush.BorderActive", 2.0),
            // Cerchio interno tratteggiato, raggio 12.
            new("M 68,58 A 12,12 0 1 0 92,58 A 12,12 0 1 0 68,58 Z", "Brush.TextTertiary", 1.2, Dashed: true),
            // Traccia di meteora tratteggiata, senza evento.
            new("M 26,104 Q 58,92 90,100", "Brush.Meteor", 1.5, Dashed: true),
        ],
        IllustrationKind.CameraNotConnected =>
        [
            // Corpo della camera, angoli arrotondati raggio 8.
            new("M 52,40 H 108 A 8,8 0 0 1 116,48 V 76 A 8,8 0 0 1 108,84 H 52 A 8,8 0 0 1 44,76 V 48 A 8,8 0 0 1 52,40 Z", "Brush.BorderActive", 2.5),
            // Mirino superiore.
            new("M 66,40 L 70,30 H 90 L 94,40", "Brush.BorderActive", 2.5),
            // Obiettivo, centro (80,62) raggio 14.
            new("M 66,62 A 14,14 0 1 0 94,62 A 14,14 0 1 0 66,62 Z", "Brush.Accent", 2.0),
            // Collegamento tagliato.
            new("M 30,96 L 130,20", "Brush.TextSecondary", 2.0),
        ],
        IllustrationKind.YouTubeNotConnected =>
        [
            // Schermo, angoli arrotondati raggio 10.
            new("M 36,34 H 124 A 10,10 0 0 1 134,44 V 78 A 10,10 0 0 1 124,88 H 36 A 10,10 0 0 1 26,78 V 44 A 10,10 0 0 1 36,34 Z", "Brush.BorderActive", 2.5),
            // Triangolo di riproduzione.
            new("M 72,48 L 72,74 L 96,61 Z", "Brush.Accent", 2.0),
            // Collegamento interrotto sotto lo schermo.
            new("M 26,108 H 62 M 98,108 H 134", "Brush.TextTertiary", 2.0),
            new("M 70,108 H 90", "Brush.TextTertiary", 1.5, Dashed: true),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
