using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using AllSky.Core;
using AllSky.Core.YouTube;

namespace AllSky.Desktop.Views.Publication;

/// <summary>
/// Pannello YouTube della vista Pubblicazione (P7.4): collegamento e scollegamento dell'account, stato del caricamento,
/// impostazioni (ora, privacy, tag, categoria) e "Carica ora la notte scorsa" con conferma.
/// </summary>
/// <remarks>
/// <para>
/// Il client secret si legge dal campo solo al collegamento, poi il campo si svuota. Non viene mai riletto, mostrato o scritto
/// in un log. Lo stato non contiene segreti.
/// </para>
/// <para>
/// Le operazioni chiamano <see cref="IYouTubeService"/> in modo asincrono. Lo stato arriva dall'evento
/// <see cref="IYouTubeService.StatusChanged"/> anche dal thread di sfondo, e si riporta sul thread dell'interfaccia.
/// La logica di testo sta in <c>AllSky.Core/YouTube</c>.
/// </para>
/// </remarks>
public sealed class YouTubePanel : UserControl
{
    /// <summary>Intervallo del controllo periodico dello stato, mentre il pannello è visibile.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly IYouTubeService _service;
    private readonly DesktopTexts _texts;
    private readonly DispatcherTimer _refreshTimer;

    private bool _attached;
    private bool _busy;
    private string? _lastVideoUrl;
    private CancellationTokenSource? _loginCts;

    // Stato.
    private TextBlock _connectionText = null!;
    private TextBlock _automaticText = null!;
    private TextBlock _nextUploadText = null!;
    private TextBlock _lastUploadText = null!;
    private TextBlock _problemText = null!;
    private TextBlock _progressText = null!;
    private TextBlock _resultText = null!;
    private Button _openVideoButton = null!;

    // Account.
    private TextBox _clientIdBox = null!;
    private PasswordBox _secretBox = null!;
    private Button _loginButton = null!;
    private Button _cancelLoginButton = null!;
    private Button _logoutButton = null!;

    // Impostazioni.
    private TextBox _dailyTimeBox = null!;
    private ComboBox _privacyBox = null!;
    private TextBox _tagsBox = null!;
    private TextBox _categoryBox = null!;
    private StackPanel _settingsProblems = null!;
    private Button _saveButton = null!;

    // Caricamento.
    private Button _uploadButton = null!;

    /// <summary>Crea il pannello con i testi della lingua scelta. Va creato sul thread dell'interfaccia.</summary>
    /// <param name="service">Servizio YouTube dell'Host.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public YouTubePanel(IYouTubeService service, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(texts);

        _service = service;
        _texts = texts;
        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) =>
        {
            if (_attached)
            {
                Render(_service.GetStatus());
            }
        };

        Content = BuildLayout();
        Margin = new Thickness(0, 16, 0, 0);
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
        Render(_service.GetStatus());
        _refreshTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _attached = false;
        _service.StatusChanged -= OnStatusChanged;
        _refreshTimer.Stop();

        // Si annulla solo il collegamento in attesa del browser. Un caricamento in corso prosegue e riprende dal registro.
        _loginCts?.Cancel();
    }

    private void OnStatusChanged(YouTubeStatus status)
    {
        // Evento dal thread dell'Host: si torna sul thread dell'interfaccia.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_attached)
            {
                Render(status);
            }
        }));
    }

    // ---- Azioni ----

    private async Task LoginAsync()
    {
        if (_busy)
        {
            return;
        }

        string clientId = _clientIdBox.Text.Trim();
        string secret = _secretBox.Password;

        // Il secret non resta nel campo, qualunque sia l'esito.
        _secretBox.Clear();
        ShowResult(null, ok: true);

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
        {
            ShowResult(YouTubeStatusFormatter.ProblemText(YouTubeProblem.CredentialsMissing, _texts), ok: false);
            return;
        }

        var cts = new CancellationTokenSource();
        _loginCts = cts;
        SetBusy(true);
        SetProgress(_texts.YouTubeLoginWaiting);
        try
        {
            YouTubeLoginResult result = await _service.LoginAsync(clientId, secret, cts.Token);
            if (result.Ok)
            {
                ShowResult(_texts.YouTubeLoginDone, ok: true);
            }
            else
            {
                ShowResult(YouTubeStatusFormatter.ProblemText(result.Problem, _texts), ok: false);
            }
        }
        catch (OperationCanceledException)
        {
            ShowResult(_texts.YouTubeLoginCancel, ok: false);
        }
        catch (Exception)
        {
            // Nessun dettaglio dell'eccezione in interfaccia: non deve contenere valori dei campi.
            ShowResult(_texts.YouTubeProblemLoginFailed, ok: false);
        }
        finally
        {
            _loginCts = null;
            cts.Dispose();
            SetBusy(false);
            SetProgress(null);
            Render(_service.GetStatus());
        }
    }

    private async Task LogoutAsync()
    {
        if (_busy || !Confirm(_texts.YouTubeLogoutConfirm))
        {
            return;
        }

        SetBusy(true);
        try
        {
            await _service.LogoutAsync(CancellationToken.None);
            ShowResult(_texts.YouTubeLogoutDone, ok: true);
        }
        catch (Exception)
        {
            ShowResult(_texts.YouTubeProblemLoginFailed, ok: false);
        }
        finally
        {
            SetBusy(false);
            Render(_service.GetStatus());
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (_busy)
        {
            return;
        }

        ClearSettingsProblems();
        ShowResult(null, ok: true);

        var change = new YouTubeSettingsChange(
            _dailyTimeBox.Text.Trim(),
            SelectedPrivacy(),
            YouTubeSettingsRules.ParseTags(_tagsBox.Text),
            _categoryBox.Text.Trim());

        SetBusy(true);
        try
        {
            YouTubeSaveResult result = await Task.Run(() => _service.SaveSettings(change));
            if (result.Ok)
            {
                ShowSettings(_service.GetSettings());
                ShowResult(_texts.YouTubeSaved, ok: true);
            }
            else
            {
                ShowSettingsProblems(result);
                ShowResult(_texts.YouTubeSaveFailed, ok: false);
            }
        }
        catch (Exception)
        {
            ShowResult(_texts.YouTubeSaveFailed, ok: false);
        }
        finally
        {
            SetBusy(false);
            Render(_service.GetStatus());
        }
    }

    private async Task UploadLastNightAsync()
    {
        if (_busy || !Confirm(_texts.YouTubeUploadConfirm))
        {
            return;
        }

        YouTubeUploadOutcome? outcome = await RunUploadAsync(acceptUncertainRisk: false);

        // Dopo un esito incerto o fallito, un secondo "sì" esplicito consente di ritentare la notte (rischio di duplicato).
        if (outcome is not null
            && YouTubeStatusFormatter.CanForceRetry(outcome.Kind)
            && Confirm(_texts.YouTubeUploadConfirmRisk))
        {
            outcome = await RunUploadAsync(acceptUncertainRisk: true);
        }

        if (outcome is not null)
        {
            ShowResult(
                YouTubeStatusFormatter.OutcomeText(outcome.Kind, _texts),
                ok: !YouTubeStatusFormatter.IsErrorOutcome(outcome.Kind));
        }

        Render(_service.GetStatus());
    }

    /// <summary>Esito del caricamento, o null se l'operazione è stata annullata.</summary>
    private async Task<YouTubeUploadOutcome?> RunUploadAsync(bool acceptUncertainRisk)
    {
        SetBusy(true);
        SetProgress(_texts.YouTubeUploadRunning);
        try
        {
            return await _service.UploadLastNightNowAsync(acceptUncertainRisk, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return new YouTubeUploadOutcome(YouTubeUploadOutcomeKind.Failed, null);
        }
        finally
        {
            SetBusy(false);
            SetProgress(null);
        }
    }

    private void OpenVideo()
    {
        if (_lastVideoUrl is null)
        {
            return;
        }

        try
        {
            // Il link viene da YouTubeLinks: solo https://www.youtube.com/watch?v=ID con caratteri previsti.
            Process.Start(new ProcessStartInfo(_lastVideoUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowResult(_texts.YouTubeProblemUnavailable, ok: false);
        }
    }

    // ---- Rendering ----

    private void Render(YouTubeStatus status)
    {
        _connectionText.Text = YouTubeStatusFormatter.ConnectionText(status.Connected, _texts);
        _automaticText.Text = YouTubeStatusFormatter.AutomaticText(status.AutomaticEnabled, _texts);
        _nextUploadText.Text = YouTubeStatusFormatter.NextUploadText(status.NextUploadUtc, _texts);
        _lastUploadText.Text = YouTubeStatusFormatter.LastUploadText(status.LastUpload, _texts);

        _lastVideoUrl = status.LastUpload?.VideoUrl;
        _openVideoButton.IsEnabled = _lastVideoUrl is not null && !_busy;
        _openVideoButton.Visibility = _lastVideoUrl is null ? Visibility.Collapsed : Visibility.Visible;

        string? problem = YouTubeStatusFormatter.LastProblemLine(status.Problem, _texts);
        SetOptional(_problemText, problem, "Brush.Error");

        if (status.ClientId is not null && string.IsNullOrEmpty(_clientIdBox.Text))
        {
            // Il client ID non e' un segreto: si ripropone, il secret no.
            _clientIdBox.Text = status.ClientId;
        }

        _loginButton.IsEnabled = !_busy;
        _cancelLoginButton.IsEnabled = _loginCts is not null;
        _logoutButton.IsEnabled = !_busy && status.Connected;
        _uploadButton.IsEnabled = !_busy && status.Connected;
        _saveButton.IsEnabled = !_busy;
    }

    private void ShowSettings(YouTubeSettingsSnapshot settings)
    {
        _dailyTimeBox.Text = settings.DailyTime;
        _tagsBox.Text = YouTubeSettingsRules.FormatTags(settings.Tags);
        _categoryBox.Text = settings.CategoryId;
        foreach (object item in _privacyBox.Items)
        {
            if (item is ComboBoxItem option && string.Equals(option.Tag as string, settings.Privacy, StringComparison.Ordinal))
            {
                _privacyBox.SelectedItem = option;
                break;
            }
        }
    }

    private void ShowSettingsProblems(YouTubeSaveResult result)
    {
        _settingsProblems.Children.Clear();
        foreach (YouTubeSettingsProblem problem in result.Problems)
        {
            _settingsProblems.Children.Add(BuildBodyText(YouTubeStatusFormatter.SettingsProblemText(problem, _texts), "Brush.Error"));
        }

        _settingsProblems.Visibility = result.Problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearSettingsProblems()
    {
        _settingsProblems.Children.Clear();
        _settingsProblems.Visibility = Visibility.Collapsed;
    }

    private void ShowResult(string? text, bool ok)
    {
        _resultText.Text = text ?? string.Empty;
        _resultText.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Brush.Success" : "Brush.Error");
        _resultText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _loginButton.IsEnabled = !busy;
        _logoutButton.IsEnabled = !busy;
        _uploadButton.IsEnabled = !busy;
        _saveButton.IsEnabled = !busy;
    }

    private void SetProgress(string? text)
    {
        _progressText.Text = text ?? string.Empty;
        _progressText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void SetOptional(TextBlock block, string? text, string brushKey)
    {
        block.Text = text ?? string.Empty;
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        block.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private string SelectedPrivacy() =>
        (_privacyBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "private";

    private bool Confirm(string text) =>
        System.Windows.MessageBox.Show(text, _texts.PublicationYouTubeTitle, MessageBoxButton.YesNo, MessageBoxImage.Question)
        == MessageBoxResult.Yes;

    // ---- Layout ----

    private UIElement BuildLayout()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.PublicationYouTubeTitle));
        stack.Children.Add(BuildBodyText(_texts.YouTubeTestingWarning, "Brush.Warning", "Size.Caption"));

        // Stato.
        _connectionText = BuildBodyText(string.Empty, "Brush.Text", "Size.Subtitle");
        _automaticText = BuildBodyText(string.Empty, "Brush.TextSecondary");
        _nextUploadText = BuildBodyText(string.Empty, "Brush.TextSecondary");
        _lastUploadText = BuildBodyText(string.Empty, "Brush.Text");
        _openVideoButton = BuildButton(_texts.YouTubeOpenVideo, primary: false);
        _openVideoButton.Click += (_, _) => OpenVideo();
        _problemText = BuildBodyText(string.Empty, "Brush.Error");
        _problemText.Visibility = Visibility.Collapsed;
        _progressText = BuildBodyText(string.Empty, "Brush.TextSecondary");
        _progressText.Visibility = Visibility.Collapsed;
        _resultText = BuildBodyText(string.Empty, "Brush.Text");
        _resultText.Visibility = Visibility.Collapsed;

        stack.Children.Add(_connectionText);
        stack.Children.Add(_automaticText);
        stack.Children.Add(_nextUploadText);
        stack.Children.Add(_lastUploadText);
        stack.Children.Add(_openVideoButton);
        stack.Children.Add(_problemText);

        // Account.
        stack.Children.Add(BuildSectionTitle(_texts.YouTubeAccountTitle));
        stack.Children.Add(BuildLabel(_texts.YouTubeClientIdLabel));
        _clientIdBox = BuildTextBox(_texts.YouTubeClientIdLabel, 340);
        stack.Children.Add(_clientIdBox);
        stack.Children.Add(BuildLabel(_texts.YouTubeClientSecretLabel));
        _secretBox = BuildPasswordBox(_texts.YouTubeClientSecretLabel);
        stack.Children.Add(_secretBox);
        stack.Children.Add(BuildBodyText(_texts.YouTubeSecretNote, "Brush.TextTertiary", "Size.Caption"));

        var accountButtons = new StackPanel { Orientation = Orientation.Horizontal };
        _loginButton = BuildButton(_texts.YouTubeLogin, primary: true);
        _loginButton.Click += async (_, _) => await LoginAsync();
        _cancelLoginButton = BuildButton(_texts.YouTubeLoginCancel, primary: false);
        _cancelLoginButton.Click += (_, _) => _loginCts?.Cancel();
        _logoutButton = BuildButton(_texts.YouTubeLogout, primary: false);
        _logoutButton.Click += async (_, _) => await LogoutAsync();
        accountButtons.Children.Add(_loginButton);
        accountButtons.Children.Add(_cancelLoginButton);
        accountButtons.Children.Add(_logoutButton);
        stack.Children.Add(accountButtons);

        // Impostazioni.
        stack.Children.Add(BuildSectionTitle(_texts.YouTubeSettingsTitle));
        stack.Children.Add(BuildLabel(_texts.YouTubeDailyTimeLabel));
        _dailyTimeBox = BuildTextBox(_texts.YouTubeDailyTimeLabel, 120);
        stack.Children.Add(_dailyTimeBox);

        stack.Children.Add(BuildLabel(_texts.YouTubePrivacyLabel));
        _privacyBox = new ComboBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(_privacyBox, _texts.YouTubePrivacyLabel);
        _privacyBox.Items.Add(new ComboBoxItem { Content = _texts.YouTubePrivacyPrivate, Tag = "private" });
        _privacyBox.Items.Add(new ComboBoxItem { Content = _texts.YouTubePrivacyUnlisted, Tag = "unlisted" });
        _privacyBox.Items.Add(new ComboBoxItem { Content = _texts.YouTubePrivacyPublic, Tag = "public" });
        stack.Children.Add(_privacyBox);

        stack.Children.Add(BuildLabel(_texts.YouTubeTagsLabel));
        _tagsBox = BuildTextBox(_texts.YouTubeTagsLabel, 340);
        stack.Children.Add(_tagsBox);

        stack.Children.Add(BuildLabel(_texts.YouTubeCategoryLabel));
        _categoryBox = BuildTextBox(_texts.YouTubeCategoryLabel, 120);
        stack.Children.Add(_categoryBox);
        stack.Children.Add(BuildBodyText(_texts.YouTubeCategoryHint, "Brush.TextTertiary", "Size.Caption"));

        _settingsProblems = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        stack.Children.Add(_settingsProblems);

        _saveButton = BuildButton(_texts.YouTubeSave, primary: false);
        _saveButton.Click += async (_, _) => await SaveSettingsAsync();
        stack.Children.Add(_saveButton);

        // Caricamento.
        stack.Children.Add(BuildSectionTitle(_texts.YouTubeUploadTitle));
        _uploadButton = BuildButton(_texts.YouTubeUploadNow, primary: true);
        _uploadButton.Click += async (_, _) => await UploadLastNightAsync();
        stack.Children.Add(_uploadButton);
        stack.Children.Add(_progressText);
        stack.Children.Add(_resultText);

        return BuildPanel(stack, _texts.PublicationYouTubeTitle);
    }

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

    private TextBlock BuildSectionTitle(string text)
    {
        var title = new TextBlock { Text = text, Margin = new Thickness(0, 20, 0, 4), FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        title.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
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

    /// <summary>Campo del secret: nessun valore viene mai scritto dall'applicazione.</summary>
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

    private Button BuildButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 32,
            Margin = new Thickness(0, 8, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, primary ? "Button.Primary" : "Button.Secondary");
        AutomationProperties.SetName(button, text);
        return button;
    }
}
