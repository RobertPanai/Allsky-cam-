using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AllSky.Core;
using AllSky.Core.Nights;
using AllSky.Engine;

namespace AllSky.Desktop.Views.Nights;

/// <summary>
/// Vista "Notti": elenco delle notti archiviate con frame e stato del timelapse, keogram usato come barra di scorrimento
/// temporale, startrails, riproduzione del timelapse e pulsante "Rigenera timelapse".
/// </summary>
/// <remarks>
/// <para>Ogni lettura dell'archivio (elenco, frame, immagini, percorso del timelapse) gira in un task in background. Le
/// letture si susseguono senza sovrapporsi: se ne arriva una nuova mentre una è in corso, si esegue l'ultima richiesta alla
/// fine (la stessa logica di <c>LiveView</c>). Una risposta vecchia viene scartata quando la notte selezionata è cambiata.</para>
/// <para>Gli eventi del servizio arrivano dal thread dell'Engine e si riportano sul thread dell'interfaccia. L'elenco non
/// si rilegge a ogni frame: si rilegge al cambio dei lavori di timelapse e al più una volta ogni minuto.</para>
/// <para>Le immagini sono congelate e ridotte in larghezza. Allo scarico si rilasciano immagini, sorgente del video e
/// sottoscrizioni agli eventi.</para>
/// <para>
/// Corrispondenza keogram-frame: la colonna <c>k</c> del keogram è il frame <c>k</c>, quindi la posizione del clic dà il
/// frame. Il keogram può coprire meno frame dell'archivio (esclude quelli prima di un riavvio): la corrispondenza è
/// approssimata, come documentato in <see cref="KeogramScrubber"/>.
/// </para>
/// </remarks>
public sealed class NightsView : UserControl
{
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

    /// <summary>Intervallo minimo tra due riletture complete dell'archivio per nuovi frame.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private const string TimelapseKind = "timelapse";

    private readonly IAllSkyService _service;
    private readonly DesktopTexts _texts;
    private readonly NightsKeogramBar _keogramBar;
    private readonly NightsTimelapsePlayer _player;

    private readonly ListBox _list = new();
    private readonly TextBlock _listStatus = new();
    private readonly Border _hintPanel = new();
    private readonly ScrollViewer _detailPane = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _subtitle = new();
    private readonly TextBlock _actionStatus = new();
    private readonly Image _frameImage = new();
    private readonly TextBlock _frameMessage = new();
    private readonly TextBlock _frameInfo = new();
    private readonly Image _startrailsImage = new();
    private readonly TextBlock _startrailsMessage = new();
    private readonly TextBlock _timelapseStatus = new();
    private readonly Button _regenerate = new();

    private IReadOnlyList<NightSummary> _nights = Array.Empty<NightSummary>();
    private IReadOnlyList<JobDto> _jobs = Array.Empty<JobDto>();
    private HashSet<string> _busyNights = new(StringComparer.Ordinal);
    private string? _listError;
    private bool _loaded;
    private bool _pendingDetailReload;
    private string? _selectedId;
    private NightsData.NightDetail? _detailData;
    private int _wantedFrame = -1;
    private int _shownFrame = -1;
    private bool _frameBusy;
    private bool _listBusy;
    private bool _listAgain;
    private int _detailSeq;
    private bool _regenBusy;
    private bool _rebuilding;
    private bool _attached;
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private Style? _listItemStyle;

    /// <summary>Crea la vista con i testi in italiano.</summary>
    /// <param name="service">Servizio dell'Engine, già avviato dall'host.</param>
    public NightsView(IAllSkyService service)
        : this(service, DesktopTexts.Italian)
    {
    }

    /// <summary>Crea la vista con i testi nella lingua scelta. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="service">Servizio dell'Engine.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public NightsView(IAllSkyService service, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(texts);

        _service = service;
        _texts = texts;
        _keogramBar = new NightsKeogramBar(texts);
        _player = new NightsTimelapsePlayer(texts);

        Content = BuildLayout();
        AutomationProperties.SetName(this, texts.NavNights);

        _list.SelectionChanged += OnSelectionChanged;
        _keogramBar.FractionRequested += OnFractionRequested;
        _keogramBar.StepRequested += OnStepRequested;
        _regenerate.Click += (_, _) => _ = RegenerateAsync();

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
        _service.FrameProduced += OnFrameProduced;
        _service.StatusChanged += OnStatusChanged;
        _pendingDetailReload = true;
        _ = RefreshAllAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _service.FrameProduced -= OnFrameProduced;
        _service.StatusChanged -= OnStatusChanged;

        // Le letture in volo si scartano (_detailSeq e _attached) e le immagini si rilasciano subito.
        _detailSeq++;
        _detailData = null;
        _wantedFrame = -1;
        _shownFrame = -1;
        _keogramBar.SetImage(null);
        _keogramBar.SetCursor(null);
        _frameImage.Source = null;
        _startrailsImage.Source = null;
        _player.Release();
    }

    // Eventi dal thread dell'Engine: si torna sul thread dell'interfaccia prima di toccare la vista.
    private void OnFrameProduced(FrameEvent frame)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(OnFrameOnUi));
    }

    private void OnFrameOnUi()
    {
        if (_attached && DateTime.UtcNow - _lastRefreshUtc >= RefreshInterval)
        {
            _ = RefreshAllAsync();
        }
    }

    private void OnStatusChanged(StatusDto status)
    {
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => OnStatusOnUi(status)));
    }

    private void OnStatusOnUi(StatusDto status)
    {
        if (!_attached)
        {
            return;
        }

        _jobs = status.Jobs.Where(j => j.Kind == TimelapseKind).ToList();
        var busy = _jobs.Select(j => j.NightId).ToHashSet(StringComparer.Ordinal);
        bool changed = !busy.SetEquals(_busyNights);
        _busyNights = busy;

        // Un lavoro è partito o finito: l'elenco va riletto per lo stato del timelapse.
        if (changed)
        {
            _ = RefreshAllAsync();
        }

        ApplyTimelapseControls();
    }

    // ---- Letture in background ----

    private async Task RefreshAllAsync()
    {
        _lastRefreshUtc = DateTime.UtcNow;
        _pendingDetailReload = true;
        await RefreshListAsync();
    }

    private async Task RefreshListAsync()
    {
        if (_listBusy)
        {
            _listAgain = true;
            return;
        }

        _listBusy = true;
        try
        {
            do
            {
                _listAgain = false;
                NightsData.NightList result = await Task.Run(() => NightsData.ListNights(_service));
                if (!_attached)
                {
                    break;
                }

                ApplyList(result);
            }
            while (_listAgain && _attached);
        }
        finally
        {
            _listBusy = false;
        }
    }

    private async Task LoadDetailAsync()
    {
        string? id = _selectedId;
        if (id is null)
        {
            return;
        }

        int seq = ++_detailSeq;
        NightsData.NightDetail detail = await Task.Run(() => NightsData.LoadDetail(_service, id));
        if (!_attached || seq != _detailSeq || id != _selectedId)
        {
            return;
        }

        ApplyDetail(detail);
    }

    // Eseguito una sola volta alla volta per vista: legge il frame richiesto per ultimo e scarta quelli superati.
    private async Task PumpFrameAsync()
    {
        if (_frameBusy)
        {
            return;
        }

        _frameBusy = true;
        try
        {
            while (_attached
                && _detailData is { } detail
                && _wantedFrame >= 0
                && _wantedFrame < detail.Frames.Count
                && _shownFrame != _wantedFrame)
            {
                int index = _wantedFrame;
                string nightId = detail.NightId;
                string frameId = detail.Frames[index].Id;
                BitmapSource? image = await Task.Run(() => NightsData.LoadFrame(_service, nightId, frameId));

                // Se nel frattempo sono cambiate la notte o il frame richiesto, si riparte dal nuovo stato.
                if (!_attached || _detailData != detail || index != _wantedFrame)
                {
                    continue;
                }

                _shownFrame = index;
                _frameImage.Source = image;
                SetMessage(_frameMessage, image is null ? _texts.NightsFrameMissing : null);
            }
        }
        finally
        {
            _frameBusy = false;
        }
    }

    // ---- Applicazione dei risultati (thread dell'interfaccia) ----

    private void ApplyList(NightsData.NightList result)
    {
        NightSummary? before = SelectedNight();
        _loaded = true;
        if (result.Error is null)
        {
            _nights = result.Nights;
            _listError = null;
        }
        else
        {
            // Si conserva l'elenco precedente: un errore momentaneo non deve svuotare la vista.
            _listError = result.Error;
        }

        RebuildList();
        UpdateListStatus();

        NightSummary? after = SelectedNight();
        bool timelapseChanged = before is not null && after is not null && before.Timelapse != after.Timelapse;
        bool needDetail = _pendingDetailReload
            || timelapseChanged
            || (_selectedId is not null && _detailData?.NightId != _selectedId);
        _pendingDetailReload = false;
        if (needDetail)
        {
            _ = LoadDetailAsync();
        }

        ApplyNightHeader();
        ApplyTimelapseControls();
    }

    private void ApplyDetail(NightsData.NightDetail detail)
    {
        bool sameNight = _detailData?.NightId == detail.NightId;
        _detailData = detail;
        _shownFrame = -1;

        int count = detail.Frames.Count;
        int target = count == 0
            ? -1
            : sameNight && _wantedFrame >= 0 ? Math.Min(_wantedFrame, count - 1) : 0;

        _keogramBar.SetImage(detail.Keogram);
        _keogramBar.SetMessage(detail.Keogram is null ? _texts.NightsKeogramMissing : null);
        _startrailsImage.Source = detail.Startrails;
        SetMessage(_startrailsMessage, detail.Startrails is null ? _texts.NightsStartrailsMissing : null);
        _actionStatus.Text = detail.Error is null
            ? string.Empty
            : string.Format(_texts.Culture, _texts.NightsLoadError, detail.Error);

        ApplyTimelapseControls();
        ShowFrame(target);
    }

    private void RebuildList()
    {
        string? previous = _selectedId;
        _rebuilding = true;
        try
        {
            _list.Items.Clear();
            foreach (NightSummary night in _nights)
            {
                _list.Items.Add(CreateItem(NightRowFormatter.Format(night, _texts)));
            }

            int index = _nights.Count == 0 ? -1 : Math.Max(0, IndexOfNight(previous));
            _list.SelectedIndex = index;
            _selectedId = index < 0 ? null : _nights[index].Id;
        }
        finally
        {
            _rebuilding = false;
        }

        if (_selectedId != previous)
        {
            ResetDetail();
        }

        UpdateSelectionVisibility();
    }

    private void UpdateListStatus()
    {
        NightsPanelState state = NightsViewStates.Resolve(_loaded, _nights.Count, _listError is not null);
        string? text = state switch
        {
            NightsPanelState.Loading => _texts.NightsLoading,
            NightsPanelState.Empty => _texts.EmptyNights,
            NightsPanelState.Error => string.Format(_texts.Culture, _texts.NightsLoadError, _listError),
            _ => _listError is null ? null : string.Format(_texts.Culture, _texts.NightsLoadError, _listError),
        };

        SetMessage(_listStatus, text);
        _list.Visibility = _nights.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyNightHeader()
    {
        NightSummary? night = SelectedNight();
        if (night is null)
        {
            _title.Text = string.Empty;
            _subtitle.Text = string.Empty;
            return;
        }

        NightRow row = NightRowFormatter.Format(night, _texts);
        _title.Text = row.Title;
        _subtitle.Text = row.Subtitle;
    }

    private void ApplyTimelapseControls()
    {
        NightSummary? night = SelectedNight();
        if (night is null)
        {
            return;
        }

        JobDto? job = _jobs.FirstOrDefault(j => j.NightId == night.Id);
        TimelapseControlState state = TimelapseControls.Resolve(night.Timelapse, night.Frames, job?.Progress, _texts);

        _timelapseStatus.Text = state.StatusText;
        _regenerate.IsEnabled = state.CanRegenerate && !_regenBusy;

        // Durante una rigenerazione il file non si apre: il motore lo sta per sostituire.
        if (_regenBusy || !state.CanPlay)
        {
            _player.ShowPlaceholder(_regenBusy ? _texts.NightsTimelapseQueued : state.StatusText);
            return;
        }

        if (_detailData is not { } detail || detail.NightId != night.Id)
        {
            _player.ShowPlaceholder(_texts.NightsLoading);
            return;
        }

        if (detail.TimelapsePath is { } path)
        {
            _player.Show(path);
        }
        else
        {
            _player.ShowPlaceholder(_texts.NightsTimelapseMissingFile);
        }
    }

    private void ShowFrame(int index)
    {
        if (_detailData is null)
        {
            _wantedFrame = -1;
            return;
        }

        int count = _detailData.Frames.Count;
        if (count == 0)
        {
            _wantedFrame = -1;
            _keogramBar.SetCursor(null);
            _frameImage.Source = null;
            _shownFrame = -1;
            SetMessage(_frameMessage, _texts.NightsNoFrames);
            _frameInfo.Text = string.Empty;
            return;
        }

        int clamped = Math.Clamp(index, 0, count - 1);
        _wantedFrame = clamped;
        FrameDto frame = _detailData.Frames[clamped];
        _keogramBar.SetCursor(KeogramScrubber.FractionOfFrame(clamped, count));

        NightFrameText text = NightFrameFormatter.Format(clamped, count, frame.Timestamp, frame.Phase, _texts);
        _frameInfo.Text = text.Position + " · " + text.Time + " · " + text.Phase;
        _frameInfo.ToolTip = text.TimeTooltip;

        _ = PumpFrameAsync();
    }

    /// <summary>Riporta la vista allo stato "in caricamento" per la notte selezionata, senza leggere nulla.</summary>
    private void ResetDetail()
    {
        _detailSeq++;
        _detailData = null;
        _wantedFrame = -1;
        _shownFrame = -1;
        _player.Release();

        _keogramBar.SetImage(null);
        _keogramBar.SetCursor(null);
        _keogramBar.SetMessage(_selectedId is null ? null : _texts.NightsLoading);
        _frameImage.Source = null;
        SetMessage(_frameMessage, _selectedId is null ? null : _texts.NightsLoading);
        _frameInfo.Text = string.Empty;
        _startrailsImage.Source = null;
        SetMessage(_startrailsMessage, _selectedId is null ? null : _texts.NightsLoading);

        ApplyNightHeader();
        UpdateSelectionVisibility();
    }

    private void UpdateSelectionVisibility()
    {
        bool hasSelection = _selectedId is not null;
        _detailPane.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        _hintPanel.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Interazione ----

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding)
        {
            return;
        }

        if (_list.SelectedItem is ListBoxItem { Tag: string id } && id != _selectedId)
        {
            _selectedId = id;
            ResetDetail();
            _ = LoadDetailAsync();
        }
    }

    private void OnFractionRequested(double fraction)
    {
        if (_detailData is not { } detail || detail.Frames.Count == 0)
        {
            return;
        }

        ShowFrame(KeogramScrubber.FrameIndexAt(fraction, detail.Frames.Count));
    }

    private void OnStepRequested(int delta)
    {
        if (_detailData is null)
        {
            return;
        }

        ShowFrame(FrameNavigation.Step(_wantedFrame, delta, _detailData.Frames.Count));
    }

    private async Task RegenerateAsync()
    {
        string? id = _selectedId;
        if (id is null || _regenBusy || !_attached)
        {
            return;
        }

        _regenBusy = true;
        _regenerate.IsEnabled = false;
        _player.ShowPlaceholder(_texts.NightsTimelapseQueued);
        try
        {
            bool queued = await Task.Run(() => _service.RequestTimelapseAsync(id));
            _actionStatus.Text = queued ? _texts.NightsTimelapseRequested : _texts.NightsTimelapseRefused;
        }
        catch (Exception ex)
        {
            _actionStatus.Text = string.Format(_texts.Culture, _texts.NightsTimelapseRegenerateError, ex.Message);
        }
        finally
        {
            _regenBusy = false;
        }

        if (_attached)
        {
            await RefreshAllAsync();
        }
    }

    // ---- Helper ----

    private NightSummary? SelectedNight() => _selectedId is null ? null : _nights.FirstOrDefault(n => n.Id == _selectedId);

    private int IndexOfNight(string? id)
    {
        for (int i = 0; i < _nights.Count; i++)
        {
            if (_nights[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    private static void SetMessage(TextBlock text, string? message)
    {
        text.Text = message ?? string.Empty;
        text.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private ListBoxItem CreateItem(NightRow row)
    {
        var title = new TextBlock
        {
            Text = row.Title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

        var subtitle = new TextBlock
        {
            Text = row.Subtitle,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        subtitle.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        subtitle.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");

        // Lo stato è detto anche con il testo, non solo con il colore.
        var state = new TextBlock { Text = row.TimelapseLabel, Margin = new Thickness(0, 2, 0, 0) };
        state.SetResourceReference(TextBlock.ForegroundProperty, TimelapseBrush(row.Timelapse));
        state.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");

        var content = new StackPanel();
        content.Children.Add(title);
        content.Children.Add(subtitle);
        content.Children.Add(state);

        var item = new ListBoxItem
        {
            Content = content,
            Tag = row.Id,
            Style = ListItemStyle(),
        };
        AutomationProperties.SetName(item, row.Title + ", " + row.Subtitle + ", " + row.TimelapseLabel);
        return item;
    }

    private static string TimelapseBrush(NightTimelapseState state) => state switch
    {
        NightTimelapseState.Ready => "Brush.Success",
        NightTimelapseState.Generating => "Brush.Warning",
        NightTimelapseState.Failed => "Brush.Error",
        _ => "Brush.TextSecondary",
    };

    private Style ListItemStyle() => _listItemStyle ??= (Style)XamlReader.Parse(ListItemStyleXaml);

    private static Style PanelStyle() => (Style)System.Windows.Application.Current.FindResource("Panel");

    private TextBlock CreatePanelTitle(string text)
    {
        var title = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(FrameworkElement.StyleProperty, "PanelTitle");
        return title;
    }

    private TextBlock Note(string text, Thickness margin)
    {
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        note.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        return note;
    }

    // ---- Layout ----

    private UIElement BuildLayout()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement listPanel = BuildListPanel();
        Grid.SetColumn(listPanel, 0);
        root.Children.Add(listPanel);

        var right = new Grid { Margin = new Thickness(16, 0, 0, 0) };
        _hintPanel.Style = PanelStyle();
        _hintPanel.VerticalAlignment = VerticalAlignment.Top;
        _hintPanel.Child = Note(_texts.NightsSelectNight, new Thickness(0));
        right.Children.Add(_hintPanel);

        right.Children.Add(BuildDetail());
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        UpdateSelectionVisibility();
        return root;
    }

    private UIElement BuildListPanel()
    {
        _list.ItemContainerStyle = ListItemStyle();
        _list.BorderThickness = new Thickness(0);
        _list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _list.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Brush.Surface1");
        _list.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Brush.Text");
        _list.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Brush.Border");
        AutomationProperties.SetName(_list, _texts.NightsListName);

        _listStatus.TextWrapping = TextWrapping.Wrap;
        _listStatus.Margin = new Thickness(4, 0, 4, 8);
        _listStatus.Visibility = Visibility.Collapsed;
        _listStatus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _listStatus.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        var stack = new DockPanel();
        DockPanel.SetDock(_listStatus, Dock.Top);
        stack.Children.Add(_listStatus);
        stack.Children.Add(_list);

        return new Border
        {
            Style = PanelStyle(),
            Child = stack,
        };
    }

    private ScrollViewer BuildDetail()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(340) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Intestazione: la data della notte, come informazione dominante.
        _title.FontWeight = FontWeights.SemiBold;
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _title.SetResourceReference(TextBlock.FontSizeProperty, "Size.Display");
        _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _subtitle.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        _subtitle.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        _actionStatus.TextWrapping = TextWrapping.Wrap;
        _actionStatus.Margin = new Thickness(0, 8, 0, 0);
        _actionStatus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _actionStatus.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(_title);
        header.Children.Add(_subtitle);
        header.Children.Add(_actionStatus);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // Keogram: barra di scorrimento temporale.
        var keogramStack = new StackPanel();
        keogramStack.Children.Add(CreatePanelTitle(_texts.NightsKeogramTitle));
        keogramStack.Children.Add(_keogramBar);
        keogramStack.Children.Add(Note(_texts.NightsKeogramHint, new Thickness(0, 8, 0, 0)));
        var keogramPanel = new Border
        {
            Style = PanelStyle(),
            Margin = new Thickness(0, 0, 0, 16),
            Child = keogramStack,
        };
        Grid.SetRow(keogramPanel, 1);
        grid.Children.Add(keogramPanel);

        // Frame selezionato e startrails.
        var middle = new Grid();
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement framePanel = BuildImagePanel(
            _texts.NightsFrameTitle, _frameImage, _frameMessage, _texts.NightsFrameName, _frameInfo, new Thickness(0, 0, 8, 0));
        Grid.SetColumn(framePanel, 0);
        middle.Children.Add(framePanel);

        UIElement startrailsPanel = BuildImagePanel(
            _texts.NightsStartrailsTitle, _startrailsImage, _startrailsMessage, _texts.NightsStartrailsName, null, new Thickness(8, 0, 0, 0));
        Grid.SetColumn(startrailsPanel, 1);
        middle.Children.Add(startrailsPanel);

        Grid.SetRow(middle, 2);
        grid.Children.Add(middle);

        // Timelapse: riproduzione e rigenerazione.
        var timelapseStack = new StackPanel();
        timelapseStack.Children.Add(CreatePanelTitle(_texts.NightsTimelapseTitle));
        timelapseStack.Children.Add(_player);

        _timelapseStatus.VerticalAlignment = VerticalAlignment.Center;
        _timelapseStatus.Margin = new Thickness(0, 0, 16, 0);
        _timelapseStatus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        _timelapseStatus.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        _regenerate.Content = _texts.NightsTimelapseRegenerate;
        _regenerate.ToolTip = _texts.NightsTimelapseRegenerateTooltip;
        _regenerate.MinHeight = 32;
        _regenerate.IsEnabled = false;
        _regenerate.SetResourceReference(FrameworkElement.StyleProperty, "Button.Primary");
        AutomationProperties.SetName(_regenerate, _texts.NightsTimelapseRegenerate);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(_timelapseStatus);
        actions.Children.Add(_regenerate);
        timelapseStack.Children.Add(actions);

        var timelapsePanel = new Border
        {
            Style = PanelStyle(),
            Margin = new Thickness(0, 16, 0, 0),
            Child = timelapseStack,
        };
        Grid.SetRow(timelapsePanel, 3);
        grid.Children.Add(timelapsePanel);

        _detailPane.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _detailPane.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _detailPane.Content = grid;
        return _detailPane;
    }

    private UIElement BuildImagePanel(
        string title,
        Image image,
        TextBlock message,
        string imageName,
        TextBlock? info,
        Thickness margin)
    {
        image.Stretch = System.Windows.Media.Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        AutomationProperties.SetName(image, imageName);

        message.TextWrapping = TextWrapping.Wrap;
        message.TextAlignment = TextAlignment.Center;
        message.HorizontalAlignment = HorizontalAlignment.Center;
        message.VerticalAlignment = VerticalAlignment.Center;
        message.Margin = new Thickness(16);
        message.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        message.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        message.Visibility = Visibility.Collapsed;

        var imageArea = new Grid();
        imageArea.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface1");
        imageArea.Children.Add(image);
        imageArea.Children.Add(message);

        var dock = new DockPanel();
        TextBlock titleBlock = CreatePanelTitle(title);
        DockPanel.SetDock(titleBlock, Dock.Top);
        dock.Children.Add(titleBlock);
        if (info is not null)
        {
            info.TextWrapping = TextWrapping.Wrap;
            info.Margin = new Thickness(0, 8, 0, 0);
            info.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            info.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
            info.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
            DockPanel.SetDock(info, Dock.Bottom);
            dock.Children.Add(info);
        }

        dock.Children.Add(imageArea);

        return new Border
        {
            Style = PanelStyle(),
            Margin = margin,
            Child = dock,
        };
    }
}
