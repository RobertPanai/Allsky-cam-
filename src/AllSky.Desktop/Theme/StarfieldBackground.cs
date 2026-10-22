using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AllSky.Core.Design;

namespace AllSky.Desktop.Theme;

/// <summary>
/// Sfondo stellare statico per le schermate senza immagine del cielo (P1.10). Le stelle vengono da
/// <see cref="StarfieldGenerator"/> con un seme fisso: non è animato e non cambia tra un avvio e l'altro.
/// </summary>
/// <remarks>
/// Le stelle si ricalcolano solo quando cambia la dimensione, con un ritardo di <see cref="ResizeDelay"/> per non
/// rifare il lavoro a ogni pixel del trascinamento. Il cambio di tema (notte rossa, luminosità) ridisegna solo i colori,
/// non riposiziona le stelle. Il disegno usa un <see cref="DrawingVisual"/>, senza animazioni né ciclo di rendering continuo.
/// </remarks>
public sealed class StarfieldBackground : FrameworkElement
{
    /// <summary>Seme fisso dello sfondo: stesso seme, stesse stelle.</summary>
    public const int DefaultSeed = 20261022;

    /// <summary>Attesa dopo l'ultimo cambio di dimensione prima di ricalcolare le stelle.</summary>
    public static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>Colore di ripiego se la risorsa del testo non esiste (palette scura, ruolo Text).</summary>
    private static readonly Color FallbackStarColor = Color.FromRgb(0xE6, 0xED, 0xF7);

    /// <summary>Numero di livelli di opacità: le stelle si disegnano con pennelli precalcolati, uno per livello.</summary>
    private const int AlphaLevels = 16;

    private readonly DrawingVisual _visual = new();
    private readonly DispatcherTimer _resizeTimer;
    private readonly SolidColorBrush[] _brushes = new SolidColorBrush[AlphaLevels + 1];
    private IReadOnlyList<Star> _stars = Array.Empty<Star>();
    private bool _generated;
    private bool _subscribed;

    /// <summary>Crea lo sfondo. Le stelle compaiono al primo layout con una dimensione valida.</summary>
    public StarfieldBackground()
    {
        IsHitTestVisible = false;
        AddVisualChild(_visual);

        _resizeTimer = new DispatcherTimer { Interval = ResizeDelay };
        _resizeTimer.Tick += OnResizeDelayElapsed;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Seme del generatore. Un cambio dopo il primo disegno ricalcola le stelle.</summary>
    public int Seed
    {
        get => _seed;
        set
        {
            if (_seed == value)
            {
                return;
            }

            _seed = value;
            if (_generated)
            {
                Regenerate();
            }
        }
    }

    private int _seed = DefaultSeed;

    /// <inheritdoc />
    protected override int VisualChildrenCount => 1;

    /// <inheritdoc />
    protected override Visual GetVisualChild(int index)
    {
        if (index != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _visual;
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        if (!_generated)
        {
            Regenerate();
            return;
        }

        // Ricalcolo differito: ogni nuovo cambio di dimensione riparte il conto.
        _resizeTimer.Stop();
        _resizeTimer.Start();
    }

    private void OnResizeDelayElapsed(object? sender, EventArgs e)
    {
        _resizeTimer.Stop();
        Regenerate();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed && System.Windows.Application.Current is App app)
        {
            app.Theme.ThemeChanged += OnThemeChanged;
            _subscribed = true;
        }

        Redraw();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _resizeTimer.Stop();
        if (_subscribed && System.Windows.Application.Current is App app)
        {
            app.Theme.ThemeChanged -= OnThemeChanged;
            _subscribed = false;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Redraw();

    /// <summary>Ricalcola le stelle per la dimensione attuale. Con area nulla lo sfondo resta vuoto.</summary>
    private void Regenerate()
    {
        _generated = true;
        int width = (int)Math.Round(ActualWidth);
        int height = (int)Math.Round(ActualHeight);
        _stars = width > 0 && height > 0
            ? StarfieldGenerator.Generate(Seed, width, height)
            : Array.Empty<Star>();
        Redraw();
    }

    /// <summary>Disegna le stelle con il colore del tema corrente.</summary>
    private void Redraw()
    {
        Color color = ResolveStarColor();
        EnsureBrushes(color);

        using DrawingContext context = _visual.RenderOpen();
        foreach (Star star in _stars)
        {
            int level = (int)Math.Round(star.Alpha * AlphaLevels);
            context.DrawEllipse(_brushes[level], null, new Point(star.X, star.Y), star.Radius, star.Radius);
        }
    }

    /// <summary>Pennelli per i livelli di opacità, nel colore indicato. Ricreati solo se il colore cambia.</summary>
    private void EnsureBrushes(Color color)
    {
        if (_brushes[0] is not null && _brushes[0].Color.R == color.R
            && _brushes[0].Color.G == color.G && _brushes[0].Color.B == color.B)
        {
            return;
        }

        for (int level = 0; level <= AlphaLevels; level++)
        {
            byte alpha = (byte)Math.Round(255.0 * level / AlphaLevels);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
            brush.Freeze();
            _brushes[level] = brush;
        }
    }

    /// <summary>Colore del ruolo Text del tema corrente: in modalità notte rossa le stelle diventano rosse.</summary>
    private Color ResolveStarColor()
    {
        object? resource = TryFindResource("Color.Text");
        return resource is Color color ? color : FallbackStarColor;
    }
}
