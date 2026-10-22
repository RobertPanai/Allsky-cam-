using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AllSky.Core.Tonight;
using Shapes = System.Windows.Shapes;

namespace AllSky.Desktop.Views.Tonight;

/// <summary>
/// Glifo vettoriale della fase lunare: disco scuro, parte illuminata come <see cref="Shapes.Path"/>.
/// Il contorno arriva da <see cref="MoonPhaseGlyph.LitOutline"/>, quindi la logica della fase non sta qui.
/// </summary>
internal sealed class MoonGlyphView : Canvas
{
    /// <summary>Diametro del glifo in pixel WPF.</summary>
    private const double Diameter = 64.0;

    /// <summary>Margine del bordo del disco, per non tagliare il tratto di contorno.</summary>
    private const double Inset = 1.0;

    private readonly Shapes.Ellipse _disk = new();
    private readonly Shapes.Path _lit = new();

    public MoonGlyphView()
    {
        Width = Diameter;
        Height = Diameter;
        ClipToBounds = false;

        _disk.Width = Diameter - 2 * Inset;
        _disk.Height = Diameter - 2 * Inset;
        _disk.StrokeThickness = 1.0;
        _disk.SetResourceReference(Shapes.Shape.FillProperty, "Brush.Surface2");
        _disk.SetResourceReference(Shapes.Shape.StrokeProperty, "Brush.Border");
        SetLeft(_disk, Inset);
        SetTop(_disk, Inset);

        _lit.SetResourceReference(Shapes.Shape.FillProperty, "Brush.Moon");
        _lit.Stroke = null;

        Children.Add(_disk);
        Children.Add(_lit);
    }

    /// <summary>Mostra la fase indicata. Il contorno vuoto lascia solo il disco.</summary>
    public void Show(MoonPhaseGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);

        if (glyph.LitOutline.Count < 3)
        {
            _lit.Data = null;
            return;
        }

        double center = Diameter / 2.0;
        double radius = (Diameter - 2 * Inset) / 2.0;

        var figure = new PathFigure
        {
            StartPoint = ToPixel(glyph.LitOutline[0], center, radius),
            IsClosed = true,
            IsFilled = true,
        };
        for (int i = 1; i < glyph.LitOutline.Count; i++)
        {
            figure.Segments.Add(new LineSegment(ToPixel(glyph.LitOutline[i], center, radius), isStroked: true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _lit.Data = geometry;
    }

    private static Point ToPixel(GlyphPoint p, double center, double radius) =>
        new(center + p.X * radius, center + p.Y * radius);
}
