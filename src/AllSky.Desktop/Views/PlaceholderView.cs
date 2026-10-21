using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace AllSky.Desktop.Views;

/// <summary>
/// Schermata segnaposto di una vista non ancora implementata: titolo e stato vuoto curato, dentro un pannello del tema.
/// Ogni task di P1.4–P1.8 e P1.11 la sostituisce con la vista vera.
/// </summary>
/// <remarks>
/// Usa lo stile <c>Panel</c> di Theme/Styles.xaml. Tutti i colori e le dimensioni arrivano da risorse dinamiche.
/// </remarks>
internal sealed class PlaceholderView : Grid
{
    /// <summary>Crea il segnaposto con il titolo della vista e il testo dello stato vuoto.</summary>
    /// <param name="title">Nome della vista, già nella lingua scelta.</param>
    /// <param name="emptyText">Spiegazione dello stato vuoto, già nella lingua scelta.</param>
    public PlaceholderView(string title, string emptyText)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(emptyText);

        var heading = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        heading.SetResourceReference(TextBlock.FontSizeProperty, "Size.Display");

        var body = new TextBlock
        {
            Text = emptyText,
            TextWrapping = TextWrapping.Wrap,
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        body.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        var stack = new StackPanel();
        stack.Children.Add(heading);
        stack.Children.Add(body);

        var panel = new Border
        {
            Style = (Style)System.Windows.Application.Current.FindResource("Panel"),
            Child = stack,
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        Children.Add(panel);
        AutomationProperties.SetName(this, title);
    }
}
