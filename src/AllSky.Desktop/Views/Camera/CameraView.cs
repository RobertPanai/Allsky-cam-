using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using AllSky.Core;
using AllSky.Core.CameraView;
using AllSky.Engine;

namespace AllSky.Desktop.Views.Camera;

/// <summary>
/// Vista "Camera": ricerca e collegamento delle camere (simulatore, ASCOM COM, ASCOM Alpaca), stato del collegamento,
/// guadagno ed esposizione massima con validazione, e un segnaposto per la calibrazione ottica (P3.3).
/// </summary>
/// <remarks>
/// <para>Tutte le chiamate che possono bloccare (ricerca, collegamento, salvataggio) girano fuori dal thread UI.</para>
/// <para>
/// La logica pura (classificazione degli errori, validazione del form, filtri per sorgente) sta in
/// <c>AllSky.Core/CameraView</c>. Qui restano solo i controlli WPF e il collegamento con <see cref="IAllSkyService"/>.
/// </para>
/// <para>
/// Limite noto: l'Engine non espone la disconnessione. Per cambiare camera se ne collega un'altra.
/// </para>
/// </remarks>
public class CameraView : UserControl
{
    /// <summary>Voci dell'elenco: pennello del tema e nessun colore di sistema, come la barra laterale.</summary>
    private const string ListItemStyleXaml = """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               TargetType="ListBoxItem">
          <Setter Property="Background" Value="Transparent"/>
          <Setter Property="Foreground" Value="{DynamicResource Brush.Text}"/>
          <Setter Property="FontFamily" Value="{DynamicResource Font.Ui}"/>
          <Setter Property="FontSize" Value="{DynamicResource Size.Body}"/>
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="Padding" Value="{DynamicResource Inset.Button}"/>
          <Setter Property="Margin" Value="0,2"/>
          <Setter Property="FocusVisualStyle" Value="{DynamicResource Focus.Ring}"/>
          <Setter Property="Template">
            <Setter.Value>
              <ControlTemplate TargetType="ListBoxItem">
                <Border x:Name="Chrome"
                        Background="{TemplateBinding Background}"
                        CornerRadius="{DynamicResource Radius.Control}"
                        Padding="{TemplateBinding Padding}"
                        SnapsToDevicePixels="True">
                  <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                    VerticalAlignment="Center"/>
                </Border>
              </ControlTemplate>
            </Setter.Value>
          </Setter>
          <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter Property="Background" Value="{DynamicResource Brush.Surface2}"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter Property="Background" Value="{DynamicResource Brush.Surface3}"/>
            </Trigger>
          </Style.Triggers>
        </Style>
        """;

    private readonly IAllSkyService _service;
    private readonly DesktopTexts _texts;

    private ComboBox _sourceBox = null!;
    private Button _searchButton = null!;
    private ListBox _listBox = null!;
    private TextBlock _listHint = null!;
    private Button _connectButton = null!;
    private TextBlock _progressText = null!;
    private TextBlock _connectedText = null!;
    private Button _disconnectButton = null!;
    private TextBlock _warningText = null!;
    private TextBlock _errorText = null!;
    private TextBlock _detailText = null!;
    private TextBlock _infoText = null!;
    private TextBlock _successText = null!;
    private TextBox _gainBox = null!;
    private TextBlock _gainProblem = null!;
    private TextBox _exposureBox = null!;
    private TextBlock _exposureProblem = null!;
    private Button _saveButton = null!;

    private IReadOnlyList<CameraCandidate> _candidates = Array.Empty<CameraCandidate>();
    private bool _searchedOnce;
    private bool _busy;
    private bool _connected;
    private bool _attached;
    private CancellationTokenSource _lifetime = new();
    private CameraErrorView? _lastError;
    private string? _statusError;
    private Style? _listItemStyle;

    /// <summary>Crea la vista con i testi in italiano.</summary>
    /// <param name="service">Servizio applicativo dell'Engine, già avviato dall'host.</param>
    public CameraView(IAllSkyService service)
        : this(service, DesktopTexts.Italian)
    {
    }

    /// <summary>Crea la vista con i testi nella lingua scelta. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="service">Servizio applicativo dell'Engine.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public CameraView(IAllSkyService service, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(texts);

        _service = service;
        _texts = texts;

        Content = BuildLayout();
        AutomationProperties.SetName(this, texts.NavCamera);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ---- Ciclo di vita ----

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _service.StatusChanged += OnStatusChanged;

        ShowSettings(_service.GetConfig());
        RenderStatus(_service.GetStatus());
        RenderList();

        if (!_searchedOnce)
        {
            await SearchAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _attached = false;
        _service.StatusChanged -= OnStatusChanged;

        // Si annulla solo la ricerca. Un collegamento in corso non si interrompe: lascerebbe la camera a metà.
        _lifetime.Cancel();
        _lifetime = new CancellationTokenSource();
    }

    private void OnStatusChanged(AllSky.Engine.StatusDto status)
    {
        // Evento dal thread dell'Engine: si torna sul thread dell'interfaccia.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_attached)
            {
                RenderStatus(status);
            }
        }));
    }

    // ---- Azioni ----

    private async Task SearchAsync()
    {
        if (_busy)
        {
            return;
        }

        CancellationToken token = _lifetime.Token;
        SetBusy(true, _texts.CameraSearching);
        try
        {
            IReadOnlyList<CameraCandidate> found = await Task.Run(() => _service.DiscoverCamerasAsync(token), token);
            _candidates = found;
            _searchedOnce = true;
            _lastError = null;
            RenderList();
        }
        catch (OperationCanceledException)
        {
            // Vista chiusa durante la ricerca: nessun errore da mostrare.
        }
        catch (Exception ex)
        {
            _lastError = CameraErrorFormatter.Describe(ex.Message, _texts);
        }
        finally
        {
            SetBusy(false, null);
            RenderErrors();
        }
    }

    private async Task ConnectAsync(CameraCandidate candidate)
    {
        SetBusy(true, _texts.CameraConnecting);
        try
        {
            // Nessun token di annullamento: vedi OnUnloaded.
            ConnectResult result = await Task.Run(() => _service.ConnectCameraAsync(candidate.Id, CancellationToken.None));
            if (result.Ok)
            {
                _lastError = null;
                _infoText.Text = string.Empty;
            }
            else
            {
                _lastError = CameraErrorFormatter.Describe(result.Error, _texts);
            }
        }
        catch (Exception ex)
        {
            _lastError = CameraErrorFormatter.Describe(ex.Message, _texts);
        }
        finally
        {
            SetBusy(false, null);
            RenderStatus(_service.GetStatus());
        }
    }

    private async Task DisconnectAsync()
    {
        if (_busy)
        {
            return;
        }

        _infoText.Text = string.Empty;
        _successText.Text = string.Empty;
        SetBusy(true, _texts.CameraDiscBusy);
        try
        {
            // Nessun token di annullamento, come per il collegamento: vedi OnUnloaded.
            await Task.Run(() => _service.DisconnectCameraAsync(CancellationToken.None));
            _lastError = null;
            _successText.Text = _texts.CameraDiscDone;
        }
        catch (Exception ex)
        {
            _lastError = CameraErrorFormatter.Describe(ex.Message, _texts);
        }
        finally
        {
            SetBusy(false, null);
            RenderStatus(_service.GetStatus());
        }
    }

    private async Task SaveAsync()
    {
        if (_busy)
        {
            return;
        }

        CameraSettingsCheck check = RenderProblems();
        _successText.Text = string.Empty;
        _infoText.Text = string.Empty;
        if (!check.IsValid)
        {
            return;
        }

        // Si legge la configurazione corrente al momento del salvataggio: il form può essere cambiato da altre viste.
        AllSky.Core.AppConfig current = _service.GetConfig();
        if (!CameraSettingsForm.HasChanges(check, current.Camera.Gain, current.Camera.MaxExposureMs))
        {
            _infoText.Text = _texts.CameraNoChanges;
            return;
        }

        AllSky.Core.AppConfig updated = current with
        {
            Camera = current.Camera with
            {
                Gain = check.Gain!.Value,
                MaxExposureMs = check.MaxExposureMs!.Value,
            },
        };

        SetBusy(true, null);
        try
        {
            ConfigUpdateResult result = await Task.Run(() => _service.UpdateConfig(updated));
            if (result.Ok)
            {
                _lastError = null;
                _successText.Text = _texts.CameraSaved;
                ShowSettings(_service.GetConfig());
            }
            else
            {
                _lastError = CameraErrorFormatter.DescribeValidation(result.Errors, _texts);
            }
        }
        catch (Exception ex)
        {
            _lastError = CameraErrorFormatter.Describe(ex.Message, _texts);
        }
        finally
        {
            SetBusy(false, null);
            RenderErrors();
        }
    }

    // ---- Rendering ----

    private void RenderList()
    {
        CameraSourceChoice choice = CurrentChoice();

        _listBox.Items.Clear();
        int shown = 0;
        foreach (CameraCandidate candidate in _candidates)
        {
            if (!CameraSources.Matches(choice, candidate.Kind))
            {
                continue;
            }

            string kindLabel = CameraSources.KindLabel(candidate.Kind, _texts);
            var item = new ListBoxItem
            {
                Tag = candidate,
                Content = BuildCandidateContent(candidate, kindLabel),
                Style = ListItemStyle(),
            };
            AutomationProperties.SetName(item, candidate.Name + ", " + kindLabel);
            _listBox.Items.Add(item);
            shown++;
        }

        if (!_searchedOnce)
        {
            _listHint.Visibility = Visibility.Collapsed;
        }
        else if (shown == 0)
        {
            string? specific = CameraSources.EmptyHint(choice, _texts);
            _listHint.Text = specific is null ? _texts.CameraNoneFound : _texts.CameraNoneFound + " " + specific;
            _listHint.Visibility = Visibility.Visible;
        }
        else
        {
            _listHint.Visibility = Visibility.Collapsed;
        }

        UpdateConnectEnabled();
    }

    private void RenderStatus(AllSky.Engine.StatusDto status)
    {
        AllSky.Engine.CameraStatusDto camera = status.Camera;
        _connected = camera.Connected;
        UpdateConnectEnabled();

        string? warning = status.Warning;
        _warningText.Text = warning is null ? string.Empty : CameraErrorFormatter.Describe(warning, _texts).Message;
        _warningText.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;
        _connectedText.Text = camera.Connected
            ? string.Format(
                _texts.Culture,
                _texts.CameraConnectedAs,
                string.IsNullOrWhiteSpace(camera.Name) ? camera.Id : camera.Name,
                CameraSources.KindLabel(camera.Kind, _texts))
            : _texts.CameraNoneConnected;

        _statusError = status.Error;
        RenderErrors();
    }

    private void RenderErrors()
    {
        CameraErrorView? view = _lastError
            ?? (string.IsNullOrWhiteSpace(_statusError) ? null : CameraErrorFormatter.Describe(_statusError, _texts));

        if (view is null)
        {
            _errorText.Text = string.Empty;
            _detailText.Text = string.Empty;
            _errorText.Visibility = Visibility.Collapsed;
            _detailText.Visibility = Visibility.Collapsed;
            return;
        }

        _errorText.Text = _texts.ErrorLabel + ": " + view.Message;
        _errorText.Visibility = Visibility.Visible;
        _detailText.Text = view.Detail is null ? string.Empty : string.Format(_texts.Culture, _texts.CameraErrorDetail, view.Detail);
        _detailText.Visibility = view.Detail is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private CameraSettingsCheck RenderProblems()
    {
        CameraSettingsCheck check = CameraSettingsForm.Check(_gainBox.Text, _exposureBox.Text, _texts.Culture);
        SetProblem(_gainProblem, CameraFieldMessages.For(check.GainProblem, _texts));
        SetProblem(_exposureProblem, CameraFieldMessages.For(check.MaxExposureProblem, _texts));
        return check;
    }

    private void ShowSettings(AllSky.Core.AppConfig config)
    {
        _gainBox.Text = CameraSettingsForm.FormatGain(config.Camera.Gain);
        _exposureBox.Text = CameraSettingsForm.FormatExposure(config.Camera.MaxExposureMs, _texts.Culture);
        RenderProblems();
    }

    private void SetBusy(bool busy, string? progress)
    {
        _busy = busy;
        _searchButton.IsEnabled = !busy;
        _saveButton.IsEnabled = !busy;
        _progressText.Text = progress ?? string.Empty;
        _progressText.Visibility = string.IsNullOrEmpty(progress) ? Visibility.Collapsed : Visibility.Visible;
        UpdateConnectEnabled();
    }

    private void UpdateConnectEnabled()
    {
        _connectButton.IsEnabled = !_busy && _listBox.SelectedItem is ListBoxItem;
        _disconnectButton.IsEnabled = !_busy && _connected;
    }

    private CameraSourceChoice CurrentChoice()
    {
        return _sourceBox.SelectedItem is ComboBoxItem { Tag: CameraSourceChoice choice }
            ? choice
            : CameraSourceChoice.All;
    }

    private static void SetProblem(TextBlock block, string? text)
    {
        block.Text = text ?? string.Empty;
        block.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private Style ListItemStyle() => _listItemStyle ??= (Style)XamlReader.Parse(ListItemStyleXaml);

    private UIElement BuildCandidateContent(CameraCandidate candidate, string kindLabel)
    {
        var stack = new StackPanel();

        var name = new TextBlock { Text = candidate.Name, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        name.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        var kind = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(candidate.Detail) ? kindLabel : kindLabel + " · " + candidate.Detail,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        kind.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        kind.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");

        stack.Children.Add(name);
        stack.Children.Add(kind);
        return stack;
    }

    // ---- Layout ----

    private UIElement BuildLayout()
    {
        var header = new TextBlock
        {
            Text = _texts.NavCamera,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        header.SetResourceReference(TextBlock.FontSizeProperty, "Size.Display");

        var errors = BuildErrorBlock();

        var left = new StackPanel();
        left.Children.Add(BuildSearchPanel());
        left.Children.Add(BuildConnectionPanel());

        var right = new StackPanel();
        right.Children.Add(BuildSettingsPanel());
        right.Children.Add(BuildCalibrationPanel());

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        columns.Children.Add(left);
        columns.Children.Add(right);

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(errors);
        body.Children.Add(columns);

        // Lo scorrimento evita tagli sulle finestre basse; la larghezza segue la vista.
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };
    }

    private UIElement BuildErrorBlock()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

        _errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        _errorText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");
        _errorText.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        _detailText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
        _detailText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _detailText.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");

        stack.Children.Add(_errorText);
        stack.Children.Add(_detailText);
        return stack;
    }

    private Border BuildSearchPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.CameraSearchTitle));

        var sourceLabel = BuildLabel(_texts.CameraSourceLabel);
        _sourceBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12), MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        _sourceBox.SetResourceReference(Control.BackgroundProperty, "Brush.Surface2");
        _sourceBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _sourceBox.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        AutomationProperties.SetName(_sourceBox, _texts.CameraSourceLabel);
        foreach (CameraSourceChoice choice in CameraSources.Choices)
        {
            _sourceBox.Items.Add(new ComboBoxItem { Content = CameraSources.ChoiceLabel(choice, _texts), Tag = choice });
        }

        _sourceBox.SelectedIndex = 0;
        _sourceBox.SelectionChanged += (_, _) => RenderList();

        _searchButton = BuildButton(_texts.CameraSearch, primary: false);
        _searchButton.Click += async (_, _) => await SearchAsync();

        _listBox = new ListBox
        {
            Height = 220,
            Margin = new Thickness(0, 12, 0, 0),
            Background = null,
        };
        _listBox.SetResourceReference(Control.BackgroundProperty, "Brush.Surface1");
        _listBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _listBox.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        AutomationProperties.SetName(_listBox, _texts.CameraFoundLabel);
        _listBox.SelectionChanged += (_, _) => UpdateConnectEnabled();

        _listHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        _listHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _listHint.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        stack.Children.Add(sourceLabel);
        stack.Children.Add(_sourceBox);
        stack.Children.Add(_searchButton);
        stack.Children.Add(_listBox);
        stack.Children.Add(_listHint);
        return BuildPanel(stack, _texts.CameraSearchTitle);
    }

    private Border BuildConnectionPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.CameraConnectionTitle));

        _connectButton = BuildButton(_texts.CameraConnect, primary: true);
        _connectButton.IsEnabled = false;
        _connectButton.Click += async (_, _) => await OnConnectClickAsync();

        _progressText = BuildBodyText(string.Empty, "Brush.TextSecondary");
        _progressText.Visibility = Visibility.Collapsed;

        _connectedText = BuildBodyText(_texts.CameraNoneConnected, "Brush.Text");
        _connectedText.Margin = new Thickness(0, 12, 0, 0);

        _disconnectButton = BuildButton(_texts.CameraDiscButton, primary: false);
        _disconnectButton.Margin = new Thickness(0, 12, 0, 0);
        _disconnectButton.IsEnabled = false;
        _disconnectButton.Click += async (_, _) => await DisconnectAsync();

        // Avviso non bloccante (es. limiti di esposizione sanificati): nota gialla, la cattura continua.
        _warningText = BuildBodyText(string.Empty, "Brush.Warning");
        _warningText.Margin = new Thickness(0, 12, 0, 0);
        _warningText.Visibility = Visibility.Collapsed;

        _infoText = BuildBodyText(string.Empty, "Brush.TextSecondary");
        _successText = BuildBodyText(string.Empty, "Brush.Success");

        stack.Children.Add(_connectButton);
        stack.Children.Add(_progressText);
        stack.Children.Add(_connectedText);
        stack.Children.Add(_disconnectButton);
        stack.Children.Add(_warningText);
        stack.Children.Add(_infoText);
        stack.Children.Add(_successText);
        return BuildPanel(stack, _texts.CameraConnectionTitle);
    }

    private Border BuildSettingsPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.CameraSettingsTitle));

        stack.Children.Add(BuildLabel(_texts.CameraGainLabel));
        _gainBox = BuildTextBox(_texts.CameraGainLabel);
        _gainBox.TextChanged += (_, _) => RenderProblems();
        stack.Children.Add(_gainBox);
        stack.Children.Add(BuildBodyText(_texts.CameraGainHint, "Brush.TextTertiary", "Size.Caption"));
        _gainProblem = BuildBodyText(string.Empty, "Brush.Error", "Size.Body");
        stack.Children.Add(_gainProblem);

        stack.Children.Add(BuildLabel(_texts.CameraMaxExposureLabel));
        _exposureBox = BuildTextBox(_texts.CameraMaxExposureLabel);
        _exposureBox.TextChanged += (_, _) => RenderProblems();
        stack.Children.Add(_exposureBox);
        stack.Children.Add(BuildBodyText(_texts.CameraMaxExposureHint, "Brush.TextTertiary", "Size.Caption"));
        _exposureProblem = BuildBodyText(string.Empty, "Brush.Error", "Size.Body");
        stack.Children.Add(_exposureProblem);

        _saveButton = BuildButton(_texts.CameraSave, primary: true);
        _saveButton.Margin = new Thickness(0, 16, 0, 0);
        _saveButton.Click += async (_, _) => await SaveAsync();
        stack.Children.Add(_saveButton);

        return BuildPanel(stack, _texts.CameraSettingsTitle);
    }

    private Border BuildCalibrationPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.CameraCalibrationTitle));
        stack.Children.Add(BuildBodyText(_texts.CameraCalibrationEmpty, "Brush.TextSecondary"));

        var panel = BuildPanel(stack, _texts.CameraCalibrationTitle);
        panel.Margin = new Thickness(0, 16, 0, 0);
        return panel;
    }

    private async Task OnConnectClickAsync()
    {
        if (_busy)
        {
            return;
        }

        if (_listBox.SelectedItem is not ListBoxItem { Tag: CameraCandidate candidate })
        {
            _successText.Text = string.Empty;
            _infoText.Text = _texts.CameraSelectFirst;
            return;
        }

        _infoText.Text = string.Empty;
        _successText.Text = string.Empty;
        await ConnectAsync(candidate);
    }

    // ---- Costruttori di controlli ----

    private Border BuildPanel(UIElement content, string name)
    {
        var panel = new Border
        {
            Child = content,
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16),
            VerticalAlignment = VerticalAlignment.Top,
        };
        panel.SetResourceReference(FrameworkElement.StyleProperty, "Panel");
        AutomationProperties.SetName(panel, name);
        return panel;
    }

    private TextBlock BuildPanelTitle(string text)
    {
        var title = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(FrameworkElement.StyleProperty, "PanelTitle");
        return title;
    }

    private TextBlock BuildLabel(string text)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        label.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        return label;
    }

    private TextBlock BuildBodyText(string text, string brushKey, string sizeKey = "Size.Body")
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        block.SetResourceReference(TextBlock.FontSizeProperty, sizeKey);
        return block;
    }

    private TextBox BuildTextBox(string accessibleName)
    {
        var box = new TextBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(box, accessibleName);
        return box;
    }

    private Button BuildButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 32,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, primary ? "Button.Primary" : "Button.Secondary");
        AutomationProperties.SetName(button, text);
        return button;
    }
}
