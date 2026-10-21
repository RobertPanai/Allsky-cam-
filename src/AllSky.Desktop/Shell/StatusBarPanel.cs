using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AllSky.Core;
using AllSky.Core.Shell;
using AllSky.Desktop.Theme;

namespace AllSky.Desktop.Shell;

/// <summary>
/// Barra di stato in basso: camera, fase, Sole, Luna, ultimo frame, disco, pubblicazioni e il pulsante della notte rossa.
/// Ogni voce è un'icona, un'etichetta e un valore; un dato mancante mostra "n/d" con il motivo nel tooltip.
/// </summary>
/// <remarks>
/// Riceve un <see cref="StatusBarText"/> già formattato da <see cref="StatusBarFormatter"/>: questo controllo non calcola nulla.
/// Ricostruisce le voci a ogni aggiornamento, che è poco frequente (a ogni frame archiviato o cambio di stato).
/// </remarks>
internal sealed class StatusBarPanel : Border
{
    // WrapPanel: con la finestra stretta le voci vanno a capo invece di essere tagliate.
    private readonly WrapPanel _items = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Crea la barra. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="texts">Testi della lingua scelta (pulsante della notte rossa).</param>
    /// <param name="theme">Gestore del tema, per il pulsante della modalità notte.</param>
    public StatusBarPanel(DesktopTexts texts, ThemeManager theme)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(theme);

        SetResourceReference(BackgroundProperty, "Brush.Surface1");
        SetResourceReference(BorderBrushProperty, "Brush.Border");
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(24, 8, 24, 8);

        var night = new Button
        {
            Content = texts.NightMode,
            ToolTip = "Ctrl+R",
            VerticalAlignment = VerticalAlignment.Center,
        };
        night.SetResourceReference(FrameworkElement.StyleProperty, "Button.Secondary");
        night.Click += (_, _) => theme.ToggleNightMode();
        AutomationProperties.SetName(night, texts.NightMode);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_items, 0);
        Grid.SetColumn(night, 1);
        grid.Children.Add(_items);
        grid.Children.Add(night);

        Child = grid;
    }

    /// <summary>Sostituisce le voci con quelle della formattazione corrente. Va chiamato dal thread dell'interfaccia.</summary>
    /// <param name="text">Barra formattata.</param>
    public void Show(StatusBarText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        _items.Children.Clear();
        _items.Children.Add(CreateItem(text.Camera, ShellIcons.Camera));
        _items.Children.Add(CreateItem(text.Phase, ShellIcons.ForPhase(text.PhaseKind)));
        _items.Children.Add(CreateItem(text.Sun, ShellIcons.Sun));
        _items.Children.Add(CreateItem(text.Moon, ShellIcons.Moon));
        _items.Children.Add(CreateItem(text.LastFrame, ShellIcons.Image));
        _items.Children.Add(CreateItem(text.Disk, ShellIcons.Disk));
        _items.Children.Add(CreateItem(text.Publications, ShellIcons.Upload));
    }

    /// <summary>Una voce: icona, etichetta e valore. Il valore mancante è in colore terziario, con tooltip.</summary>
    private static StackPanel CreateItem(StatusBarItem item, System.Windows.Media.Geometry icon)
    {
        var glyph = new System.Windows.Shapes.Path
        {
            Data = icon,
            Width = 16,
            Height = 16,
            Stretch = System.Windows.Media.Stretch.Uniform,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,
            item.Available ? "Brush.TextSecondary" : "Brush.TextTertiary");

        var label = new TextBlock
        {
            Text = item.Label,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        label.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");

        var value = new TextBlock
        {
            Text = item.Value,
            VerticalAlignment = VerticalAlignment.Center,
        };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        value.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        value.SetResourceReference(TextBlock.ForegroundProperty, item.Available ? "Brush.Text" : "Brush.TextTertiary");

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 28, 0),
            ToolTip = item.Tooltip,
        };
        row.Children.Add(glyph);
        row.Children.Add(label);
        row.Children.Add(value);
        AutomationProperties.SetName(row, $"{item.Label}: {item.Value}");
        return row;
    }
}
