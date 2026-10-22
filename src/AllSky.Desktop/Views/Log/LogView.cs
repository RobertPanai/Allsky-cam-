using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using AllSky.Core;
using AllSky.Core.LogView;

namespace AllSky.Desktop.Views.Log;

/// <summary>
/// Vista "Log": ultime voci del log giornaliero con filtro per gravità e testo, scorrimento automatico in fondo,
/// copia negli appunti e apertura del file. I segreti sono mascherati prima di arrivare alla UI.
/// </summary>
/// <remarks>
/// La lettura avviene in fondo al file (ultimi 256 KB) fuori dal thread della UI, ogni 3 secondi mentre la vista è aperta.
/// Filtro e ricerca lavorano sulle voci già lette, senza rileggere il disco. La logica sta in <c>AllSky.Core/LogView</c>.
/// </remarks>
public class LogView : UserControl
{
    /// <summary>Byte finali del file letti a ogni aggiornamento.</summary>
    private const int TailBytes = 256 * 1024;

    /// <summary>Numero massimo di voci mostrate, a partire dalle più recenti.</summary>
    private const int MaxShown = 500;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly AppPaths _paths;
    private readonly DesktopTexts _texts;
    private readonly DispatcherTimer _timer;

    private ComboBox _levelBox = null!;
    private TextBox _searchBox = null!;
    private CheckBox _autoScrollBox = null!;
    private Button _refreshButton = null!;
    private Button _copyButton = null!;
    private Button _openButton = null!;
    private ListBox _list = null!;
    private TextBlock _countText = null!;
    private TextBlock _fileText = null!;
    private TextBlock _emptyText = null!;
    private TextBlock _statusText = null!;

    private IReadOnlyList<LogRecord> _records = Array.Empty<LogRecord>();
    private IReadOnlyList<LogRecord> _shown = Array.Empty<LogRecord>();
    private string? _currentFile;
    private bool _readFailed;
    private bool _reading;
    private bool _attached;

    /// <summary>Crea la vista con i testi nella lingua scelta. Va creata sul thread dell'interfaccia.</summary>
    /// <param name="paths">Cartelle dell'applicazione: il log è in <see cref="AppPaths.LogsFolder"/>.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public LogView(AppPaths paths, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(texts);

        _paths = paths;
        _texts = texts;
        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += OnTick;

        Content = BuildLayout();
        AutomationProperties.SetName(this, texts.LogViewTitle);

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
        _timer.Start();
        await RefreshAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _attached = false;
        _timer.Stop();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (IsVisible && !_reading)
        {
            await RefreshAsync();
        }
    }

    // ---- Lettura e rendering ----

    private async Task RefreshAsync()
    {
        if (_reading)
        {
            return;
        }

        _reading = true;
        _refreshButton.IsEnabled = false;
        try
        {
            LogSnapshot snapshot = await Task.Run(ReadSnapshot);
            _currentFile = snapshot.FilePath;
            _records = snapshot.Records;
            _readFailed = false;
        }
        catch (Exception)
        {
            _readFailed = true;
        }
        finally
        {
            _reading = false;
            _refreshButton.IsEnabled = true;
            Render();
        }
    }

    private LogSnapshot ReadSnapshot()
    {
        string logsFolder = _paths.LogsFolder;
        string? file = LogFiles.LatestDailyLog(logsFolder);
        if (file is null)
        {
            return new LogSnapshot(null, Array.Empty<LogRecord>());
        }

        IReadOnlyList<string> lines = LogFiles.ReadTailLines(file, TailBytes);
        return new LogSnapshot(file, LogParser.Parse(lines));
    }

    private void Render()
    {
        _shown = LogFilter.Apply(_records, CurrentLevel(), _searchBox.Text, MaxShown);

        _list.Items.Clear();
        foreach (LogRecord record in _shown)
        {
            _list.Items.Add(BuildEntry(record));
        }

        _countText.Text = string.Format(_texts.Culture, _texts.LogViewCount, _shown.Count);
        _fileText.Text = _currentFile is null
            ? string.Empty
            : string.Format(_texts.Culture, _texts.LogViewFile, Path.GetFileName(_currentFile));

        _copyButton.IsEnabled = _shown.Count > 0;
        _openButton.IsEnabled = _currentFile is not null;

        if (_readFailed)
        {
            SetEmpty(_texts.LogViewReadError);
        }
        else if (_currentFile is null)
        {
            SetEmpty(_texts.LogViewNoFile);
        }
        else if (_shown.Count == 0)
        {
            SetEmpty(_texts.LogViewEmpty);
        }
        else
        {
            SetEmpty(null);
        }

        if (_autoScrollBox.IsChecked == true && _list.Items.Count > 0)
        {
            _list.ScrollIntoView(_list.Items[_list.Items.Count - 1]);
        }
    }

    private void SetEmpty(string? text)
    {
        _emptyText.Text = text ?? string.Empty;
        _emptyText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private LogLevelFilter CurrentLevel()
    {
        return _levelBox.SelectedItem is ComboBoxItem { Tag: LogLevelFilter level } ? level : LogLevelFilter.All;
    }

    /// <summary>Una voce: ora, etichetta di gravità in testo (non solo colore) e testo già mascherato.</summary>
    private TextBlock BuildEntry(LogRecord record)
    {
        string time = record.Timestamp is { } stamp
            ? stamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : "--:--:--";

        string tag = record.Level switch
        {
            LogLevel.Error => _texts.LogViewTagError,
            LogLevel.Warning => _texts.LogViewTagWarning,
            _ => _texts.LogViewTagInfo,
        };

        var entry = new TextBlock
        {
            Text = time + "  " + tag + ":  " + record.Text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2),
        };
        entry.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        entry.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        entry.SetResourceReference(TextBlock.ForegroundProperty, record.Level switch
        {
            LogLevel.Error => "Brush.Error",
            LogLevel.Warning => "Brush.Warning",
            _ => "Brush.TextSecondary",
        });
        return entry;
    }

    // ---- Azioni ----

    private void CopyVisible()
    {
        if (_shown.Count == 0)
        {
            return;
        }

        string text = string.Join(Environment.NewLine, _shown.Select(r => (r.Timestamp is { } t
            ? t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  "
            : string.Empty) + r.Text));

        try
        {
            Clipboard.SetText(text);
            SetStatus(_texts.LogViewCopied, error: false);
        }
        catch (Exception)
        {
            // Appunti occupati da un'altra applicazione: si ritenta al clic successivo.
            SetStatus(_texts.LogViewReadError, error: true);
        }
    }

    private void OpenFile()
    {
        if (_currentFile is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_currentFile) { UseShellExecute = true })?.Dispose();
            SetStatus(string.Empty, error: false);
        }
        catch (Exception)
        {
            SetStatus(string.Format(_texts.Culture, _texts.LogViewOpenError, _currentFile), error: true);
        }
    }

    private void SetStatus(string text, bool error)
    {
        _statusText.Text = text;
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "Brush.Error" : "Brush.Success");
    }

    // ---- Layout ----

    private UIElement BuildLayout()
    {
        var header = new TextBlock
        {
            Text = _texts.LogViewTitle,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        header.SetResourceReference(TextBlock.FontSizeProperty, "Size.Display");

        _fileText = BuildCaption("Brush.TextSecondary");
        _countText = BuildCaption("Brush.TextSecondary");
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        meta.Children.Add(_countText);
        meta.Children.Add(new TextBlock { Text = "  ·  " });
        meta.Children.Add(_fileText);

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        toolbar.Children.Add(BuildLabel(_texts.LogViewLevelLabel));

        _levelBox = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        _levelBox.SetResourceReference(Control.BackgroundProperty, "Brush.Surface2");
        _levelBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _levelBox.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        _levelBox.Items.Add(new ComboBoxItem { Content = _texts.LogViewLevelAll, Tag = LogLevelFilter.All });
        _levelBox.Items.Add(new ComboBoxItem { Content = _texts.LogViewLevelWarnings, Tag = LogLevelFilter.WarningsAndErrors });
        _levelBox.Items.Add(new ComboBoxItem { Content = _texts.LogViewLevelErrors, Tag = LogLevelFilter.ErrorsOnly });
        _levelBox.SelectedIndex = 0;
        AutomationProperties.SetName(_levelBox, _texts.LogViewLevelLabel);
        _levelBox.SelectionChanged += (_, _) => Render();
        toolbar.Children.Add(_levelBox);

        _searchBox = new TextBox { Width = 240, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_searchBox, _texts.LogViewSearchLabel);
        _searchBox.TextChanged += (_, _) => Render();
        toolbar.Children.Add(_searchBox);

        _autoScrollBox = new CheckBox
        {
            Content = _texts.LogViewAutoScroll,
            IsChecked = true,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _autoScrollBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        AutomationProperties.SetName(_autoScrollBox, _texts.LogViewAutoScroll);
        _autoScrollBox.Checked += (_, _) => Render();
        toolbar.Children.Add(_autoScrollBox);

        _refreshButton = BuildButton(_texts.LogViewRefresh, primary: false);
        _refreshButton.Click += async (_, _) => await RefreshAsync();
        toolbar.Children.Add(_refreshButton);

        _copyButton = BuildButton(_texts.LogViewCopy, primary: false);
        _copyButton.Click += (_, _) => CopyVisible();
        toolbar.Children.Add(_copyButton);

        _openButton = BuildButton(_texts.LogViewOpenFile, primary: false);
        _openButton.Click += (_, _) => OpenFile();
        toolbar.Children.Add(_openButton);

        var note = BuildCaption("Brush.TextTertiary");
        note.Text = _texts.LogViewSecretsNote;
        note.Margin = new Thickness(0, 0, 0, 8);

        _list = new ListBox
        {
            Margin = new Thickness(0),
            SelectionMode = SelectionMode.Extended,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        _list.SetResourceReference(Control.BackgroundProperty, "Brush.Surface1");
        _list.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _list.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        AutomationProperties.SetName(_list, _texts.LogViewTitle);

        _emptyText = BuildCaption("Brush.TextSecondary");
        _emptyText.Margin = new Thickness(0, 12, 0, 0);
        _emptyText.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");

        _statusText = BuildCaption("Brush.Success");
        _statusText.Margin = new Thickness(0, 8, 0, 0);

        var listArea = new Grid();
        listArea.Children.Add(_list);
        listArea.Children.Add(_emptyText);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(root, header, 0);
        AddRow(root, meta, 1);
        AddRow(root, toolbar, 2);
        AddRow(root, note, 3);
        AddRow(root, listArea, 4);
        AddRow(root, _statusText, 5);
        return root;
    }

    private static void AddRow(Grid grid, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }

    private TextBlock BuildLabel(string text)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        label.SetResourceReference(TextBlock.FontSizeProperty, "Size.Body");
        return label;
    }

    private TextBlock BuildCaption(string brushKey)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        block.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        return block;
    }

    private Button BuildButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 32,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, primary ? "Button.Primary" : "Button.Secondary");
        AutomationProperties.SetName(button, text);
        return button;
    }

    private sealed record LogSnapshot(string? FilePath, IReadOnlyList<LogRecord> Records);
}
