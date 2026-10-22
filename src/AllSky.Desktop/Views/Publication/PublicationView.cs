using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using AllSky.Core;
using AllSky.Core.Publication;
using AllSky.Core.YouTube;

namespace AllSky.Desktop.Views.Publication;

/// <summary>
/// Vista "Pubblicazione": impostazioni dell'Uplink (server, dispositivo, intervallo, qualità, token), stato della coda,
/// prova di connessione e il pannello YouTube (caricamento automatico, P7.4).
/// </summary>
/// <remarks>
/// <para>
/// Il token non viene mai riletto né mostrato. Il campo serve solo a digitarne uno nuovo: si legge al salvataggio o alla
/// prova, e si svuota dopo un salvataggio riuscito. Nello stato compare solo "impostato" o "non impostato".
/// </para>
/// <para>
/// Salvataggio e prova girano fuori dal thread dell'interfaccia. La logica pura sta in <c>AllSky.Core/Publication</c>.
/// Qui restano i controlli WPF e le chiamate a <see cref="IPublishSettingsService"/>.
/// </para>
/// <para>
/// Lo stato si aggiorna con l'evento <see cref="IPublishSettingsService.StatusChanged"/> e con un controllo periodico, così
/// le scadenze e lo stato "invio in corso" restano corretti anche se l'evento manca.
/// </para>
/// </remarks>
public class PublicationView : UserControl
{
    /// <summary>Intervallo del controllo periodico dello stato, mentre la vista è visibile.</summary>
    private static readonly TimeSpan StatusRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly IPublishSettingsService _service;
    private readonly IYouTubeService? _youTube;
    private readonly DesktopTexts _texts;
    private readonly DispatcherTimer _refreshTimer;

    // Impostazioni.
    private CheckBox _enabledBox = null!;
    private TextBox _serverUrlBox = null!;
    private TextBox _deviceIdBox = null!;
    private TextBox _profileNameBox = null!;
    private TextBox _intervalBox = null!;
    private TextBox _longEdgeBox = null!;
    private TextBox _jpegQualityBox = null!;
    private TextBlock _intervalProblem = null!;
    private TextBlock _longEdgeProblem = null!;
    private TextBlock _jpegQualityProblem = null!;
    private TextBlock _tokenStatusText = null!;
    private PasswordBox _tokenBox = null!;
    private CheckBox _clearTokenBox = null!;
    private TextBox _extraHeaderNameBox = null!;
    private TextBlock _extraHeaderStatusText = null!;
    private PasswordBox _extraHeaderValueBox = null!;
    private CheckBox _clearExtraHeaderBox = null!;
    private StackPanel _issuesPanel = null!;
    private Button _saveButton = null!;
    private TextBlock _saveProgress = null!;
    private TextBlock _saveResult = null!;

    // Stato.
    private TextBlock _stateText = null!;
    private TextBlock _stateHint = null!;
    private TextBlock _countersText = null!;
    private TextBlock _lastSuccessText = null!;
    private TextBlock _nextAttemptText = null!;
    private TextBlock _lastErrorText = null!;

    // Prova di connessione.
    private Button _testButton = null!;
    private TextBlock _testProgress = null!;
    private TextBlock _testResult = null!;

    private bool _busy;
    private bool _attached;
    private CancellationTokenSource _lifetime = new();

    /// <summary>Crea la vista con i testi in italiano.</summary>
    /// <param name="service">Accesso alla pubblicazione (adattatore sopra i servizi dell'Host).</param>
    public PublicationView(IPublishSettingsService service)
        : this(service, DesktopTexts.Italian)
    {
    }

    /// <summary>Crea la vista con i testi nella lingua scelta. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="service">Accesso alla pubblicazione.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    /// <param name="youTube">Servizio YouTube. Null: la vista non mostra il pannello YouTube.</param>
    public PublicationView(IPublishSettingsService service, DesktopTexts texts, IYouTubeService? youTube = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(texts);

        _service = service;
        _texts = texts;
        _youTube = youTube;
        _refreshTimer = new DispatcherTimer { Interval = StatusRefreshInterval };
        _refreshTimer.Tick += (_, _) =>
        {
            if (_attached)
            {
                RenderStatus(_service.GetStatus());
            }
        };

        Content = BuildLayout();
        AutomationProperties.SetName(this, texts.NavPublication);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ---- Ciclo di vita ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _service.StatusChanged += OnStatusChanged;

        ShowSettings(_service.GetSettings());
        RenderStatus(_service.GetStatus());
        _refreshTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _attached = false;
        _service.StatusChanged -= OnStatusChanged;
        _refreshTimer.Stop();

        // Si annulla solo la prova. Un salvataggio in corso non si interrompe: lascerebbe le impostazioni a metà.
        _lifetime.Cancel();
        _lifetime = new CancellationTokenSource();
    }

    private void OnStatusChanged(PublicationStatusSnapshot status)
    {
        // Evento dal thread della pubblicazione: si torna sul thread dell'interfaccia.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_attached)
            {
                RenderStatus(status);
            }
        }));
    }

    // ---- Azioni ----

    private async Task SaveAsync()
    {
        if (_busy)
        {
            return;
        }

        ClearIssues();
        SetOptional(_saveResult, null);

        PublicationFormCheck check = RenderProblems();
        if (!check.IsValid)
        {
            ShowSaveMessage(_texts.PublicationSaveFailed, "Brush.Error");
            return;
        }

        PublicationSettingsChange change = BuildChange(check);

        // Le impostazioni correnti si leggono al momento del salvataggio: possono essere cambiate altrove.
        if (!PublicationSettingsForm.HasChanges(change, _service.GetSettings()))
        {
            ShowSaveMessage(_texts.PublicationNoChanges, "Brush.TextSecondary");
            return;
        }

        SetBusy(true);
        SetProgress(_saveProgress, _texts.PublicationSaving);
        try
        {
            PublicationSaveResult result = await Task.Run(() => _service.SaveSettings(change));
            if (result.Ok)
            {
                // Il segreto digitato non deve restare nel campo: il valore salvato non si rilegge mai.
                _tokenBox.Clear();
                _extraHeaderValueBox.Clear();
                ShowSettings(_service.GetSettings());
                RenderStatus(_service.GetStatus());
                ShowSaveMessage(_texts.PublicationSaved, "Brush.Success");
            }
            else
            {
                ShowIssues(result.Issues);
                ShowSaveMessage(_texts.PublicationSaveFailed, "Brush.Error");
            }
        }
        catch (Exception)
        {
            // Nessun dettaglio dell'eccezione in interfaccia: potrebbe contenere valori dei campi.
            ShowSaveMessage(_texts.PublicationSaveFailed, "Brush.Error");
        }
        finally
        {
            SetBusy(false);
            SetProgress(_saveProgress, null);
        }
    }

    private async Task TestAsync()
    {
        if (_busy)
        {
            return;
        }

        _testResult.Text = string.Empty;
        PublicationFormCheck check = RenderProblems();
        if (!check.IsValid)
        {
            ShowTestResult(PublicationStatusFormatter.TestText(
                new PublicationTestResult(false, "invalid_settings", string.Empty, null), _texts), ok: false);
            return;
        }

        PublicationSettingsChange change = BuildChange(check);
        CancellationToken token = _lifetime.Token;

        SetBusy(true);
        SetProgress(_testProgress, _texts.PublicationTesting);
        try
        {
            PublicationTestResult result = await Task.Run(() => _service.TestConnectionAsync(change, token), token);
            ShowTestResult(PublicationStatusFormatter.TestText(result, _texts), result.Ok);
        }
        catch (OperationCanceledException)
        {
            // Vista chiusa durante la prova: nessun esito da mostrare.
        }
        catch (Exception)
        {
            ShowTestResult(PublicationStatusFormatter.TestText(
                new PublicationTestResult(false, "unknown", string.Empty, null), _texts), ok: false);
        }
        finally
        {
            SetBusy(false);
            SetProgress(_testProgress, null);
        }
    }

    /// <summary>
    /// Legge il modulo in una modifica. Il token si legge qui e basta: vuoto significa "non cambiare".
    /// Il valore non viene mai scritto in un controllo o in un log.
    /// </summary>
    private PublicationSettingsChange BuildChange(PublicationFormCheck check)
    {
        return new PublicationSettingsChange(
            Enabled: _enabledBox.IsChecked == true,
            ServerUrl: PublicationSettingsForm.Trim(_serverUrlBox.Text),
            DeviceId: PublicationSettingsForm.Trim(_deviceIdBox.Text),
            ProfileName: PublicationSettingsForm.TrimOrNull(_profileNameBox.Text),
            IntervalSeconds: check.IntervalSeconds.Value ?? PublicationSettingsForm.IntervalDefault,
            LongEdgePx: check.LongEdgePx.Value ?? PublicationSettingsForm.LongEdgeDefault,
            JpegQuality: check.JpegQuality.Value ?? PublicationSettingsForm.JpegQualityDefault,
            ExtraHeaderName: PublicationSettingsForm.TrimOrNull(_extraHeaderNameBox.Text),
            NewToken: PublicationSettingsForm.TrimOrNull(_tokenBox.Password),
            ClearToken: _clearTokenBox.IsChecked == true,
            NewExtraHeaderValue: PublicationSettingsForm.TrimOrNull(_extraHeaderValueBox.Password),
            ClearExtraHeaderValue: _clearExtraHeaderBox.IsChecked == true);
    }

    // ---- Rendering ----

    private void ShowSettings(PublicationSettingsSnapshot settings)
    {
        _enabledBox.IsChecked = settings.Enabled;
        _serverUrlBox.Text = settings.ServerUrl;
        _deviceIdBox.Text = settings.DeviceId;
        _profileNameBox.Text = settings.ProfileName ?? string.Empty;
        _intervalBox.Text = settings.IntervalSeconds.ToString(CultureInfo.InvariantCulture);
        _longEdgeBox.Text = settings.LongEdgePx.ToString(CultureInfo.InvariantCulture);
        _jpegQualityBox.Text = settings.JpegQuality.ToString(CultureInfo.InvariantCulture);

        _tokenStatusText.Text = PublicationStatusFormatter.SecretStatus(settings.HasToken, _texts);
        _clearTokenBox.IsChecked = false;
        _clearTokenBox.IsEnabled = settings.HasToken;

        _extraHeaderNameBox.Text = settings.ExtraHeaderName ?? string.Empty;
        _extraHeaderStatusText.Text = PublicationStatusFormatter.SecretStatus(settings.HasExtraHeaderValue, _texts);
        _clearExtraHeaderBox.IsChecked = false;
        _clearExtraHeaderBox.IsEnabled = settings.HasExtraHeaderValue;

        RenderProblems();
    }

    private void RenderStatus(PublicationStatusSnapshot status)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        _stateText.Text = PublicationStatusFormatter.StateText(status.State, _texts);
        _stateText.SetResourceReference(TextBlock.ForegroundProperty, StateBrushKey(status.State));

        SetOptional(_stateHint, status.State == PublicationState.Disabled ? _texts.PublicationStateDisabledHint : null);
        _countersText.Text = PublicationStatusFormatter.Counters(status, _texts);
        _lastSuccessText.Text = PublicationStatusFormatter.LastSuccess(status.LastSuccessUtc, now, _texts);
        SetOptional(_nextAttemptText, PublicationStatusFormatter.NextAttempt(status.NextAttemptUtc, now, _texts));
        SetOptional(_lastErrorText, PublicationStatusFormatter.LastError(status, now, _texts));
    }

    /// <summary>Colore dello stato. Il testo dice sempre lo stato: il colore non è l'unica informazione.</summary>
    private static string StateBrushKey(PublicationState state) => state switch
    {
        PublicationState.Idle => "Brush.Success",
        PublicationState.Sending => "Brush.Success",
        PublicationState.Backoff => "Brush.Warning",
        PublicationState.Suspended => "Brush.Error",
        _ => "Brush.Text",
    };

    /// <summary>Mostra i problemi dei campi numerici sotto ciascun campo. Ritorna l'esito della validazione.</summary>
    private PublicationFormCheck RenderProblems()
    {
        PublicationFormCheck check = PublicationSettingsForm.Check(
            _intervalBox.Text, _longEdgeBox.Text, _jpegQualityBox.Text, _texts.Culture);

        SetOptional(_intervalProblem, PublicationFieldMessages.ForNumber(
            check.IntervalSeconds.Problem, PublicationSettingsForm.IntervalMin, PublicationSettingsForm.IntervalMax, _texts));
        SetOptional(_longEdgeProblem, PublicationFieldMessages.ForNumber(
            check.LongEdgePx.Problem, PublicationSettingsForm.LongEdgeMin, PublicationSettingsForm.LongEdgeMax, _texts));
        SetOptional(_jpegQualityProblem, PublicationFieldMessages.ForNumber(
            check.JpegQuality.Problem, PublicationSettingsForm.JpegQualityMin, PublicationSettingsForm.JpegQualityMax, _texts));
        return check;
    }

    private void ShowIssues(IReadOnlyList<PublicationIssue> issues)
    {
        _issuesPanel.Children.Clear();
        foreach (PublicationIssue issue in issues)
        {
            _issuesPanel.Children.Add(BuildBodyText(PublicationFieldMessages.IssueLine(issue, _texts), "Brush.Error"));
        }

        _issuesPanel.Visibility = issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearIssues()
    {
        _issuesPanel.Children.Clear();
        _issuesPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowSaveMessage(string text, string brushKey)
    {
        _saveResult.Text = text;
        _saveResult.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        _saveResult.Visibility = Visibility.Visible;
    }

    private void ShowTestResult(string text, bool ok)
    {
        _testResult.Text = text;
        _testResult.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Brush.Success" : "Brush.Error");
        _testResult.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _saveButton.IsEnabled = !busy;
        _testButton.IsEnabled = !busy;
    }

    private static void SetProgress(TextBlock block, string? text)
    {
        block.Text = text ?? string.Empty;
        block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void SetOptional(TextBlock block, string? text)
    {
        block.Text = text ?? string.Empty;
        block.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Layout ----

    private UIElement BuildLayout()
    {
        var header = new TextBlock
        {
            Text = _texts.NavPublication,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        header.SetResourceReference(TextBlock.FontSizeProperty, "Size.Display");

        var left = new StackPanel();
        left.Children.Add(BuildStatusPanel());
        left.Children.Add(BuildTestPanel());

        var right = new StackPanel();
        right.Children.Add(BuildSettingsPanel());

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
        body.Children.Add(columns);
        if (_youTube is not null)
        {
            body.Children.Add(new YouTubePanel(_youTube, _texts));
        }

        // Lo scorrimento evita tagli sulle finestre basse; la larghezza segue la vista.
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };
    }

    private Border BuildStatusPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.PublicationStatusTitle));

        _stateText = BuildBodyText(_texts.PublicationStateUnknown, "Brush.Text", "Size.Subtitle");
        _stateHint = BuildBodyText(string.Empty, "Brush.TextSecondary", "Size.Caption");
        _stateHint.Visibility = Visibility.Collapsed;
        _countersText = BuildBodyText(string.Empty, "Brush.TextSecondary");
        _countersText.Margin = new Thickness(0, 12, 0, 0);
        _lastSuccessText = BuildBodyText(string.Empty, "Brush.Text");
        _nextAttemptText = BuildBodyText(string.Empty, "Brush.TextSecondary");
        _nextAttemptText.Visibility = Visibility.Collapsed;
        _lastErrorText = BuildBodyText(string.Empty, "Brush.Error");
        _lastErrorText.Visibility = Visibility.Collapsed;

        stack.Children.Add(_stateText);
        stack.Children.Add(_stateHint);
        stack.Children.Add(_countersText);
        stack.Children.Add(_lastSuccessText);
        stack.Children.Add(_nextAttemptText);
        stack.Children.Add(_lastErrorText);
        return BuildPanel(stack, _texts.PublicationStatusTitle);
    }

    private Border BuildTestPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.PublicationTest));
        stack.Children.Add(BuildBodyText(_texts.PublicationTestNote, "Brush.TextSecondary", "Size.Caption"));

        _testButton = BuildButton(_texts.PublicationTest, primary: false);
        _testButton.Click += async (_, _) => await TestAsync();
        stack.Children.Add(_testButton);

        _testProgress = BuildBodyText(string.Empty, "Brush.TextSecondary");
        _testProgress.Visibility = Visibility.Collapsed;
        _testResult = BuildBodyText(string.Empty, "Brush.Text");
        _testResult.Visibility = Visibility.Collapsed;
        stack.Children.Add(_testProgress);
        stack.Children.Add(_testResult);

        var panel = BuildPanel(stack, _texts.PublicationTest);
        panel.Margin = new Thickness(0, 16, 0, 0);
        return panel;
    }

    private Border BuildSettingsPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.PublicationSettingsTitle));

        _enabledBox = new CheckBox { Content = _texts.PublicationEnabled, Margin = new Thickness(0, 4, 0, 8) };
        _enabledBox.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Brush.Text");
        AutomationProperties.SetName(_enabledBox, _texts.PublicationEnabled);
        stack.Children.Add(_enabledBox);

        stack.Children.Add(BuildLabel(_texts.PublicationServerUrl));
        _serverUrlBox = BuildTextBox(_texts.PublicationServerUrl, 340);
        stack.Children.Add(_serverUrlBox);
        stack.Children.Add(BuildBodyText(_texts.PublicationServerUrlHint, "Brush.TextTertiary", "Size.Caption"));

        stack.Children.Add(BuildLabel(_texts.PublicationDeviceId));
        _deviceIdBox = BuildTextBox(_texts.PublicationDeviceId, 240);
        stack.Children.Add(_deviceIdBox);
        stack.Children.Add(BuildBodyText(_texts.PublicationDeviceIdHint, "Brush.TextTertiary", "Size.Caption"));

        stack.Children.Add(BuildLabel(_texts.PublicationProfileName));
        _profileNameBox = BuildTextBox(_texts.PublicationProfileName, 240);
        stack.Children.Add(_profileNameBox);

        stack.Children.Add(BuildLabel(_texts.PublicationInterval));
        _intervalBox = BuildTextBox(_texts.PublicationInterval, 120);
        _intervalBox.TextChanged += (_, _) => RenderProblems();
        stack.Children.Add(_intervalBox);
        stack.Children.Add(BuildBodyText(
            string.Format(CultureInfo.InvariantCulture, _texts.PublicationRangeHint,
                PublicationSettingsForm.IntervalMin, PublicationSettingsForm.IntervalMax, PublicationSettingsForm.IntervalDefault),
            "Brush.TextTertiary", "Size.Caption"));
        _intervalProblem = BuildBodyText(string.Empty, "Brush.Error");
        _intervalProblem.Visibility = Visibility.Collapsed;
        stack.Children.Add(_intervalProblem);

        stack.Children.Add(BuildLabel(_texts.PublicationLongEdge));
        _longEdgeBox = BuildTextBox(_texts.PublicationLongEdge, 120);
        _longEdgeBox.TextChanged += (_, _) => RenderProblems();
        stack.Children.Add(_longEdgeBox);
        stack.Children.Add(BuildBodyText(
            string.Format(CultureInfo.InvariantCulture, _texts.PublicationRangeHint,
                PublicationSettingsForm.LongEdgeMin, PublicationSettingsForm.LongEdgeMax, PublicationSettingsForm.LongEdgeDefault),
            "Brush.TextTertiary", "Size.Caption"));
        _longEdgeProblem = BuildBodyText(string.Empty, "Brush.Error");
        _longEdgeProblem.Visibility = Visibility.Collapsed;
        stack.Children.Add(_longEdgeProblem);

        stack.Children.Add(BuildLabel(_texts.PublicationJpegQuality));
        _jpegQualityBox = BuildTextBox(_texts.PublicationJpegQuality, 120);
        _jpegQualityBox.TextChanged += (_, _) => RenderProblems();
        stack.Children.Add(_jpegQualityBox);
        stack.Children.Add(BuildBodyText(
            string.Format(CultureInfo.InvariantCulture, _texts.PublicationRangeHint,
                PublicationSettingsForm.JpegQualityMin, PublicationSettingsForm.JpegQualityMax, PublicationSettingsForm.JpegQualityDefault),
            "Brush.TextTertiary", "Size.Caption"));
        _jpegQualityProblem = BuildBodyText(string.Empty, "Brush.Error");
        _jpegQualityProblem.Visibility = Visibility.Collapsed;
        stack.Children.Add(_jpegQualityProblem);

        // Token: il valore non si legge né si mostra. Il campo è vuoto e serve solo a digitarne uno nuovo.
        stack.Children.Add(BuildLabel(_texts.PublicationTokenLabel));
        _tokenStatusText = BuildBodyText(string.Empty, "Brush.TextSecondary");
        stack.Children.Add(_tokenStatusText);
        stack.Children.Add(BuildLabel(_texts.PublicationSecretReplace));
        _tokenBox = BuildPasswordBox(_texts.PublicationSecretReplace);
        stack.Children.Add(_tokenBox);
        stack.Children.Add(BuildBodyText(_texts.PublicationSecretHint, "Brush.TextTertiary", "Size.Caption"));
        _clearTokenBox = BuildCheckBox(_texts.PublicationClearToken);
        stack.Children.Add(_clearTokenBox);

        // Header HTTP aggiuntivo: il nome è normale, il valore è un segreto come il token.
        stack.Children.Add(BuildLabel(_texts.PublicationExtraHeaderName));
        _extraHeaderNameBox = BuildTextBox(_texts.PublicationExtraHeaderName, 240);
        stack.Children.Add(_extraHeaderNameBox);

        stack.Children.Add(BuildLabel(_texts.PublicationExtraHeaderValue));
        _extraHeaderStatusText = BuildBodyText(string.Empty, "Brush.TextSecondary");
        stack.Children.Add(_extraHeaderStatusText);
        _extraHeaderValueBox = BuildPasswordBox(_texts.PublicationExtraHeaderValue);
        stack.Children.Add(_extraHeaderValueBox);
        _clearExtraHeaderBox = BuildCheckBox(_texts.PublicationClearExtraHeader);
        stack.Children.Add(_clearExtraHeaderBox);

        _issuesPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
        stack.Children.Add(_issuesPanel);

        _saveButton = BuildButton(_texts.PublicationSave, primary: true);
        _saveButton.Margin = new Thickness(0, 16, 0, 0);
        _saveButton.Click += async (_, _) => await SaveAsync();
        stack.Children.Add(_saveButton);

        _saveProgress = BuildBodyText(string.Empty, "Brush.TextSecondary");
        _saveProgress.Visibility = Visibility.Collapsed;
        _saveResult = BuildBodyText(string.Empty, "Brush.Text");
        _saveResult.Visibility = Visibility.Collapsed;
        stack.Children.Add(_saveProgress);
        stack.Children.Add(_saveResult);

        ShowSettingsPlaceholders();
        return BuildPanel(stack, _texts.PublicationSettingsTitle);
    }

    /// <summary>Stato iniziale dei campi testuali prima del primo caricamento (evita testi vuoti non localizzati).</summary>
    private void ShowSettingsPlaceholders()
    {
        _tokenStatusText.Text = PublicationStatusFormatter.SecretStatus(false, _texts);
        _extraHeaderStatusText.Text = PublicationStatusFormatter.SecretStatus(false, _texts);
        _clearTokenBox.IsEnabled = false;
        _clearExtraHeaderBox.IsEnabled = false;
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

    private TextBox BuildTextBox(string accessibleName, double width)
    {
        var box = new TextBox { Width = width, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(box, accessibleName);
        return box;
    }

    /// <summary>Campo per i segreti. Nessun valore viene mai scritto nel controllo dall'applicazione.</summary>
    private PasswordBox BuildPasswordBox(string accessibleName)
    {
        var box = new PasswordBox { Width = 340, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        box.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Brush.Surface2");
        box.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Brush.Border");
        box.SetResourceReference(System.Windows.Controls.Control.PaddingProperty, "Inset.Field");
        box.SetResourceReference(FrameworkElement.FocusVisualStyleProperty, "Focus.Ring");
        AutomationProperties.SetName(box, accessibleName);
        return box;
    }

    private CheckBox BuildCheckBox(string text)
    {
        var box = new CheckBox { Content = text, Margin = new Thickness(0, 8, 0, 0) };
        box.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Brush.Text");
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
