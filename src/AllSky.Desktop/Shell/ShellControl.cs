using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using AllSky.Core;
using AllSky.Core.Shell;
using AllSky.Desktop.Theme;
using AllSky.Desktop.Views;
using AllSky.Engine;

namespace AllSky.Desktop.Shell;

/// <summary>
/// Shell della finestra principale: barra laterale con le icone delle viste, area centrale e barra di stato in basso.
/// </summary>
/// <remarks>
/// <para>Tastiera: Ctrl+1..Ctrl+6 aprono le viste della <see cref="ShellNavigation.ShortcutOrder"/>; Su e Giù, con il fuoco
/// su una voce della barra laterale, passano alla vista precedente o successiva; Ctrl+R cambia la modalità notte.</para>
/// <para>La navigazione passa da <see cref="ShellNavigation"/> (logica pura). Ogni voce è un <see cref="RadioButton"/>
/// in un gruppo: uno solo è selezionato, e il gruppo gestisce anche lo stato per i lettori di schermo.</para>
/// <para>Le viste sono segnaposto (<see cref="PlaceholderView"/>) fino ai task P1.4–P1.8 e P1.11.</para>
/// </remarks>
internal sealed class ShellControl : Grid
{
    /// <summary>Larghezza della barra laterale, in pixel WPF.</summary>
    private const double SidebarWidth = 208;

    /// <summary>
    /// Stile delle voci della barra laterale. È XAML, letto con <see cref="XamlReader"/>, perché i trigger di stato
    /// usano risorse dinamiche del tema. Nessun colore scritto a mano: tutto passa da <c>DynamicResource</c>.
    /// </summary>
    private const string NavStyleXaml = """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               TargetType="RadioButton">
          <Setter Property="Background" Value="{x:Null}"/>
          <Setter Property="BorderBrush" Value="{DynamicResource Brush.Accent}"/>
          <Setter Property="BorderThickness" Value="0"/>
          <Setter Property="Foreground" Value="{DynamicResource Brush.TextSecondary}"/>
          <Setter Property="FontFamily" Value="{DynamicResource Font.Ui}"/>
          <Setter Property="FontSize" Value="{DynamicResource Size.Body}"/>
          <Setter Property="HorizontalContentAlignment" Value="Left"/>
          <Setter Property="VerticalContentAlignment" Value="Center"/>
          <Setter Property="Padding" Value="{DynamicResource Inset.Button}"/>
          <Setter Property="MinHeight" Value="40"/>
          <Setter Property="Margin" Value="0,2"/>
          <Setter Property="Cursor" Value="Hand"/>
          <Setter Property="FocusVisualStyle" Value="{DynamicResource Focus.Ring}"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="RadioButton">
                <Border x:Name="Chrome"
                        Background="{TemplateBinding Background}"
                        BorderBrush="{TemplateBinding BorderBrush}"
                        BorderThickness="{TemplateBinding BorderThickness}"
                        CornerRadius="{DynamicResource Radius.Control}"
                        Padding="{TemplateBinding Padding}"
                        SnapsToDevicePixels="True">
                  <ContentPresenter HorizontalAlignment="Stretch"
                                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                    RecognizesAccessKey="True"/>
                </Border>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
          <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter Property="Background" Value="{DynamicResource Brush.Surface2}"/>
              <Setter Property="Foreground" Value="{DynamicResource Brush.Text}"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter Property="Background" Value="{DynamicResource Brush.Surface3}"/>
              <Setter Property="Foreground" Value="{DynamicResource Brush.Text}"/>
              <Setter Property="BorderThickness" Value="2,0,0,0"/>
            </Trigger>
          </Style.Triggers>
        </Style>
        """;

    private readonly DesktopTexts _texts;
    private readonly ThemeManager _theme;
    private readonly Dictionary<ShellView, RadioButton> _nav = new();
    private readonly Dictionary<ShellView, UIElement> _views = new();
    private readonly ContentControl _content;
    private readonly TextBlock _errorText;
    private readonly StatusBarPanel _statusBar;
    private readonly AllSky.Core.Passes.IPassForecastService? _passes;
    private ShellView _current;
    private bool _syncing;

    /// <summary>Crea la shell con le viste segnaposto e la barra di stato. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="texts">Testi nella lingua scelta.</param>
    /// <param name="theme">Gestore del tema, per la modalità notte rossa.</param>
    public ShellControl(DesktopTexts texts, ThemeManager theme, AllSky.Engine.IAllSkyService? service = null,
        AllSky.Host.AllSkyHostRuntime? runtime = null, AllSky.Core.AppPaths? paths = null)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(theme);

        _texts = texts;
        _theme = theme;
        _current = ShellNavigation.Default;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SidebarWidth) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Barra laterale: intestazione e una voce per vista.
        var navStyle = (Style)XamlReader.Parse(NavStyleXaml);
        var sidebarStack = new StackPanel { Margin = new Thickness(12, 24, 12, 24) };

        var header = new TextBlock { Text = texts.SidebarLabel, Margin = new Thickness(12, 0, 0, 12) };
        header.SetResourceReference(FrameworkElement.StyleProperty, "PanelTitle");
        sidebarStack.Children.Add(header);

        foreach (ShellView view in ShellNavigation.SidebarOrder)
        {
            RadioButton item = CreateNavItem(view, navStyle);
            _nav[view] = item;
            sidebarStack.Children.Add(item);
        }

        var sidebar = new Border
        {
            Child = sidebarStack,
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        sidebar.SetResourceReference(Border.BackgroundProperty, "Brush.Surface1");
        sidebar.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        Grid.SetColumn(sidebar, 0);
        Grid.SetRow(sidebar, 0);
        Children.Add(sidebar);

        // Area centrale: banner di errore sopra, vista corrente sotto.
        _errorText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 16, 24, 0),
            Visibility = Visibility.Collapsed,
        };
        _errorText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");

        _content = new ContentControl { Margin = new Thickness(24) };

        foreach (ShellView view in ShellNavigation.SidebarOrder)
        {
            _views[view] = CreateView(view, texts, theme, service, runtime, paths)
                ?? new PlaceholderView(texts.ViewName(view), texts.EmptyText(view));
        }

        var centre = new Grid();
        centre.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        centre.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_errorText, 0);
        Grid.SetRow(_content, 1);
        centre.Children.Add(_errorText);
        centre.Children.Add(_content);
        Grid.SetColumn(centre, 1);
        Grid.SetRow(centre, 0);
        Children.Add(centre);

        // Barra di stato su tutta la larghezza, in basso.
        _statusBar = new StatusBarPanel(texts, theme);
        _passes = service as AllSky.Core.Passes.IPassForecastService;
        Grid.SetColumn(_statusBar, 0);
        Grid.SetColumnSpan(_statusBar, 2);
        Grid.SetRow(_statusBar, 1);
        Children.Add(_statusBar);

        ShowView(_current);
    }

    /// <summary>Aggiorna la barra di stato e il banner di errore con lo stato del servizio.</summary>
    /// <param name="status">Stato corrente, da <c>IAllSkyService.GetStatus()</c> o dall'evento.</param>
    public void Update(StatusDto status)
    {
        ArgumentNullException.ThrowIfNull(status);

        (string? passTarget, DateTimeOffset? passStart, bool passInProgress) = NextPass();
        _statusBar.Show(StatusBarFormatter.Format(
            ToSnapshot(status, passTarget, passStart, passInProgress), _texts));

        bool hasError = !string.IsNullOrWhiteSpace(status.Error);
        _errorText.Text = hasError ? $"{_texts.ErrorLabel}: {status.Error}" : string.Empty;
        _errorText.Visibility = hasError ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Gestisce le scorciatoie della shell. Va chiamato dalla finestra in anteprima (tunnel), così vale ovunque.
    /// </summary>
    /// <param name="e">Evento di tastiera.</param>
    /// <returns>True se la shell ha gestito il tasto e l'evento va marcato come gestito.</returns>
    public bool TryHandleKey(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        ModifierKeys modifiers = Keyboard.Modifiers;

        if (modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.R)
            {
                _theme.ToggleNightMode();
                return true;
            }

            if (ShellNavigation.TryGetShortcut(ShortcutNumber(e.Key), out ShellView view))
            {
                ShowView(view);
                FocusNav(view);
                return true;
            }

            return false;
        }

        if (modifiers == ModifierKeys.None && (e.Key == Key.Up || e.Key == Key.Down))
        {
            ShellView? focused = FocusedNav();
            if (focused is not ShellView from)
            {
                return false;
            }

            ShellView next = ShellNavigation.Move(from, e.Key == Key.Down ? 1 : -1);
            ShowView(next);
            FocusNav(next);
            return true;
        }

        return false;
    }

    /// <summary>Crea una voce della barra laterale: icona vettoriale, nome e suggerimento con la scorciatoia.</summary>
    private RadioButton CreateNavItem(ShellView view, Style style)
    {
        string name = _texts.ViewName(view);
        int number = ShellNavigation.ShortcutNumber(view);
        string tooltip = number == 0 ? name : string.Format(_texts.Culture, _texts.NavTooltip, name, number);

        var icon = new System.Windows.Shapes.Path
        {
            Data = ShellIcons.For(view),
            Width = 24,
            Height = 24,
            Stretch = System.Windows.Media.Stretch.None,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Il tratto dell'icona segue il colore del testo della voce: secondario, poi accento o testo quando è selezionata.
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding(nameof(System.Windows.Controls.Control.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(RadioButton), 1),
        });

        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(label);

        var item = new RadioButton
        {
            GroupName = "ShellSidebar",
            Style = style,
            Content = row,
            ToolTip = tooltip,
        };
        item.Checked += (_, _) =>
        {
            if (!_syncing)
            {
                ShowView(view);
            }
        };

        AutomationProperties.SetName(item, name);
        AutomationProperties.SetHelpText(item, tooltip);
        return item;
    }

    /// <summary>Mostra la vista indicata e riallinea la selezione della barra laterale.</summary>
    private void ShowView(ShellView view)
    {
        _current = view;
        _content.Content = _views[view];

        _syncing = true;
        try
        {
            foreach (KeyValuePair<ShellView, RadioButton> entry in _nav)
            {
                entry.Value.IsChecked = entry.Key == view;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Sposta il fuoco sulla voce della barra laterale della vista indicata.</summary>
    private void FocusNav(ShellView view) => _nav[view].Focus();

    /// <summary>Vista della voce che ha il fuoco da tastiera, o null se il fuoco è altrove.</summary>
    private ShellView? FocusedNav()
    {
        foreach (KeyValuePair<ShellView, RadioButton> entry in _nav)
        {
            if (entry.Value.IsKeyboardFocusWithin)
            {
                return entry.Key;
            }
        }

        return null;
    }

    /// <summary>Numero della scorciatoia (1..6) per il tasto premuto con Ctrl, oppure 0.</summary>
    private static int ShortcutNumber(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        Key.D4 or Key.NumPad4 => 4,
        Key.D5 or Key.NumPad5 => 5,
        Key.D6 or Key.NumPad6 => 6,
        _ => 0,
    };

    /// <summary>
    /// Dallo stato del servizio alla barra di stato. Le pubblicazioni non sono ancora nello stato del servizio:
    /// restano "n/d" finché il servizio non le espone.
    /// </summary>
    private static StatusBarSnapshot ToSnapshot(StatusDto status, string? passTarget, DateTimeOffset? passStart, bool passInProgress) => new(
        Phase: status.Phase,
        SunAltitudeDeg: status.SunAltitudeDeg,
        MoonAltitudeDeg: status.MoonAltitudeDeg,
        CameraConnected: status.Camera.Connected,
        CameraName: status.Camera.Name,
        LastFrameTimestamp: status.LastFrame?.Timestamp,
        DiskFreeGb: status.Disk?.FreeGb,
        Publication: PublicationStatus.Unknown,
        PassTarget: passTarget,
        PassStartUtc: passStart,
        PassInProgress: passInProgress);

    /// <summary>Passaggio in corso, o prossimo, per la barra di stato (P4.3). Nessuno se la previsione non è disponibile.</summary>
    private (string? Target, DateTimeOffset? Start, bool InProgress) NextPass()
    {
        if (_passes is null)
        {
            return (null, null, false);
        }

        AllSky.Core.Passes.PassForecastSnapshot forecast = _passes.GetForecast();
        AllSky.Core.Passes.PassForecastItem? item = forecast.Current ?? forecast.Next;
        return item is null ? (null, null, false) : (item.Target, item.StartUtc, forecast.Current is not null);
    }

    private static UIElement? CreateView(ShellView view, DesktopTexts texts, ThemeManager theme,
        AllSky.Engine.IAllSkyService? service, AllSky.Host.AllSkyHostRuntime? runtime, AllSky.Core.AppPaths? paths)
    {
        return view switch
        {
            ShellView.Tonight when service is not null => new AllSky.Desktop.Views.Tonight.TonightView(service, texts),
            ShellView.Live when service is not null => new AllSky.Desktop.Views.Live.LiveView(service, texts),
            ShellView.Camera when service is not null => new AllSky.Desktop.Views.Camera.CameraView(service, texts),
            ShellView.Nights when service is not null => new AllSky.Desktop.Views.Nights.NightsView(service, texts),
            ShellView.Publication when runtime is not null => new AllSky.Desktop.Views.Publication.PublicationView(runtime.PublishSettings, texts, runtime.YouTube),
            ShellView.Settings when runtime is not null && paths is not null => new AllSky.Desktop.Views.Settings.SettingsView(runtime, paths, texts, theme),
            ShellView.Log when paths is not null => new AllSky.Desktop.Views.Log.LogView(paths, texts),
            _ => null,
        };
    }
}
