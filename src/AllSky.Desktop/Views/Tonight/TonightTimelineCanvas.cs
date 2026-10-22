using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AllSky.Astro;
using AllSky.Core;
using AllSky.Core.Tonight;
using Shapes = System.Windows.Shapes;

namespace AllSky.Desktop.Views.Tonight;

/// <summary>
/// Linea temporale da tramonto ad alba: fasce dei crepuscoli in alto, curve di altezza di Sole e Luna sotto,
/// marcatore dell'ora corrente. Disegna con <see cref="Shapes.Path"/> e ridisegna a ogni cambio di dimensione o di modello.
/// </summary>
/// <remarks>
/// Il colore porta un'informazione, ma non è l'unica: la vista accanto elenca le fasce con gli orari in testo.
/// Il disegno non fa calcoli: riceve la linea già calcolata da <see cref="TonightCalculator"/>.
/// </remarks>
internal sealed class TonightTimelineCanvas : Canvas
{
    private const double Pad = 8.0;
    private const double BandHeight = 26.0;
    private const double LabelHeight = 18.0;
    private const double CurveGap = 10.0;
    private const double MinAltitudeScaleDeg = 12.0;

    private TonightTimeline? _timeline;
    private DateTimeOffset _now;
    private TimeZoneInfo _tz = TimeZoneInfo.Utc;
    private DesktopTexts? _texts;

    public TonightTimelineCanvas()
    {
        Height = BandHeight + CurveGap + 120.0 + LabelHeight;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        SizeChanged += (_, _) => Redraw();
    }

    /// <summary>Imposta la linea da disegnare e l'istante del marcatore.</summary>
    public void Show(TonightTimeline timeline, DateTimeOffset now, TimeZoneInfo tz, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(tz);
        ArgumentNullException.ThrowIfNull(texts);

        _timeline = timeline;
        _now = now;
        _tz = tz;
        _texts = texts;
        Redraw();
    }

    private void Redraw()
    {
        Children.Clear();
        if (_timeline is null || _texts is null)
        {
            return;
        }

        double width = ActualWidth;
        if (double.IsNaN(width) || width <= 2 * Pad)
        {
            return;
        }

        if (!_timeline.Available || _timeline.Sunset is not { } sunset || _timeline.Sunrise is not { } sunrise)
        {
            AddText(_texts.TonightNoWindow, Pad, BandHeight / 2.0, "Brush.TextSecondary", width - 2 * Pad);
            return;
        }

        double plotWidth = width - 2 * Pad;
        double Xof(DateTimeOffset t) => Pad + plotWidth * ((t - sunset).TotalSeconds / (sunrise - sunset).TotalSeconds);

        // Fasce dei crepuscoli.
        foreach (TonightSegment segment in _timeline.Segments)
        {
            double x0 = Xof(segment.From);
            double x1 = Xof(segment.To);
            var band = new Shapes.Path
            {
                Data = new RectangleGeometry(new Rect(x0, 0, Math.Max(0.0, x1 - x0), BandHeight)),
                StrokeThickness = 1.0,
                ToolTip = TonightFormatter.PhaseName(segment.Phase, _texts),
            };
            band.SetResourceReference(Shapes.Shape.FillProperty, PhaseBrushKey(segment.Phase));
            band.SetResourceReference(Shapes.Shape.StrokeProperty, "Brush.Border");
            Children.Add(band);
        }

        // Curve di altezza: scala simmetrica attorno all'orizzonte.
        double curveTop = BandHeight + CurveGap;
        double curveHeight = 120.0;
        double horizonY = curveTop + curveHeight / 2.0;
        double maxAbs = MinAltitudeScaleDeg;
        foreach (TonightSample sample in _timeline.Samples)
        {
            maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(sample.SunAltitudeDeg), Math.Abs(sample.MoonAltitudeDeg)));
        }
        double halfHeight = curveHeight / 2.0 - 4.0;
        double Yof(double altitude) => horizonY - altitude / maxAbs * halfHeight;

        AddCurve(_timeline.Samples, s => s.SunAltitudeDeg, Xof, Yof, "Brush.Sun");
        AddCurve(_timeline.Samples, s => s.MoonAltitudeDeg, Xof, Yof, "Brush.Moon");

        var horizon = new Shapes.Path
        {
            Data = new LineGeometry(new Point(Pad, horizonY), new Point(width - Pad, horizonY)),
            StrokeThickness = 1.0,
            StrokeDashArray = new DoubleCollection { 3, 3 },
        };
        horizon.SetResourceReference(Shapes.Shape.StrokeProperty, "Brush.TextTertiary");
        Children.Add(horizon);

        // Marcatore dell'ora corrente, solo se cade nella finestra.
        if (_timeline.ProgressAt(_now) is { } progress)
        {
            double x = Pad + plotWidth * progress;
            var marker = new Shapes.Path
            {
                Data = new LineGeometry(new Point(x, 0), new Point(x, curveTop + curveHeight)),
                StrokeThickness = 1.5,
            };
            marker.SetResourceReference(Shapes.Shape.StrokeProperty, "Brush.Accent");
            Children.Add(marker);
        }

        // Etichette degli estremi.
        double labelY = curveTop + curveHeight + 2.0;
        string sunsetText = _texts.TonightSunset + " " + TonightFormatter.FormatClock(sunset, _tz, _texts.Culture);
        string sunriseText = _texts.TonightSunrise + " " + TonightFormatter.FormatClock(sunrise, _tz, _texts.Culture);
        AddText(sunsetText, Pad, labelY, "Brush.TextSecondary", plotWidth / 2.0);
        AddText(sunriseText, width - Pad - plotWidth / 2.0, labelY, "Brush.TextSecondary", plotWidth / 2.0, rightAligned: true);
    }

    private void AddCurve(
        IReadOnlyList<TonightSample> samples,
        Func<TonightSample, double> altitude,
        Func<DateTimeOffset, double> xOf,
        Func<double, double> yOf,
        string brushKey)
    {
        if (samples.Count < 2)
        {
            return;
        }

        var figure = new PathFigure { StartPoint = new Point(xOf(samples[0].Utc), yOf(altitude(samples[0]))) };
        for (int i = 1; i < samples.Count; i++)
        {
            figure.Segments.Add(new LineSegment(new Point(xOf(samples[i].Utc), yOf(altitude(samples[i]))), true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        var curve = new Shapes.Path { Data = geometry, StrokeThickness = 1.5 };
        curve.SetResourceReference(Shapes.Shape.StrokeProperty, brushKey);
        Children.Add(curve);
    }

    private void AddText(string text, double x, double y, string brushKey, double maxWidth, bool rightAligned = false)
    {
        var block = new TextBlock
        {
            Text = text,
            Width = maxWidth,
            TextAlignment = rightAligned ? TextAlignment.Right : TextAlignment.Left,
            TextWrapping = TextWrapping.Wrap,
        };
        block.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        block.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        SetLeft(block, x);
        SetTop(block, y);
        Children.Add(block);
    }

    /// <summary>Chiave del pennello di una fase, dalla palette.</summary>
    private static string PhaseBrushKey(SkyPhase phase) => phase switch
    {
        SkyPhase.Day => "Brush.Surface3",
        SkyPhase.CivilTwilight => "Brush.TwilightCivil",
        SkyPhase.NauticalTwilight => "Brush.TwilightNautical",
        SkyPhase.AstronomicalTwilight => "Brush.TwilightAstro",
        SkyPhase.Night => "Brush.Night",
        _ => "Brush.Surface1",
    };
}
