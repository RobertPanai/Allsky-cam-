using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AllSky.Core;

namespace AllSky.Desktop.Views;

/// <summary>
/// Schermata d'avvio di All Sky Brain (P1.10): marchio, nome e versione, senza bordi di finestra.
/// Si mostra prima della finestra principale e si chiude quando questa è caricata (collegamento in <see cref="App"/>).
/// </summary>
/// <remarks>
/// Il marchio viene dalle geometrie di Theme/Brand.xaml, generate da tools/brand/build_brand.py. Colori e font
/// seguono il tema corrente tramite risorse dinamiche. Il testo è solo il nome del prodotto e la versione:
/// nessuna stringa nuova da tradurre.
/// </remarks>
internal sealed class SplashWindow : Window
{
    private static readonly Uri BrandDictionaryUri = new("/AllSkyBrain;component/Theme/Brand.xaml", UriKind.Relative);

    private bool _closed;

    /// <summary>Crea la schermata. Va creata sul thread dell'interfaccia, dopo il tema.</summary>
    /// <param name="texts">Testi nella lingua scelta: si usa il nome del prodotto.</param>
    public SplashWindow(DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        Title = texts.WindowTitle;
        Width = 360;
        Height = 260;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Brush.Background");

        var brand = new ResourceDictionary { Source = BrandDictionaryUri };

        var mark = new Canvas { Width = 64, Height = 64 };
        mark.Children.Add(MarkPath(brand, "Brand.Lens", 2.6, 1.0, stroke: true));
        mark.Children.Add(MarkPath(brand, "Brand.Inner", 1.2, 0.45, stroke: true));
        mark.Children.Add(MarkPath(brand, "Brand.Horizon", 1.4, 0.7, stroke: true));
        mark.Children.Add(MarkPath(brand, "Brand.Star", 0, 1.0, stroke: false));

        var markBox = new Viewbox { Width = 96, Height = 96, Child = mark };

        var name = new TextBlock
        {
            Text = texts.WindowTitle,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
        };
        name.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Ui");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Size.Title");
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.FontWeight = FontWeights.SemiBold;

        var version = new TextBlock
        {
            Text = "v" + ApplicationVersion(),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        version.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        version.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        version.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(markBox);
        stack.Children.Add(name);
        stack.Children.Add(version);

        var panel = new Border
        {
            Margin = new Thickness(16),
            Child = stack,
        };
        if (System.Windows.Application.Current?.TryFindResource("Panel") is Style panelStyle)
        {
            panel.Style = panelStyle;
        }

        Content = panel;
    }

    /// <summary>Chiude la schermata. È idempotente: si può chiamare più volte, anche prima che la finestra sia mostrata.</summary>
    public void CloseSplash()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Close();
    }

    /// <summary>Percorso di un elemento del marchio: tratto con ruolo Accent, o riempimento se <paramref name="stroke"/> è falso.</summary>
    private static Path MarkPath(ResourceDictionary brand, string key, double strokeWidth, double opacity, bool stroke)
    {
        var path = new Path
        {
            Data = (Geometry)brand[key],
            Opacity = opacity,
        };

        if (stroke)
        {
            path.StrokeThickness = strokeWidth;
            path.StrokeStartLineCap = PenLineCap.Round;
            path.StrokeEndLineCap = PenLineCap.Round;
            path.SetResourceReference(Shape.StrokeProperty, "Brush.Accent");
        }
        else
        {
            path.SetResourceReference(Shape.FillProperty, "Brush.Accent");
        }

        return path;
    }

    /// <summary>Versione dell'applicazione, senza il suffisso di build dopo il "+". Vuota se non disponibile.</summary>
    private static string ApplicationVersion()
    {
        string? informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return string.Empty;
        }

        int plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? informational[..plus] : informational;
    }
}
