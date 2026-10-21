using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AllSky.Core.Design;
using AllSky.Desktop.Theme;

namespace AllSky.Desktop.Views;

/// <summary>
/// "Galleria componenti": mostra tutti gli stili del tema (pulsanti, campi, interruttore, slider, barra, pannello),
/// la scala tipografica e di spaziatura e la palette con la sigla di ogni ruolo. Si apre con <c>--gallery</c>.
/// </summary>
/// <remarks>
/// È C# puro, senza XAML della finestra. Compilata sempre, non dietro #if DEBUG, così che la verifica visiva
/// si possa fare anche su una build di rilascio. Testi in italiano fissi: è uno strumento per chi sviluppa l'interfaccia,
/// non una schermata dell'utente.
/// Ctrl+R cambia la modalità notte; l'interruttore e lo slider sono collegati a <see cref="ThemeManager"/>.
/// </remarks>
internal sealed class ComponentGalleryWindow : Window
{
    private readonly ThemeManager _theme;
    private readonly CheckBox _nightToggle;
    private readonly Slider _brightness;
    private readonly TextBlock _brightnessLabel;
    private readonly List<(string Role, TextBlock Hex)> _swatchHex = new();

    /// <summary>Crea la galleria collegata al gestore del tema. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="theme">Gestore del tema dell'applicazione.</param>
    public ComponentGalleryWindow(ThemeManager theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _theme = theme;

        Title = "Galleria componenti - All Sky Brain";
        Width = 1280;
        Height = 800;
        SetResourceReference(BackgroundProperty, "Brush.Background");

        // Controlli di modalità: vanno creati prima della sezione, perché il gestore li sincronizza.
        _nightToggle = new CheckBox
        {
            Content = "Modalità notte rossa (Ctrl+R)",
            IsChecked = _theme.NightMode,
        };
        _brightness = new Slider
        {
            Minimum = Palette.MinimumFactor,
            Maximum = Palette.MaximumFactor,
            Value = _theme.Brightness,
            Width = 280,
            TickFrequency = 0.1,
        };
        _brightnessLabel = Text(string.Empty, "Size.Caption");

        var content = new StackPanel { Margin = new Thickness(Space(5)) };
        content.Children.Add(Text("Galleria componenti", "Size.Display"));
        content.Children.Add(Text("Stili e palette di All Sky Brain. Ctrl+R cambia la modalità notte.", "Size.Body"));

        content.Children.Add(Section("Modalità", ModeSection()));
        content.Children.Add(Section("Pulsanti", ButtonSection()));
        content.Children.Add(Section("Campi, interruttore, cursore, barra", FieldSection()));
        content.Children.Add(Section("Scala tipografica", TypeSection()));
        content.Children.Add(Section("Scala di spaziatura", SpaceSection()));
        content.Children.Add(Section("Palette", PaletteSection()));

        Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        _nightToggle.Checked += (_, _) => SetNight(true);
        _nightToggle.Unchecked += (_, _) => SetNight(false);
        _brightness.ValueChanged += (_, e) => _theme.SetBrightness(e.NewValue);
        _theme.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => _theme.ThemeChanged -= OnThemeChanged;

        PreviewKeyDown += OnPreviewKeyDown;
        RefreshLabels();
    }

    /// <summary>Ctrl+R: inverte la modalità notte rossa.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _theme.ToggleNightMode();
            e.Handled = true;
        }
    }

    /// <summary>Il tema è cambiato (da qui o da altrove): riallinea i controlli e le sigle esadecimali.</summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        RefreshLabels();
    }

    /// <summary>Applica la modalità notte senza far rientrare l'evento del controllo.</summary>
    private void SetNight(bool enabled)
    {
        if (_theme.NightMode != enabled)
        {
            _theme.SetNightMode(enabled);
        }
    }

    /// <summary>Riporta interruttore, slider, etichetta e sigle della palette allo stato del tema.</summary>
    private void RefreshLabels()
    {
        if (_nightToggle.IsChecked != _theme.NightMode)
        {
            _nightToggle.IsChecked = _theme.NightMode;
        }

        if (Math.Abs(_brightness.Value - _theme.Brightness) > 0.0001)
        {
            _brightness.Value = _theme.Brightness;
        }

        string mode = _theme.NightMode ? "notte rossa" : "scuro";
        _brightnessLabel.Text = $"Modalità {mode}. Luminosità {_theme.Brightness * 100:F0}%";

        var hexByRole = _theme.CurrentPalette.Entries().ToDictionary(e => e.Key, e => e.Value);
        foreach (var (role, hex) in _swatchHex)
        {
            hex.Text = $"{role}  {hexByRole[role]}";
        }
    }

    private StackPanel ModeSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(_nightToggle);
        panel.Children.Add(Gap(Space(3)));
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Text("Luminosità", "Size.Body"));
        row.Children.Add(Spacer(Space(3)));
        row.Children.Add(_brightness);
        row.Children.Add(Spacer(Space(3)));
        row.Children.Add(_brightnessLabel);
        panel.Children.Add(row);
        return panel;
    }

    private WrapPanel ButtonSection()
    {
        var wrap = new WrapPanel();
        wrap.Children.Add(MakeButton("Primario", "Button.Primary", enabled: true));
        wrap.Children.Add(MakeButton("Secondario", "Button.Secondary", enabled: true));
        wrap.Children.Add(MakeButton("Pericolo", "Button.Danger", enabled: true));
        wrap.Children.Add(MakeButton("Primario disabilitato", "Button.Primary", enabled: false));
        wrap.Children.Add(MakeButton("Secondario disabilitato", "Button.Secondary", enabled: false));
        wrap.Children.Add(MakeButton("Pericolo disabilitato", "Button.Danger", enabled: false));
        return wrap;
    }

    private StackPanel FieldSection()
    {
        var panel = new StackPanel();

        panel.Children.Add(Text("Campo di testo", "Size.Caption"));
        panel.Children.Add(new TextBox { Text = "Testo modificabile", Width = 360, Margin = new Thickness(0, Space(1), 0, Space(3)) });
        panel.Children.Add(new TextBox { Text = "Campo disabilitato", Width = 360, IsEnabled = false, Margin = new Thickness(0, 0, 0, Space(3)) });

        panel.Children.Add(new CheckBox { Content = "Opzione attiva", IsChecked = true, Margin = new Thickness(0, 0, 0, Space(2)) });
        panel.Children.Add(new CheckBox { Content = "Opzione disattiva", IsChecked = false, Margin = new Thickness(0, 0, 0, Space(2)) });
        panel.Children.Add(new CheckBox { Content = "Opzione disabilitata", IsEnabled = false, Margin = new Thickness(0, 0, 0, Space(3)) });

        panel.Children.Add(new Slider { Minimum = 0, Maximum = 100, Value = 40, Width = 360, Margin = new Thickness(0, 0, 0, Space(3)) });
        panel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = 60, Width = 360, Margin = new Thickness(0, 0, 0, Space(3)) });

        panel.Children.Add(new Separator());
        panel.Children.Add(new Label { Content = "Etichetta di esempio", Margin = new Thickness(0, Space(2), 0, 0) });

        // Tooltip su un pulsante: il contenuto del suggerimento usa lo stile ToolTip del tema.
        var tipButton = MakeButton("Passa il mouse qui", "Button.Secondary", enabled: true);
        tipButton.ToolTip = "Suggerimento del tema: sfondo Surface2, bordo attivo";
        tipButton.Margin = new Thickness(0, Space(3), 0, 0);
        panel.Children.Add(tipButton);
        return panel;
    }

    private StackPanel TypeSection()
    {
        var panel = new StackPanel();
        panel.Children.Add(Text("Caption 12 px: etichette e unità", "Size.Caption"));
        panel.Children.Add(Text("Body 14 px: testo di lavoro e pulsanti", "Size.Body"));
        panel.Children.Add(Text("Subtitle 16 px: sottotitoli di pagina", "Size.Subtitle"));
        panel.Children.Add(Text("Title 20 px: titoli di pagina", "Size.Title"));
        panel.Children.Add(Text("Display 28 px: intestazioni", "Size.Display"));
        panel.Children.Add(Text("Hero 40 px: numeri chiave", "Size.Hero"));

        var mono = Text("RA 05h 35m 17.3s   Dec -05°23'28\"   Alt 42.3°   T -12.5 °C   ADU 1834", "Size.Body");
        mono.SetResourceReference(TextBlock.FontFamilyProperty, ThemeManager.MonoFontKey);
        mono.Margin = new Thickness(0, Space(3), 0, 0);
        panel.Children.Add(mono);
        return panel;
    }

    private WrapPanel SpaceSection()
    {
        var wrap = new WrapPanel();
        for (int n = 1; n <= 7; n++)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, Space(4), 0) };
            var bar = new Border
            {
                Height = 24,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, Space(1)),
            };
            bar.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
            bar.SetResourceReference(FrameworkElement.WidthProperty, $"Space.{n}");
            stack.Children.Add(bar);
            stack.Children.Add(Text($"Space.{n}", "Size.Caption"));
            wrap.Children.Add(stack);
        }

        return wrap;
    }

    private WrapPanel PaletteSection()
    {
        var wrap = new WrapPanel();
        foreach (var (role, _) in _theme.CurrentPalette.Entries())
        {
            var stack = new StackPanel { Width = 132, Margin = new Thickness(0, 0, Space(3), Space(3)) };
            var swatch = new Border
            {
                Height = 56,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, Space(1)),
            };
            swatch.SetResourceReference(Border.BackgroundProperty, ThemeManager.BrushPrefix + role);
            swatch.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            stack.Children.Add(swatch);

            TextBlock hex = Text(role, "Size.Caption");
            hex.TextWrapping = TextWrapping.Wrap;
            stack.Children.Add(hex);
            _swatchHex.Add((role, hex));
            wrap.Children.Add(stack);
        }

        return wrap;
    }

    /// <summary>Pannello con titolo: usa lo stile "Panel" e "PanelTitle" del tema.</summary>
    private Border Section(string title, UIElement body)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("PanelTitle") });
        inner.Children.Add(body);

        return new Border
        {
            Style = (Style)FindResource("Panel"),
            Margin = new Thickness(0, 0, 0, Space(4)),
            Child = inner,
        };
    }

    /// <summary>Pulsante con uno dei tre stili del tema, eventualmente disabilitato.</summary>
    private Button MakeButton(string label, string styleKey, bool enabled)
    {
        return new Button
        {
            Content = label,
            Style = (Style)FindResource(styleKey),
            IsEnabled = enabled,
            Margin = new Thickness(0, 0, Space(3), Space(3)),
        };
    }

    /// <summary>Testo con la dimensione presa dalla risorsa indicata (la sigla "Size.*" o "Font.*" passa dal tema).</summary>
    private TextBlock Text(string text, string sizeKey)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        return block;
    }

    private static UIElement Gap(double height) => new Border { Height = height };

    private static UIElement Spacer(double width) => new Border { Width = width };

    /// <summary>Legge una spaziatura della scala (Space.n) dalle risorse, in unità indipendenti dal dispositivo.</summary>
    private double Space(int n) => (double)FindResource($"Space.{n}");
}
