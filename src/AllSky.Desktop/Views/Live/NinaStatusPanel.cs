using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using AllSky.Core;
using AllSky.Engine;
using AllSky.Nina;

namespace AllSky.Desktop.Views.Live;

/// <summary>
/// Pannello compatto «NINA» della vista Live: soggetto della sequenza, stato di park e connessione letti da NINA.
/// </summary>
/// <remarks>
/// <para>Legge solo <see cref="IAllSkyService.LatestNinaState"/>: nessuna chiamata di rete, nessun listener sugli eventi.
/// Un <see cref="DispatcherTimer"/> rilegge lo stato ogni <see cref="RefreshInterval"/>; il timer gira solo tra
/// <see cref="FrameworkElement.Loaded"/> e <see cref="FrameworkElement.Unloaded"/>.</para>
/// <para>Non mostra percorsi, indirizzi, porte né messaggi di errore: il campo <c>Error</c> dello stato non viene letto.</para>
/// </remarks>
public sealed class NinaStatusPanel : Border
{
    /// <summary>Intervallo di rilettura dello stato NINA.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly IAllSkyService _service;
    private readonly DesktopTexts _texts;
    private readonly DispatcherTimer _timer;
    private readonly StackPanel _rows = new();

    /// <summary>Crea il pannello. Va creato sul thread dell'interfaccia.</summary>
    /// <param name="service">Servizio dell'Engine; si legge solo <see cref="IAllSkyService.LatestNinaState"/>.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public NinaStatusPanel(IAllSkyService service, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(texts);

        _service = service;
        _texts = texts;

        Style = (Style)System.Windows.Application.Current.FindResource("Panel");

        var stack = new StackPanel();
        var title = new TextBlock { Text = texts.NinaPanelTitle, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(FrameworkElement.StyleProperty, "PanelTitle");
        stack.Children.Add(title);
        stack.Children.Add(_rows);
        Child = stack;

        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();

        Refresh();
    }

    /// <summary>Rilegge lo stato NINA e ricostruisce le righe. Senza stato mostra solo «NINA non attivo».</summary>
    private void Refresh()
    {
        NinaState? state = _service.LatestNinaState;
        _rows.Children.Clear();

        if (state is null)
        {
            var message = new TextBlock { Text = _texts.NinaOff, TextWrapping = TextWrapping.Wrap };
            message.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            message.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
            _rows.Children.Add(message);
            return;
        }

        _rows.Children.Add(CreateRow(_texts.NinaSubjectLabel, SubjectText(state)));
        _rows.Children.Add(CreateRow(_texts.NinaParkLabel, ParkText(ParkState.Compute(state))));
        _rows.Children.Add(CreateRow(
            _texts.NinaConnectionLabel,
            state.Connected ? _texts.NinaConnected : _texts.NinaNotConnected));
    }

    private string SubjectText(NinaState state)
    {
        string? name = state.Target?.Name;
        return string.IsNullOrWhiteSpace(name) ? _texts.NotAvailable : name.Trim();
    }

    private string ParkText(ParkStatus status) => status switch
    {
        ParkStatus.Parked => _texts.NinaParkParked,
        ParkStatus.Tracking => _texts.NinaParkTracking,
        ParkStatus.Slewing => _texts.NinaParkSlewing,
        ParkStatus.Stopped => _texts.NinaParkStopped,
        ParkStatus.Unknown => _texts.NinaParkUnknown,
        ParkStatus.Disconnected => _texts.NinaParkDisconnected,
        _ => _texts.NotAvailable,
    };

    private Grid CreateRow(string label, string value)
    {
        var labelBlock = new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 4) };
        labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        labelBlock.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        var valueBlock = new TextBlock
        {
            Text = value,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        valueBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        valueBlock.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        AutomationProperties.SetName(valueBlock, label);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(valueBlock, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(valueBlock);
        return row;
    }
}
