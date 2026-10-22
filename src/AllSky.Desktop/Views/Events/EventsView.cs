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
using System.Windows.Threading;
using AllSky.Core;
using AllSky.Core.Events;
using AllSky.Desktop.Theme;
using Microsoft.Win32;

namespace AllSky.Desktop.Views.Events;

/// <summary>
/// Vista "Eventi": galleria di meteore, bolidi e transiti con filtri per notte e per tipo, dettaglio con la mini-clip e i
/// dati, etichette "Confermato" e "Falso positivo" per tarare le soglie, ed esportazione dell'evento in uno zip.
/// </summary>
/// <remarks>
/// <para>
/// L'archivio si legge in background (<see cref="IEventBrowser"/>): la lista completa si carica una volta e i filtri si
/// applicano in memoria con <see cref="EventListRules"/>. Le miniature si decodificano fuori dal thread dell'interfaccia e
/// si congelano; una lettura vecchia viene scartata quando ne parte una nuova.
/// </para>
/// <para>
/// Allo scarico si fermano il timer di aggiornamento, la clip e le immagini, e si svuotano le miniature. Alla ricarica la
/// vista rilegge l'archivio. Una scrittura in corso (etichetta, esportazione) non si ripete finché non finisce.
/// </para>
/// <para>
/// Etichetta: premere lo stesso pulsante di nuovo toglie l'etichetta. Il file dell'evento non cambia mai.
/// </para>
/// </remarks>
public sealed class EventsView : UserControl
{
    private const string ItemStyleXaml = """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
               xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
               TargetType="ListBoxItem">
          <Setter Property="Background" Value="Transparent"/>
          <Setter Property="Foreground" Value="{DynamicResource Brush.Text}"/>
          <Setter Property="FontFamily" Value="{DynamicResource Font.Ui}"/>
          <Setter Property="FontSize" Value="{DynamicResource Size.Body}"/>
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="Padding" Value="{DynamicResource Inset.Button}"/>
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
                                    VerticalAlignment="Top"/>
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

    private const string ItemsPanelXaml = """
        <ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <WrapPanel Orientation="Horizontal"/>
        </ItemsPanelTemplate>
        """;

    /// <summary>Intervallo tra due riletture dell'archivio mentre la vista è aperta.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private const double ThumbWidth = 176;
    private const double ThumbHeight = 99;

    private readonly IEventBrowser _events;
    private readonly DesktopTexts _texts;
    private readonly Style _itemStyle;
    private readonly DispatcherTimer _timer;

    private readonly ComboBox _nightFilter = new();
    private readonly ComboBox _typeFilter = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _count = new();
    private readonly TextBlock _listMessage = new();
    private readonly TextBlock _noMatch = new();
    private readonly Grid _browser = new();
    private readonly ContentControl _emptyState = new();

    private readonly TextBlock _detailMessage = new();
    private readonly StackPanel _detailBody = new();
    private readonly TextBlock _detailTitle = new();
    private readonly TextBlock _detailSubtitle = new();
    private readonly Image _image = new();
    private readonly TextBlock _imageMessage = new();
    private readonly EventClipPlayer _clip;
    private readonly Grid _dataGrid = new();
    private readonly TextBlock _labelState = new();
    private readonly Button _confirm = new();
    private readonly Button _falsePositive = new();
    private readonly Button _export = new();
    private readonly TextBlock _actionMessage = new();

    private IReadOnlyList<EventListItem> _all = Array.Empty<EventListItem>();
    private EventDetail? _detail;
    private EventKey? _selected;
    private readonly Dictionary<string, BitmapSource> _thumbs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _thumbsLoading = new(StringComparer.Ordinal);
    private readonly List<ThumbSlot> _thumbSlots = new();
    private int _listSeq;
    private int _detailSeq;
    private int _imageSeq;
    private bool _attached;
    private bool _busy;
    private bool _suppressSelection;
    private bool _suppressFilters;

    /// <summary>Crea la vista. Va creata sul thread dell'interfaccia; non legge l'archivio finché non è caricata.</summary>
    /// <param name="events">Archivio di meteore, bolidi e transiti.</param>
    /// <param name="texts">Testi della lingua scelta.</param>
    public EventsView(IEventBrowser events, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(texts);

        _events = events;
        _texts = texts;
        _itemStyle = (Style)XamlReader.Parse(ItemStyleXaml);
        _clip = new EventClipPlayer(texts);
        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += (_, _) => _ = RefreshAsync();

        Content = BuildLayout();
        AutomationProperties.SetName(this, texts.EventsNav);

        _list.SelectionChanged += OnSelectionChanged;
        _nightFilter.SelectionChanged += OnFilterChanged;
        _typeFilter.SelectionChanged += OnFilterChanged;
        _confirm.Click += (_, _) => _ = SetLabelAsync(EventLabelChoice.Confirmed);
        _falsePositive.Click += (_, _) => _ = SetLabelAsync(EventLabelChoice.FalsePositive);
        _export.Click += (_, _) => _ = ExportAsync();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        SetDetailPlaceholder(texts.EventsSelectPrompt);
        UpdateButtons();
    }

    // ---- Ciclo di vita ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        _timer.Start();
        _ = RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;
        _timer.Stop();
        _listSeq++;
        _all = Array.Empty<EventListItem>();
        _listMessage.Text = string.Empty;
        ClearGallery();
        ClearDetail();
    }

    // ---- Lettura dell'archivio ----

    /// <summary>Rilegge l'archivio e ricostruisce filtri e galleria. Una lettura superata viene scartata.</summary>
    private async Task RefreshAsync()
    {
        if (!_attached)
        {
            return;
        }

        int seq = ++_listSeq;
        try
        {
            IReadOnlyList<EventListItem> all = await Task.Run(() => EventsData.ListAll(_events));
            if (seq != _listSeq || !_attached)
            {
                return;
            }

            _all = all;
            _listMessage.Text = string.Empty;
            RebuildFilterOptions();
            RebuildGallery();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (seq != _listSeq || !_attached)
            {
                return;
            }

            _listMessage.Text = string.Format(_texts.Culture, _texts.EventsListError, ex.Message);
        }
    }

    // ---- Filtri e galleria ----

    private EventFilter CurrentFilter()
    {
        string? night = (_nightFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        EventCategory? category = null;
        if ((_typeFilter.SelectedItem as ComboBoxItem)?.Tag is EventCategory selected)
        {
            category = selected;
        }

        return new EventFilter(night, category);
    }

    /// <summary>Riempie il filtro per notte con le notti presenti, mantenendo la scelta se la notte esiste ancora.</summary>
    private void RebuildFilterOptions()
    {
        object? keep = (_nightFilter.SelectedItem as ComboBoxItem)?.Tag;
        _suppressFilters = true;
        try
        {
            _nightFilter.Items.Clear();
            _nightFilter.Items.Add(new ComboBoxItem { Content = _texts.EventsFilterAllNights, Tag = null });
            foreach (string night in EventListRules.Nights(_all))
            {
                _nightFilter.Items.Add(new ComboBoxItem { Content = night, Tag = night });
            }

            SelectByTag(_nightFilter, keep);
        }
        finally
        {
            _suppressFilters = false;
        }
    }

    /// <summary>Riempie la galleria con gli eventi che passano il filtro, e riseleziona quello aperto se c'è ancora.</summary>
    private void RebuildGallery()
    {
        bool none = _all.Count == 0;
        IReadOnlyList<EventListItem> visible = EventListRules.Filter(_all, CurrentFilter());

        _browser.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        _emptyState.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        _count.Text = none ? string.Empty : string.Format(_texts.Culture, _texts.EventsCount, visible.Count);
        _noMatch.Visibility = !none && visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _thumbSlots.Clear();
        ListBoxItem? selectedItem = null;
        _suppressSelection = true;
        try
        {
            _list.Items.Clear();
            foreach (EventListItem item in visible)
            {
                ListBoxItem element = CreateItem(item);
                _list.Items.Add(element);
                if (_selected is { } key && key.NightId == item.NightId && key.Id == item.Id)
                {
                    selectedItem = element;
                }
            }

            _list.SelectedItem = selectedItem;
        }
        finally
        {
            _suppressSelection = false;
        }

        // L'evento aperto non è più nell'elenco filtrato: il dettaglio torna al suggerimento.
        if (_selected is not null && selectedItem is null)
        {
            ClearDetail();
        }
    }

    private ListBoxItem CreateItem(EventListItem item)
    {
        string caption = EventFormatter.ListCaption(item, _texts);

        var image = new Image
        {
            Width = ThumbWidth,
            Height = ThumbHeight,
            Stretch = System.Windows.Media.Stretch.Uniform,
        };
        if (item.ThumbnailPath is { } path)
        {
            if (_thumbs.TryGetValue(path, out BitmapSource? cached))
            {
                image.Source = cached;
            }
            else
            {
                _thumbSlots.Add(new ThumbSlot(path, image));
                _ = LoadThumbnailAsync(path);
            }
        }

        var frame = new Border
        {
            Width = ThumbWidth,
            Height = ThumbHeight,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = image,
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Surface2");
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");

        var text = new TextBlock
        {
            Text = caption,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 6, 0, 0),
            MaxWidth = ThumbWidth,
        };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        text.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        var stack = new StackPanel { Width = ThumbWidth };
        stack.Children.Add(frame);
        stack.Children.Add(text);

        var element = new ListBoxItem
        {
            Content = stack,
            Tag = item,
            Style = _itemStyle,
            Margin = new Thickness(0, 0, 12, 12),
        };
        AutomationProperties.SetName(element, caption);
        return element;
    }

    /// <summary>Decodifica una miniatura fuori dal thread dell'interfaccia e la applica a tutte le caselle che la mostrano.</summary>
    private async Task LoadThumbnailAsync(string path)
    {
        if (!_thumbsLoading.Add(path))
        {
            return;
        }

        BitmapSource? bitmap = await Task.Run(() => EventsData.LoadImage(path, EventsData.ThumbnailDecodeWidth));
        _thumbsLoading.Remove(path);
        if (bitmap is null || !_attached)
        {
            return;
        }

        _thumbs[path] = bitmap;
        foreach (ThumbSlot slot in _thumbSlots)
        {
            if (string.Equals(slot.Path, path, StringComparison.Ordinal))
            {
                slot.Image.Source = bitmap;
            }
        }
    }

    private void ClearGallery()
    {
        _suppressSelection = true;
        try
        {
            _list.Items.Clear();
        }
        finally
        {
            _suppressSelection = false;
        }

        _thumbSlots.Clear();
        _thumbs.Clear();
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilters || !_attached)
        {
            return;
        }

        RebuildGallery();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || !_attached)
        {
            return;
        }

        if (_list.SelectedItem is ListBoxItem { Tag: EventListItem item })
        {
            _ = ShowDetailAsync(item);
        }
    }

    // ---- Dettaglio ----

    private async Task ShowDetailAsync(EventListItem item)
    {
        int seq = ++_detailSeq;
        _selected = new EventKey(item.NightId, item.Id);
        _detail = null;
        _clip.Release();
        _actionMessage.Text = string.Empty;
        SetDetailPlaceholder(_texts.EventsStatusLoading);
        UpdateButtons();

        try
        {
            EventDetail? detail = await Task.Run(() => _events.Get(item.NightId, item.Id));
            if (seq != _detailSeq || !_attached)
            {
                return;
            }

            if (detail is null)
            {
                _selected = null;
                SetDetailPlaceholder(_texts.EventsDetailMissing);
                UpdateButtons();
                return;
            }

            ApplyDetail(detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (seq != _detailSeq)
            {
                return;
            }

            _selected = null;
            SetDetailPlaceholder(string.Format(_texts.Culture, _texts.EventsDetailMissing, ex.Message));
            UpdateButtons();
        }
    }

    /// <summary>Mostra un dettaglio già letto. Con la stessa clip già caricata la riproduzione non riparte.</summary>
    private void ApplyDetail(EventDetail detail)
    {
        _detail = detail;
        _selected = new EventKey(detail.NightId, detail.Id);

        _detailMessage.Visibility = Visibility.Collapsed;
        _detailBody.Visibility = Visibility.Visible;
        _detailTitle.Text = EventFormatter.CategoryName(detail.Category, _texts);
        _detailSubtitle.Text = EventFormatter.FormatLocalDateTime(detail.TimestampUtc);
        _detailSubtitle.ToolTip = EventFormatter.FormatUtc(detail.TimestampUtc);
        BuildDataRows(EventFormatter.DataRows(detail, _texts));

        if (detail.ClipPath is { } clip)
        {
            _clip.Show(clip);
        }
        else
        {
            _clip.ShowPlaceholder(_texts.EventsClipMissing);
        }

        _ = LoadDetailImageAsync(detail);
        UpdateButtons();
    }

    private async Task LoadDetailImageAsync(EventDetail detail)
    {
        int seq = ++_imageSeq;
        string? path = EventFormatter.PreferredImagePath(detail);
        _image.Source = null;
        if (path is null)
        {
            ShowImageMessage(_texts.EventsImageMissing);
            return;
        }

        BitmapSource? bitmap = await Task.Run(() => EventsData.LoadImage(path, EventsData.ImageDecodeWidth));
        if (seq != _imageSeq || !_attached)
        {
            return;
        }

        if (bitmap is null)
        {
            ShowImageMessage(_texts.EventsImageMissing);
            return;
        }

        _image.Source = bitmap;
        _imageMessage.Visibility = Visibility.Collapsed;
    }

    private void ShowImageMessage(string text)
    {
        _image.Source = null;
        _imageMessage.Text = text;
        _imageMessage.Visibility = Visibility.Visible;
    }

    private void BuildDataRows(IReadOnlyList<EventDataRow> rows)
    {
        _dataGrid.Children.Clear();
        _dataGrid.RowDefinitions.Clear();
        for (int i = 0; i < rows.Count; i++)
        {
            EventDataRow row = rows[i];
            _dataGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = row.Label,
                Margin = new Thickness(0, 4, 16, 4),
                TextWrapping = TextWrapping.Wrap,
            };
            label.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Ui");
            label.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
            label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
            Grid.SetRow(label, i);
            Grid.SetColumn(label, 0);
            _dataGrid.Children.Add(label);

            var value = new TextBlock
            {
                Text = row.Value,
                Margin = new Thickness(0, 4, 0, 4),
                TextWrapping = TextWrapping.Wrap,
                ToolTip = row.Tooltip,
            };
            value.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
            value.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
            value.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            _dataGrid.Children.Add(value);
        }
    }

    private void ClearDetail()
    {
        _selected = null;
        _detail = null;
        _detailSeq++;
        _imageSeq++;
        _clip.Release();
        _image.Source = null;
        _imageMessage.Visibility = Visibility.Collapsed;
        _actionMessage.Text = string.Empty;
        _dataGrid.Children.Clear();
        _dataGrid.RowDefinitions.Clear();
        _labelState.Text = string.Empty;
        SetDetailPlaceholder(_texts.EventsSelectPrompt);
        UpdateButtons();
    }

    private void SetDetailPlaceholder(string text)
    {
        _detailMessage.Text = text;
        _detailMessage.Visibility = Visibility.Visible;
        _detailBody.Visibility = Visibility.Collapsed;
    }

    private void UpdateButtons()
    {
        bool enabled = _detail is not null && !_busy;
        _confirm.IsEnabled = enabled;
        _falsePositive.IsEnabled = enabled;
        _export.IsEnabled = enabled;
        _labelState.Text = _detail is null ? string.Empty : EventFormatter.LabelName(_detail.Label, _texts);
    }

    // ---- Etichette ed esportazione ----

    /// <summary>
    /// Imposta l'etichetta. Se è già quella scelta la toglie. La scrittura va in background; alla fine il dettaglio e
    /// la galleria si riallineano all'archivio.
    /// </summary>
    private async Task SetLabelAsync(EventLabelChoice wanted)
    {
        EventDetail? current = _detail;
        if (current is null || _busy)
        {
            return;
        }

        EventLabelChoice next = current.Label == wanted ? EventLabelChoice.None : wanted;
        _busy = true;
        _actionMessage.Text = string.Empty;
        UpdateButtons();
        try
        {
            EventDetail updated = await Task.Run(() => _events.SetLabel(current.NightId, current.Id, next));
            if (!_attached)
            {
                return;
            }

            _actionMessage.Text = string.Format(_texts.Culture, _texts.EventsLabelSaved, EventFormatter.LabelName(next, _texts));
            if (_selected is { } key && key.NightId == current.NightId && key.Id == current.Id)
            {
                ApplyDetail(updated);
            }

            _ = RefreshAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _actionMessage.Text = string.Format(_texts.Culture, _texts.EventsLabelError, ex.Message);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>Esporta l'evento in uno zip scelto dall'utente. Il file zip non può stare nella cartella dell'evento.</summary>
    private async Task ExportAsync()
    {
        EventDetail? current = _detail;
        if (current is null || _busy)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = _texts.EventsExportDialogTitle,
            Filter = _texts.EventsExportFilter,
            FileName = current.Id + ".zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        string target = dialog.FileName;
        _busy = true;
        _actionMessage.Text = _texts.EventsExportBusy;
        UpdateButtons();
        try
        {
            string written = await Task.Run(() => _events.ExportZip(current.NightId, current.Id, target));
            _actionMessage.Text = string.Format(_texts.Culture, _texts.EventsExportDone, written);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _actionMessage.Text = string.Format(_texts.Culture, _texts.EventsExportError, ex.Message);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    // ---- Layout ----

    private UIElement BuildLayout()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Intestazione: titolo e conteggio a sinistra, filtri a destra.
        var title = new TextBlock { Text = _texts.EventsNav, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Ui");
        title.SetResourceReference(TextBlock.FontSizeProperty, "Size.Display");
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

        _count.Margin = new Thickness(0, 4, 0, 0);
        _count.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        _count.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        _count.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        var titleStack = new StackPanel();
        titleStack.Children.Add(title);
        titleStack.Children.Add(_count);

        var filters = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        filters.Children.Add(BuildFilter(_texts.EventsFilterNight, _nightFilter, 200));
        filters.Children.Add(BuildFilter(_texts.EventsFilterType, _typeFilter, 160));
        PopulateFilters();

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(titleStack);
        Grid.SetColumn(filters, 1);
        header.Children.Add(filters);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _listMessage.TextWrapping = TextWrapping.Wrap;
        _listMessage.Margin = new Thickness(0, 0, 0, 12);
        _listMessage.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");
        Grid.SetRow(_listMessage, 1);
        root.Children.Add(_listMessage);

        UIElement content = BuildContent();
        Grid.SetRow(content, 2);
        root.Children.Add(content);
        return root;
    }

    /// <summary>Area sotto l'intestazione: galleria e dettaglio, oppure lo stato vuoto se non c'è nessun evento.</summary>
    private UIElement BuildContent()
    {
        var content = new Grid();

        // Galleria a sinistra, dettaglio a destra.
        _browser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
        _browser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        _browser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });

        var gallery = new Grid();
        gallery.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gallery.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _noMatch.Text = _texts.EventsNoMatch;
        _noMatch.Margin = new Thickness(0, 0, 0, 12);
        _noMatch.Visibility = Visibility.Collapsed;
        _noMatch.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        Grid.SetRow(_noMatch, 0);
        gallery.Children.Add(_noMatch);

        _list.ItemsPanel = (ItemsPanelTemplate)XamlReader.Parse(ItemsPanelXaml);
        _list.BorderThickness = new Thickness(0);
        _list.Background = System.Windows.Media.Brushes.Transparent;
        _list.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Brush.Text");
        _list.SelectionMode = SelectionMode.Single;
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(_list, _texts.EventsNav);
        Grid.SetRow(_list, 1);
        gallery.Children.Add(_list);
        Grid.SetColumn(gallery, 0);
        _browser.Children.Add(gallery);

        // Dettaglio: messaggio (suggerimento, caricamento) oppure il corpo con immagine, clip, dati ed etichette.
        var detail = new Grid();
        _detailMessage.TextWrapping = TextWrapping.Wrap;
        _detailMessage.Margin = new Thickness(0, 24, 0, 0);
        _detailMessage.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        detail.Children.Add(_detailMessage);
        detail.Children.Add(BuildDetailBody());
        Grid.SetColumn(detail, 2);
        _browser.Children.Add(detail);

        content.Children.Add(_browser);

        _emptyState.Content = BuildEmptyState();
        _emptyState.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyState.VerticalAlignment = VerticalAlignment.Center;
        _emptyState.Visibility = Visibility.Collapsed;
        content.Children.Add(_emptyState);

        return content;
    }

    private UIElement BuildDetailBody()
    {
        _detailBody.Visibility = Visibility.Collapsed;
        _detailTitle.FontWeight = FontWeights.SemiBold;
        _detailTitle.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Ui");
        _detailTitle.SetResourceReference(TextBlock.FontSizeProperty, "Size.Subtitle");
        _detailTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _detailSubtitle.Margin = new Thickness(0, 2, 0, 16);
        _detailSubtitle.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        _detailSubtitle.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        _detailSubtitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        _detailBody.Children.Add(_detailTitle);
        _detailBody.Children.Add(_detailSubtitle);

        // Immagine principale: il ritaglio per le scie, la composita per i transiti.
        _image.Height = 240;
        _image.Stretch = System.Windows.Media.Stretch.Uniform;
        _imageMessage.TextWrapping = TextWrapping.Wrap;
        _imageMessage.Visibility = Visibility.Collapsed;
        _imageMessage.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        var imageArea = new Grid();
        imageArea.Children.Add(_image);
        imageArea.Children.Add(_imageMessage);
        _detailBody.Children.Add(BuildPanel(_texts.EventsPanelImage, imageArea));

        _detailBody.Children.Add(BuildPanel(_texts.EventsPanelClip, _clip));

        _dataGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _dataGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _detailBody.Children.Add(BuildPanel(_texts.EventsPanelData, _dataGrid));

        // Etichette e azioni. Le etichette non cancellano l'evento: servono a tarare le soglie.
        _labelState.Margin = new Thickness(0, 0, 0, 8);
        _labelState.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _labelState.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Ui");

        _confirm.Content = _texts.EventsButtonConfirm;
        _confirm.SetResourceReference(FrameworkElement.StyleProperty, "Button.Primary");
        _confirm.MinHeight = 32;
        _confirm.Margin = new Thickness(0, 0, 8, 8);
        AutomationProperties.SetName(_confirm, _texts.EventsButtonConfirm);

        _falsePositive.Content = _texts.EventsButtonFalsePositive;
        _falsePositive.SetResourceReference(FrameworkElement.StyleProperty, "Button.Secondary");
        _falsePositive.MinHeight = 32;
        _falsePositive.Margin = new Thickness(0, 0, 8, 8);
        AutomationProperties.SetName(_falsePositive, _texts.EventsButtonFalsePositive);

        _export.Content = _texts.EventsButtonExport;
        _export.SetResourceReference(FrameworkElement.StyleProperty, "Button.Secondary");
        _export.MinHeight = 32;
        _export.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetName(_export, _texts.EventsButtonExport);

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_confirm);
        buttons.Children.Add(_falsePositive);
        buttons.Children.Add(_export);

        var hint = new TextBlock
        {
            Text = _texts.EventsLabelHint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        hint.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        _actionMessage.TextWrapping = TextWrapping.Wrap;
        _actionMessage.Margin = new Thickness(0, 4, 0, 0);
        _actionMessage.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        var actions = new StackPanel();
        actions.Children.Add(_labelState);
        actions.Children.Add(buttons);
        actions.Children.Add(hint);
        actions.Children.Add(_actionMessage);
        _detailBody.Children.Add(BuildPanel(null, actions));

        var scroll = new ScrollViewer
        {
            Content = _detailBody,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        return scroll;
    }

    private UIElement BuildEmptyState()
    {
        var panel = new StackPanel { MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(Illustrations.Create(IllustrationKind.NoEvents, _texts));
        return panel;
    }

    /// <summary>Riquadro con il titolo in maiuscoletto, come gli altri pannelli dell'app. Il titolo può mancare.</summary>
    private Border BuildPanel(string? title, UIElement content)
    {
        var stack = new StackPanel();
        if (title is not null)
        {
            var heading = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 12) };
            heading.SetResourceReference(FrameworkElement.StyleProperty, "PanelTitle");
            stack.Children.Add(heading);
        }

        stack.Children.Add(content);
        var panel = new Border
        {
            Child = stack,
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16),
            VerticalAlignment = VerticalAlignment.Top,
        };
        panel.SetResourceReference(FrameworkElement.StyleProperty, "Panel");
        return panel;
    }

    private static StackPanel BuildFilter(string label, ComboBox box, double width)
    {
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) };
        caption.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        caption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        box.Width = width;
        AutomationProperties.SetName(box, label);
        var stack = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        stack.Children.Add(caption);
        stack.Children.Add(box);
        return stack;
    }

    /// <summary>Voci fisse dei filtri: nessuna notte e tutti i tipi. Le notti si aggiungono con l'archivio.</summary>
    private void PopulateFilters()
    {
        _typeFilter.Items.Add(new ComboBoxItem { Content = _texts.EventsFilterAllTypes, Tag = null });
        _typeFilter.Items.Add(new ComboBoxItem { Content = EventFormatter.CategoryName(EventCategory.Meteor, _texts), Tag = EventCategory.Meteor });
        _typeFilter.Items.Add(new ComboBoxItem { Content = EventFormatter.CategoryName(EventCategory.Bolide, _texts), Tag = EventCategory.Bolide });
        _typeFilter.Items.Add(new ComboBoxItem { Content = EventFormatter.CategoryName(EventCategory.Transit, _texts), Tag = EventCategory.Transit });
        _typeFilter.SelectedIndex = 0;
        _nightFilter.Items.Add(new ComboBoxItem { Content = _texts.EventsFilterAllNights, Tag = null });
        _nightFilter.SelectedIndex = 0;
    }

    /// <summary>Seleziona la voce con lo stesso tag, o la prima se non c'è più.</summary>
    private static void SelectByTag(ComboBox box, object? tag)
    {
        foreach (object item in box.Items)
        {
            if (item is ComboBoxItem candidate && Equals(candidate.Tag, tag))
            {
                box.SelectedItem = candidate;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    /// <summary>Chiave di un evento: notte e identificatore.</summary>
    private readonly record struct EventKey(string NightId, string Id);

    /// <summary>Un'immagine della galleria in attesa della sua miniatura.</summary>
    private sealed record ThumbSlot(string Path, Image Image);
}
