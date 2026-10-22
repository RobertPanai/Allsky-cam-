using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AllSky.Core;
using AllSky.Core.Settings;
using AllSky.Desktop.Theme;
using AllSky.Engine;
using AllSky.Host;
using AllSky.Updates;

namespace AllSky.Desktop.Views.Settings;

/// <summary>
/// Vista "Impostazioni": posizione dell'osservatorio, lingua, tema, cartella dati (sola lettura), avvio con Windows,
/// aggiornamenti, accesso dal browser (interfaccia web spenta di default, rete locale con password).
/// </summary>
/// <remarks>
/// <para>Le scritture di configurazione passano dal servizio dell'Engine (<see cref="AllSky.Engine.IAllSkyService.UpdateConfig"/>),
/// che valida e salva in modo atomico. Le operazioni che possono bloccare (salvataggio, hash della password, controllo
/// aggiornamenti) girano fuori dal thread della UI.</para>
/// <para>
/// La lingua e l'accesso dal browser si applicano al riavvio: la vista lo dice. Le regole di validazione stanno in
/// <c>AllSky.Core/Settings</c>.
/// </para>
/// </remarks>
public class SettingsView : UserControl
{
    private readonly AllSkyHostRuntime _runtime;
    private readonly AppPaths _paths;
    private readonly DesktopTexts _texts;
    private readonly ThemeManager _theme;

    private TextBox _latitudeBox = null!;
    private TextBox _longitudeBox = null!;
    private TextBox _elevationBox = null!;
    private TextBlock _latitudeProblem = null!;
    private TextBlock _longitudeProblem = null!;
    private TextBlock _elevationProblem = null!;
    private TextBlock _siteInfo = null!;
    private Button _siteSaveButton = null!;

    private ComboBox _languageBox = null!;
    private TextBlock _languageInfo = null!;
    private ComboBox _themeBox = null!;
    private TextBlock _themeInfo = null!;

    private TextBlock _folderInfo = null!;

    private CheckBox _autostartBox = null!;
    private TextBlock _autostartInfo = null!;

    private TextBlock _versionText = null!;
    private CheckBox _autoCheckBox = null!;
    private TextBlock _lastCheckText = null!;
    private TextBlock _updateInfo = null!;
    private Button _checkNowButton = null!;

    private CheckBox _webBox = null!;
    private TextBlock _webStatus = null!;
    private CheckBox _lanBox = null!;
    private PasswordBox _passwordBox = null!;
    private TextBlock _passwordState = null!;
    private TextBlock _passwordProblem = null!;
    private TextBlock _accessInfo = null!;
    private Button _accessSaveButton = null!;

    private bool _attached;
    private bool _busy;
    private bool _syncing;

    /// <summary>Crea la vista con i testi nella lingua scelta. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="runtime">Host in esecuzione: configurazione, aggiornamenti e stato dell'interfaccia web.</param>
    /// <param name="paths">Cartelle dell'applicazione.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    /// <param name="theme">Gestore del tema (modalità notte rossa).</param>
    public SettingsView(AllSkyHostRuntime runtime, AppPaths paths, DesktopTexts texts, ThemeManager theme)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(theme);

        _runtime = runtime;
        _paths = paths;
        _texts = texts;
        _theme = theme;

        Content = BuildLayout();
        AutomationProperties.SetName(this, texts.SettingsTitle);

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
        _theme.ThemeChanged += OnThemeChanged;
        RenderAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _attached = false;
        _theme.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // Ctrl+R cambia il tema dalla shell: la scelta della vista lo segue senza salvare di nuovo.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_attached)
            {
                RenderTheme();
            }
        }));
    }

    // ---- Rendering dello stato ----

    private void RenderAll()
    {
        AllSky.Core.AppConfig config = _runtime.Service.GetConfig();
        RenderSite(config);
        RenderLanguage(config);
        RenderTheme();
        RenderAutostart();
        RenderUpdates(_runtime.Updates.GetStatus());
        RenderAccess(config);
    }

    private void RenderSite(AllSky.Core.AppConfig config)
    {
        _latitudeBox.Text = SiteForm.Format(config.Site.Latitude, _texts.Culture);
        _longitudeBox.Text = SiteForm.Format(config.Site.Longitude, _texts.Culture);
        _elevationBox.Text = SiteForm.Format(config.Site.ElevationM, _texts.Culture);
        RenderSiteProblems();
    }

    private void RenderLanguage(AllSky.Core.AppConfig config)
    {
        _syncing = true;
        try
        {
            _languageBox.SelectedIndex = config.Language == AllSky.Core.Language.En ? 1 : 0;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RenderTheme()
    {
        _syncing = true;
        try
        {
            _themeBox.SelectedIndex = _theme.NightMode ? 1 : 0;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RenderAutostart()
    {
        _syncing = true;
        try
        {
            _autostartBox.IsChecked = AutostartRegistry.IsEnabled();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RenderUpdates(UpdateStatus status)
    {
        _versionText.Text = string.Format(_texts.Culture, _texts.SettingsCurrentVersion,
            string.IsNullOrWhiteSpace(status.CurrentVersion) ? "—" : status.CurrentVersion);

        _syncing = true;
        try
        {
            _autoCheckBox.IsChecked = _runtime.Updates.AutoCheckEnabled;
        }
        finally
        {
            _syncing = false;
        }

        _lastCheckText.Text = status.LastCheckUtc is { } last
            ? string.Format(_texts.Culture, _texts.SettingsLastCheck, last.ToLocalTime().ToString("g", _texts.Culture))
            : _texts.SettingsNeverChecked;

        _checkNowButton.IsEnabled = !_busy && status.State is not (UpdateState.Checking or UpdateState.Disabled);

        switch (status.State)
        {
            case UpdateState.Checking:
                SetInfo(_updateInfo, _texts.SettingsChecking, error: false);
                break;
            case UpdateState.UpToDate:
                SetInfo(_updateInfo, _texts.SettingsUpToDate, error: false);
                break;
            case UpdateState.Available:
            case UpdateState.ReadyToInstall:
                SetInfo(_updateInfo, string.Format(_texts.Culture, _texts.SettingsUpdateAvailable, status.AvailableVersion ?? "—"), error: false);
                break;
            case UpdateState.Disabled:
                SetInfo(_updateInfo, _texts.SettingsUpdateDisabled, error: false);
                break;
            case UpdateState.Error:
                SetInfo(_updateInfo, status.ErrorCode == UpdateErrorCodes.Network
                    ? _texts.SettingsUpdateNetworkError
                    : _texts.SettingsUpdateGenericError, error: true);
                break;
            default:
                SetInfo(_updateInfo, string.Empty, error: false);
                break;
        }
    }

    private void RenderAccess(AllSky.Core.AppConfig config)
    {
        _syncing = true;
        try
        {
            _webBox.IsChecked = config.Ui.WebEnabled;
            _lanBox.IsChecked = config.Ui.AllowLan;
        }
        finally
        {
            _syncing = false;
        }

        _lanBox.IsEnabled = _webBox.IsChecked == true;
        _passwordState.Text = string.IsNullOrEmpty(config.Ui.LanPasswordHash)
            ? _texts.SettingsLanPasswordNotSet
            : _texts.SettingsLanPasswordSet;

        _webStatus.Text = _runtime.WebRunning
            ? string.Format(_texts.Culture, _texts.SettingsWebRunning, _runtime.Url ?? string.Empty)
            : _texts.SettingsWebNotRunning;
    }

    private void RenderSiteProblems()
    {
        SiteCheck check = SiteForm.Check(_latitudeBox.Text, _longitudeBox.Text, _elevationBox.Text, _texts.Culture);
        SetProblem(_latitudeProblem, SiteProblemText(check.LatitudeProblem));
        SetProblem(_longitudeProblem, SiteProblemText(check.LongitudeProblem));
        SetProblem(_elevationProblem, SiteProblemText(check.ElevationProblem));
    }

    private string? SiteProblemText(SiteFieldProblem problem) => problem switch
    {
        SiteFieldProblem.None => null,
        SiteFieldProblem.Empty => _texts.SettingsProblemEmpty,
        SiteFieldProblem.NotNumber => _texts.SettingsProblemNotNumber,
        SiteFieldProblem.LatitudeOutOfRange => _texts.SettingsProblemLatitude,
        SiteFieldProblem.LongitudeOutOfRange => _texts.SettingsProblemLongitude,
        SiteFieldProblem.ElevationOutOfRange => _texts.SettingsProblemElevation,
        _ => null,
    };

    // ---- Azioni ----

    private async Task SaveSiteAsync()
    {
        if (_busy)
        {
            return;
        }

        RenderSiteProblems();
        SiteCheck check = SiteForm.Check(_latitudeBox.Text, _longitudeBox.Text, _elevationBox.Text, _texts.Culture);
        if (!check.IsValid)
        {
            SetInfo(_siteInfo, _texts.SettingsInvalidValues, error: true);
            return;
        }

        AllSky.Core.AppConfig current = _runtime.Service.GetConfig();
        if (!SiteForm.HasChanges(check, current.Site.Latitude, current.Site.Longitude, current.Site.ElevationM))
        {
            SetInfo(_siteInfo, _texts.SettingsNoChanges, error: false);
            return;
        }

        AllSky.Core.AppConfig updated = current with
        {
            Site = current.Site with
            {
                Latitude = check.Latitude!.Value,
                Longitude = check.Longitude!.Value,
                ElevationM = check.ElevationM!.Value,
            },
        };

        BeginBusy();
        try
        {
            await SaveConfigAsync(updated, _siteInfo, _texts.SettingsSiteSaved);
        }
        finally
        {
            EndBusy();
        }
    }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _busy)
        {
            RenderLanguage(_runtime.Service.GetConfig());
            return;
        }

        AllSky.Core.Language language = _languageBox.SelectedIndex == 1 ? AllSky.Core.Language.En : AllSky.Core.Language.It;
        AllSky.Core.AppConfig current = _runtime.Service.GetConfig();
        if (current.Language == language)
        {
            return;
        }

        BeginBusy();
        try
        {
            await SaveConfigAsync(current with { Language = language }, _languageInfo, _texts.SettingsLanguageSaved);
        }
        finally
        {
            EndBusy();
        }

        RenderLanguage(_runtime.Service.GetConfig());
    }

    private async void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        bool red = _themeBox.SelectedIndex == 1;
        _theme.SetNightMode(red);

        AllSky.Core.AppConfig current = _runtime.Service.GetConfig();
        AllSky.Core.Theme wanted = red ? AllSky.Core.Theme.Red : AllSky.Core.Theme.Day;
        if (current.Ui.Theme == wanted || _busy)
        {
            return;
        }

        BeginBusy();
        try
        {
            await SaveConfigAsync(current with { Ui = current.Ui with { Theme = wanted } }, _themeInfo, _texts.SettingsSaved);
        }
        finally
        {
            EndBusy();
        }
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        bool enable = _autostartBox.IsChecked == true;
        try
        {
            string exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Percorso dell'eseguibile non disponibile.");
            AutostartRegistry.Set(enable, exePath);
            SetInfo(_autostartInfo, string.Empty, error: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            RenderAutostart();
            SetInfo(_autostartInfo, string.Format(_texts.Culture, _texts.SettingsAutostartError, ex.Message), error: true);
        }
    }

    private void OnAutoCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        bool enabled = _autoCheckBox.IsChecked == true;
        try
        {
            _runtime.SetUpdateAutoCheck(enabled);
            SetInfo(_updateInfo, string.Empty, error: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RenderUpdates(_runtime.Updates.GetStatus());
            SetInfo(_updateInfo, _texts.SettingsSaveError, error: true);
        }
    }

    private async Task CheckNowAsync()
    {
        if (_busy)
        {
            return;
        }

        BeginBusy();
        SetInfo(_updateInfo, _texts.SettingsChecking, error: false);
        try
        {
            UpdateStatus status = await _runtime.Updates.CheckNowAsync();
            RenderUpdates(status);
        }
        catch (Exception)
        {
            SetInfo(_updateInfo, _texts.SettingsUpdateGenericError, error: true);
        }
        finally
        {
            EndBusy();
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_paths.DataFolder) { UseShellExecute = true })?.Dispose();
            SetInfo(_folderInfo, string.Empty, error: false);
        }
        catch (Exception)
        {
            SetInfo(_folderInfo, string.Format(_texts.Culture, _texts.SettingsOpenFolderError, _paths.DataFolder), error: true);
        }
    }

    private async Task SaveAccessAsync()
    {
        if (_busy)
        {
            return;
        }

        AllSky.Core.AppConfig current = _runtime.Service.GetConfig();
        bool web = _webBox.IsChecked == true;
        bool lan = web && _lanBox.IsChecked == true;
        string password = _passwordBox.Password;
        bool typed = !string.IsNullOrWhiteSpace(password);

        SetProblem(_passwordProblem, null);
        if (typed && LanPassword.Check(password) == LanPasswordProblem.TooShort)
        {
            SetProblem(_passwordProblem, _texts.SettingsPasswordTooShort);
            return;
        }

        string? hash = current.Ui.LanPasswordHash;
        if (lan && !typed && string.IsNullOrEmpty(hash))
        {
            SetProblem(_passwordProblem, _texts.SettingsPasswordRequired);
            return;
        }

        BeginBusy();
        try
        {
            string? newHash = typed ? await Task.Run(() => LanPassword.Hash(password)) : hash;
            AllSky.Core.AppConfig updated = current with
            {
                Ui = current.Ui with
                {
                    WebEnabled = web,
                    AllowLan = lan,
                    LanPasswordHash = newHash,
                },
            };

            bool saved = await SaveConfigAsync(updated, _accessInfo, _texts.SettingsAccessSaved);
            if (saved)
            {
                _passwordBox.Password = string.Empty;
            }
        }
        finally
        {
            EndBusy();
            RenderAccess(_runtime.Service.GetConfig());
        }
    }

    /// <summary>Salva la configurazione fuori dal thread della UI e mostra l'esito. Restituisce true se è stata salvata.</summary>
    /// <remarks>Chi chiama deve avere già chiamato <see cref="BeginBusy"/>.</remarks>
    private async Task<bool> SaveConfigAsync(AllSky.Core.AppConfig updated, TextBlock info, string successText)
    {
        SetInfo(info, _texts.SettingsSaving, error: false);
        try
        {
            ConfigUpdateResult result = await Task.Run(() => _runtime.Service.UpdateConfig(updated));
            if (result.Ok)
            {
                SetInfo(info, successText, error: false);
                return true;
            }

            string detail = result.Errors.Count > 0 ? " " + result.Errors[0].Message : string.Empty;
            SetInfo(info, _texts.SettingsInvalidValues + detail, error: true);
            return false;
        }
        catch (Exception)
        {
            SetInfo(info, _texts.SettingsSaveError, error: true);
            return false;
        }
    }

    /// <summary>Blocca i pulsanti di salvataggio e di controllo mentre un'operazione è in corso.</summary>
    private void BeginBusy()
    {
        _busy = true;
        _siteSaveButton.IsEnabled = false;
        _accessSaveButton.IsEnabled = false;
        _checkNowButton.IsEnabled = false;
    }

    private void EndBusy()
    {
        _busy = false;
        _siteSaveButton.IsEnabled = true;
        _accessSaveButton.IsEnabled = true;
        _checkNowButton.IsEnabled = true;
    }

    // ---- Layout ----

    private UIElement BuildLayout()
    {
        var header = new TextBlock
        {
            Text = _texts.SettingsTitle,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        header.SetResourceReference(TextBlock.FontSizeProperty, "Size.Display");

        var left = new StackPanel();
        left.Children.Add(BuildSitePanel());
        left.Children.Add(BuildGeneralPanel());
        left.Children.Add(BuildFolderPanel());

        var right = new StackPanel();
        right.Children.Add(BuildStartupPanel());
        right.Children.Add(BuildMaskPanel());
        right.Children.Add(BuildUpdatesPanel());
        right.Children.Add(BuildAccessPanel());

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

        // Lo scorrimento evita tagli sulle finestre basse; la larghezza segue la vista.
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };
    }

    private Border BuildSitePanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.SettingsObservatoryTitle));
        stack.Children.Add(BuildBodyText(_texts.SettingsSiteHint, "Brush.TextTertiary", "Size.Caption"));

        _latitudeBox = BuildTextBox(_texts.SettingsLatitude);
        _latitudeBox.TextChanged += (_, _) => RenderSiteProblems();
        _latitudeProblem = BuildProblem();
        stack.Children.Add(BuildLabel(_texts.SettingsLatitude));
        stack.Children.Add(_latitudeBox);
        stack.Children.Add(_latitudeProblem);

        _longitudeBox = BuildTextBox(_texts.SettingsLongitude);
        _longitudeBox.TextChanged += (_, _) => RenderSiteProblems();
        _longitudeProblem = BuildProblem();
        stack.Children.Add(BuildLabel(_texts.SettingsLongitude));
        stack.Children.Add(_longitudeBox);
        stack.Children.Add(_longitudeProblem);

        _elevationBox = BuildTextBox(_texts.SettingsElevation);
        _elevationBox.TextChanged += (_, _) => RenderSiteProblems();
        _elevationProblem = BuildProblem();
        stack.Children.Add(BuildLabel(_texts.SettingsElevation));
        stack.Children.Add(_elevationBox);
        stack.Children.Add(_elevationProblem);

        _siteSaveButton = BuildButton(_texts.SettingsSiteSave, primary: true);
        _siteSaveButton.Margin = new Thickness(0, 16, 0, 0);
        _siteSaveButton.Click += async (_, _) => await SaveSiteAsync();
        stack.Children.Add(_siteSaveButton);

        _siteInfo = BuildBodyText(string.Empty, "Brush.TextSecondary");
        stack.Children.Add(_siteInfo);

        return BuildPanel(stack, _texts.SettingsObservatoryTitle);
    }

    private Border BuildGeneralPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.SettingsGeneralTitle));

        stack.Children.Add(BuildLabel(_texts.SettingsLanguageLabel));
        _languageBox = new ComboBox { Margin = new Thickness(0, 4, 0, 0), MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
        _languageBox.SetResourceReference(Control.BackgroundProperty, "Brush.Surface2");
        _languageBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _languageBox.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        _languageBox.Items.Add(new ComboBoxItem { Content = "Italiano" });
        _languageBox.Items.Add(new ComboBoxItem { Content = "English" });
        AutomationProperties.SetName(_languageBox, _texts.SettingsLanguageLabel);
        _languageBox.SelectionChanged += OnLanguageChanged;
        stack.Children.Add(_languageBox);
        stack.Children.Add(BuildBodyText(_texts.SettingsLanguageRestart, "Brush.TextTertiary", "Size.Caption"));
        _languageInfo = BuildBodyText(string.Empty, "Brush.TextSecondary");
        stack.Children.Add(_languageInfo);

        stack.Children.Add(BuildLabel(_texts.SettingsThemeLabel));
        _themeBox = new ComboBox { Margin = new Thickness(0, 4, 0, 0), MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
        _themeBox.SetResourceReference(Control.BackgroundProperty, "Brush.Surface2");
        _themeBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _themeBox.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        _themeBox.Items.Add(new ComboBoxItem { Content = _texts.SettingsThemeNormal });
        _themeBox.Items.Add(new ComboBoxItem { Content = _texts.SettingsThemeNightRed });
        AutomationProperties.SetName(_themeBox, _texts.SettingsThemeLabel);
        _themeBox.SelectionChanged += OnThemeSelectionChanged;
        stack.Children.Add(_themeBox);
        stack.Children.Add(BuildBodyText(_texts.SettingsThemeHint, "Brush.TextTertiary", "Size.Caption"));
        _themeInfo = BuildBodyText(string.Empty, "Brush.TextSecondary");
        stack.Children.Add(_themeInfo);

        var panel = BuildPanel(stack, _texts.SettingsGeneralTitle);
        panel.Margin = new Thickness(0, 16, 0, 16);
        return panel;
    }

    private Border BuildMaskPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.MaskTitle));
        var openButton = BuildButton(_texts.MaskNav, primary: false);
        openButton.Margin = new Thickness(0, 12, 0, 0);
        openButton.Click += (_, _) => OpenMaskEditor();
        stack.Children.Add(openButton);
        return BuildPanel(stack, _texts.MaskTitle);
    }

    private void OpenMaskEditor()
    {
        var window = new Window
        {
            Title = _texts.MaskTitle,
            Width = 1200,
            Height = 780,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new AllSky.Desktop.Views.Mask.MaskEditorView(_runtime.Service, _paths, _texts),
        };
        window.SetResourceReference(Window.BackgroundProperty, "Brush.Background");
        window.Show();
    }

    private Border BuildFolderPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.SettingsDataFolderTitle));
        stack.Children.Add(BuildBodyText(string.Format(_texts.Culture, _texts.SettingsDataFolderPath, _paths.DataFolder), "Brush.Text"));
        stack.Children.Add(BuildBodyText(_texts.SettingsDataFolderReadOnly, "Brush.TextTertiary", "Size.Caption"));

        var openButton = BuildButton(_texts.SettingsOpenFolder, primary: false);
        openButton.Margin = new Thickness(0, 12, 0, 0);
        openButton.Click += (_, _) => OpenDataFolder();
        stack.Children.Add(openButton);

        _folderInfo = BuildBodyText(string.Empty, "Brush.Error");
        stack.Children.Add(_folderInfo);

        return BuildPanel(stack, _texts.SettingsDataFolderTitle);
    }

    private Border BuildStartupPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.SettingsStartupTitle));

        _autostartBox = new CheckBox { Content = _texts.SettingsStartWithWindows, Margin = new Thickness(0, 4, 0, 0) };
        _autostartBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        AutomationProperties.SetName(_autostartBox, _texts.SettingsStartWithWindows);
        _autostartBox.Checked += OnAutostartChanged;
        _autostartBox.Unchecked += OnAutostartChanged;
        stack.Children.Add(_autostartBox);

        stack.Children.Add(BuildBodyText(_texts.SettingsStartWithWindowsHint, "Brush.TextTertiary", "Size.Caption"));
        _autostartInfo = BuildBodyText(string.Empty, "Brush.Error");
        stack.Children.Add(_autostartInfo);

        return BuildPanel(stack, _texts.SettingsStartupTitle);
    }

    private Border BuildUpdatesPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.SettingsUpdatesTitle));

        _versionText = BuildBodyText(string.Empty, "Brush.Text");
        stack.Children.Add(_versionText);

        _autoCheckBox = new CheckBox { Content = _texts.SettingsAutoCheck, Margin = new Thickness(0, 12, 0, 0) };
        _autoCheckBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        AutomationProperties.SetName(_autoCheckBox, _texts.SettingsAutoCheck);
        _autoCheckBox.Checked += OnAutoCheckChanged;
        _autoCheckBox.Unchecked += OnAutoCheckChanged;
        stack.Children.Add(_autoCheckBox);
        stack.Children.Add(BuildBodyText(_texts.SettingsAutoCheckHint, "Brush.TextTertiary", "Size.Caption"));

        _checkNowButton = BuildButton(_texts.SettingsCheckNow, primary: false);
        _checkNowButton.Margin = new Thickness(0, 12, 0, 0);
        _checkNowButton.Click += async (_, _) => await CheckNowAsync();
        stack.Children.Add(_checkNowButton);

        _lastCheckText = BuildBodyText(string.Empty, "Brush.TextSecondary", "Size.Caption");
        stack.Children.Add(_lastCheckText);

        _updateInfo = BuildBodyText(string.Empty, "Brush.TextSecondary");
        stack.Children.Add(_updateInfo);

        var panel = BuildPanel(stack, _texts.SettingsUpdatesTitle);
        panel.Margin = new Thickness(0, 0, 0, 16);
        return panel;
    }

    private void OnWebBoxClick(object sender, RoutedEventArgs e)
    {
        _lanBox.IsEnabled = _webBox.IsChecked == true;
    }

    private Border BuildAccessPanel()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.SettingsAccessTitle));

        _webBox = new CheckBox { Content = _texts.SettingsWebEnabled, Margin = new Thickness(0, 4, 0, 0) };
        _webBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        AutomationProperties.SetName(_webBox, _texts.SettingsWebEnabled);
        _webBox.Click += OnWebBoxClick;
        stack.Children.Add(_webBox);
        stack.Children.Add(BuildBodyText(_texts.SettingsWebHint, "Brush.TextTertiary", "Size.Caption"));
        _webStatus = BuildBodyText(string.Empty, "Brush.TextSecondary", "Size.Caption");
        stack.Children.Add(_webStatus);

        _lanBox = new CheckBox { Content = _texts.SettingsLanEnabled, Margin = new Thickness(0, 12, 0, 0) };
        _lanBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        AutomationProperties.SetName(_lanBox, _texts.SettingsLanEnabled);
        stack.Children.Add(_lanBox);
        stack.Children.Add(BuildBodyText(_texts.SettingsLanHint, "Brush.TextTertiary", "Size.Caption"));

        stack.Children.Add(BuildLabel(_texts.SettingsLanPasswordLabel));
        _passwordBox = new PasswordBox { Width = 240, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(_passwordBox, _texts.SettingsLanPasswordLabel);
        stack.Children.Add(_passwordBox);
        stack.Children.Add(BuildBodyText(_texts.SettingsLanPasswordHint, "Brush.TextTertiary", "Size.Caption"));
        _passwordState = BuildBodyText(string.Empty, "Brush.TextSecondary", "Size.Caption");
        stack.Children.Add(_passwordState);
        _passwordProblem = BuildProblem();
        stack.Children.Add(_passwordProblem);

        _accessSaveButton = BuildButton(_texts.SettingsAccessSave, primary: true);
        _accessSaveButton.Margin = new Thickness(0, 16, 0, 0);
        _accessSaveButton.Click += async (_, _) => await SaveAccessAsync();
        stack.Children.Add(_accessSaveButton);

        _accessInfo = BuildBodyText(string.Empty, "Brush.TextSecondary");
        stack.Children.Add(_accessInfo);

        var panel = BuildPanel(stack, _texts.SettingsAccessTitle);
        panel.Margin = new Thickness(0, 0, 0, 16);
        return panel;
    }

    // ---- Costruttori di controlli ----

    private static void SetProblem(TextBlock block, string? text)
    {
        block.Text = text ?? string.Empty;
        block.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetInfo(TextBlock block, string text, bool error)
    {
        block.Text = text;
        block.SetResourceReference(TextBlock.ForegroundProperty, error ? "Brush.Error" : "Brush.TextSecondary");
    }

    private TextBlock BuildProblem()
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");
        block.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        return block;
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
        box.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
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
