using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AllSky.Core;
using AllSky.Core.Calibration;
using AllSky.Desktop.Views.Live;
using AllSky.Engine;
using AllSky.Sky;
using Shapes = System.Windows.Shapes;

namespace AllSky.Desktop.Views.Camera;

/// <summary>
/// Pannello "Calibrazione ottica" (P3.3): stato della raccolta dei punti dal Sole, anteprima dell'ultimo frame con la griglia
/// alt/az sovrapposta, accetta/rifiuta della proposta e modalita' manuale con anteprima immediata.
/// </summary>
/// <remarks>
/// <para>
/// La geometria (linee della griglia, punti cardinali, residui, validazione del form) viene da <c>AllSky.Core/Calibration</c>.
/// Qui restano solo i controlli WPF: le linee sono <see cref="Shapes.Path"/> su un <see cref="Canvas"/> sopra l'immagine.
/// </para>
/// <para>
/// Le chiamate che possono bloccare (accettazione con salvataggio) girano fuori dal thread UI. Gli eventi arrivano dal thread
/// dell'Engine e si riportano sul thread UI con <see cref="Dispatcher"/>.
/// </para>
/// </remarks>
public sealed class CalibrationPanel : Border
{
    private const double MaxPreviewWidth = 520.0;
    private const double LabelOffsetPx = 14.0;
    private const double PointDiameterPx = 6.0;
    private const double FieldWidthPx = 200.0;

    private readonly IAllSkyService _service;
    private readonly ICalibrationService _calibration;
    private readonly DesktopTexts _texts;

    private TextBlock _statusText = null!;
    private TextBlock _hintText = null!;
    private TextBlock _failureText = null!;
    private TextBlock _pointsText = null!;
    private TextBlock _residualText = null!;
    private TextBlock _actionText = null!;
    private TextBlock _emptyFrameText = null!;
    private TextBlock _legendText = null!;
    private Grid _previewHost = null!;
    private Image _image = null!;
    private Canvas _overlay = null!;
    private Button _acceptButton = null!;
    private Button _rejectButton = null!;

    private TextBox _cxBox = null!;
    private TextBox _cyBox = null!;
    private TextBox _radiusBox = null!;
    private TextBox _k2Box = null!;
    private TextBox _rotBox = null!;
    private TextBlock _cxProblem = null!;
    private TextBlock _cyProblem = null!;
    private TextBlock _radiusProblem = null!;
    private TextBlock _k2Problem = null!;
    private TextBlock _rotProblem = null!;
    private CheckBox _mirrorBox = null!;
    private TextBlock _manualPreviewText = null!;
    private TextBlock _manualInvalidText = null!;
    private Button _manualUseButton = null!;
    private Button _manualResetButton = null!;

    private CalibrationSnapshot _snapshot;
    private BitmapSource? _frame;
    private ManualCalibrationCheck? _manualCheck;
    private bool _attached;
    private bool _busy;
    private bool _manualTouched;
    private bool _suppressManual;
    private double _displayWidth;

    /// <summary>Crea il pannello. Va creato sul thread dell'interfaccia.</summary>
    /// <param name="service">Servizio dell'Engine, per l'ultimo frame.</param>
    /// <param name="calibration">Flusso di calibrazione.</param>
    /// <param name="texts">Testi nella lingua scelta.</param>
    public CalibrationPanel(IAllSkyService service, ICalibrationService calibration, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(texts);

        _service = service;
        _calibration = calibration;
        _texts = texts;
        _snapshot = calibration.GetSnapshot();

        Padding = new Thickness(16);
        VerticalAlignment = VerticalAlignment.Top;
        SetResourceReference(FrameworkElement.StyleProperty, "Panel");
        AutomationProperties.SetName(this, texts.CalibTitle);

        Child = BuildLayout();
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
        _calibration.SnapshotChanged += OnSnapshotChanged;
        _service.FrameProduced += OnFrameProduced;
        _snapshot = _calibration.GetSnapshot();
        FillManualFromReference();
        Render();
        _ = LoadFrameAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _attached = false;
        _calibration.SnapshotChanged -= OnSnapshotChanged;
        _service.FrameProduced -= OnFrameProduced;
    }

    // Evento dal thread dell'Engine: si torna sul thread dell'interfaccia.
    private void OnSnapshotChanged()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_attached)
            {
                _snapshot = _calibration.GetSnapshot();
                Render();
            }
        }));
    }

    // Evento dal thread di cattura: si ricarica l'ultimo frame sul thread dell'interfaccia.
    private void OnFrameProduced(FrameEvent frame)
    {
        Dispatcher.BeginInvoke(new Action(OnFrameReady));
    }

    private async void OnFrameReady()
    {
        if (_attached)
        {
            await LoadFrameAsync();
        }
    }

    // ---- Frame ----

    private async Task LoadFrameAsync()
    {
        byte[]? jpeg = _service.GetLatestJpeg(raw: false);
        if (jpeg is null)
        {
            return;
        }

        try
        {
            BitmapSource bitmap = await Task.Run(() => FrameDecoder.Decode(jpeg));
            if (!_attached)
            {
                return;
            }

            _frame = bitmap;
            _image.Source = bitmap;
            FitPreview();
        }
        catch (NotSupportedException)
        {
            // Frame non decodificabile: resta l'ultima anteprima valida.
        }
    }

    // Adatta l'immagine alla larghezza disponibile e ridisegna la griglia alla nuova scala.
    private void FitPreview()
    {
        if (_frame is null)
        {
            _previewHost.ClearValue(FrameworkElement.HeightProperty);
            _emptyFrameText.Visibility = Visibility.Visible;
            _previewHost.Visibility = Visibility.Collapsed;
            _displayWidth = 0.0;
            DrawOverlay();
            return;
        }

        _emptyFrameText.Visibility = Visibility.Collapsed;
        _previewHost.Visibility = Visibility.Visible;

        double available = _previewHost.ActualWidth > 0.0 ? _previewHost.ActualWidth : MaxPreviewWidth;
        double width = Math.Min(available, MaxPreviewWidth);
        double height = width * _frame.PixelHeight / _frame.PixelWidth;

        _image.Width = width;
        _image.Height = height;
        _overlay.Width = width;
        _overlay.Height = height;
        _previewHost.Height = height;
        _displayWidth = width;
        DrawOverlay();
    }

    // ---- Disegno della griglia ----

    // Modello da disegnare: anteprima manuale, poi proposta (con residui), poi modello accettato.
    private void DrawOverlay()
    {
        _overlay.Children.Clear();
        if (_frame is null || _displayWidth <= 0.0)
        {
            return;
        }

        int referenceWidth = _snapshot.FrameWidthPx > 0 ? _snapshot.FrameWidthPx : _frame.PixelWidth;
        double scale = _displayWidth / referenceWidth;

        FisheyeModel? model;
        bool showResiduals = false;
        if (ManualPreviewModel() is { } manual)
        {
            model = manual;
        }
        else if (_snapshot.Proposal is { } proposal)
        {
            model = proposal.Model;
            showResiduals = true;
        }
        else
        {
            model = _snapshot.Accepted?.ToModel();
        }

        if (model is not null)
        {
            foreach (GridLine line in SkyOverlayGeometry.DefaultGrid(model))
            {
                AddPolyline(line.Points, scale, line.IsAltitude ? "Brush.Accent" : "Brush.TextTertiary",
                    line.IsAltitude ? 0.8 : 0.45);
            }
        }

        if (showResiduals && _snapshot.Proposal is { } shown)
        {
            foreach (CalibrationResidual residual in shown.Residuals)
            {
                if (residual.Predicted is { } predicted)
                {
                    AddSegment(Map(new PixelPoint(residual.Observed.X, residual.Observed.Y), scale),
                        Map(predicted, scale));
                }
            }
        }

        foreach (CalibrationPoint point in _snapshot.Points)
        {
            AddDot(Map(new PixelPoint(point.X, point.Y), scale), PointDiameterPx, "Brush.Sun");
        }

        if (model is not null)
        {
            Point center = Map(new PixelPoint(model.CenterX, model.CenterY), scale);
            AddDot(center, 4.0, "Brush.Accent");
            foreach (CardinalMark mark in SkyOverlayGeometry.Cardinals(model))
            {
                AddCardinalLabel(mark, center, scale);
            }
        }
    }

    private void AddPolyline(IReadOnlyList<PixelPoint> points, double scale, string brushKey, double opacity)
    {
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(Map(points[0], scale), isFilled: false, isClosed: false);
            for (int i = 1; i < points.Count; i++)
            {
                context.LineTo(Map(points[i], scale), isStroked: true, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        var path = new Shapes.Path { Data = geometry, StrokeThickness = 1.0, Opacity = opacity };
        path.SetResourceReference(Shapes.Shape.StrokeProperty, brushKey);
        _overlay.Children.Add(path);
    }

    private void AddSegment(Point from, Point to)
    {
        var line = new Shapes.Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            StrokeThickness = 1.0,
            Opacity = 0.8,
        };
        line.SetResourceReference(Shapes.Shape.StrokeProperty, "Brush.Warning");
        _overlay.Children.Add(line);
    }

    private void AddDot(Point at, double diameter, string brushKey)
    {
        var dot = new Shapes.Ellipse { Width = diameter, Height = diameter };
        dot.SetResourceReference(Shapes.Shape.FillProperty, brushKey);
        Canvas.SetLeft(dot, at.X - diameter / 2.0);
        Canvas.SetTop(dot, at.Y - diameter / 2.0);
        AutomationProperties.SetName(dot, _texts.CalibObservedName);
        _overlay.Children.Add(dot);
    }

    private void AddCardinalLabel(CardinalMark mark, Point center, double scale)
    {
        Point horizon = Map(mark.Horizon, scale);
        double dx = horizon.X - center.X;
        double dy = horizon.Y - center.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        // Etichetta un po' fuori dall'orizzonte, nella direzione dal centro. Senza direzione valida resta sul punto.
        double offsetX = length > 0.5 ? dx / length * LabelOffsetPx : 0.0;
        double offsetY = length > 0.5 ? dy / length * LabelOffsetPx : 0.0;

        var label = new TextBlock { Text = CardinalText(mark.Direction) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        label.SetResourceReference(TextBlock.FontSizeProperty, "Size.Caption");
        Canvas.SetLeft(label, horizon.X + offsetX - 4.0);
        Canvas.SetTop(label, horizon.Y + offsetY - 8.0);
        _overlay.Children.Add(label);
    }

    private string CardinalText(CardinalDirection direction) => direction switch
    {
        CardinalDirection.North => _texts.CalibCardinalNorth,
        CardinalDirection.East => _texts.CalibCardinalEast,
        CardinalDirection.South => _texts.CalibCardinalSouth,
        _ => _texts.CalibCardinalWest,
    };

    private static Point Map(PixelPoint p, double scale) => new(p.X * scale, p.Y * scale);

    // ---- Stato e pulsanti ----

    private void Render()
    {
        RenderStatus();
        RenderButtons();
        FitPreview();
    }

    private void RenderStatus()
    {
        CalibrationSnapshot s = _snapshot;
        CultureInfo c = _texts.Culture;
        CalibrationProposal? proposal = s.Proposal;

        switch (s.Phase)
        {
            case CalibrationPhase.Review when proposal is not null:
                if (proposal.Source == OpticalCalibrationSource.Manual)
                {
                    _statusText.Text = _texts.CalibStatusReviewManual;
                }
                else if (proposal.Acceptable)
                {
                    _statusText.Text = _texts.CalibStatusReview;
                }
                else
                {
                    _statusText.Text = string.Format(c, _texts.CalibStatusReviewNotAcceptable,
                        proposal.RmsPx.ToString("F2", c), CalibrationFlow.AcceptancePercent.ToString("F0", c));
                }

                _hintText.Text = string.Empty;
                break;

            case CalibrationPhase.Calibrated when s.Accepted is { } accepted:
                string source = accepted.SourceKind == OpticalCalibrationSource.Manual
                    ? _texts.CalibSourceManual
                    : _texts.CalibSourceSun;
                string date = accepted.CalibratedAtUtc is { } at
                    ? at.ToLocalTime().ToString("d", c)
                    : string.Empty;
                _statusText.Text = string.Format(c, _texts.CalibStatusCalibrated,
                    accepted.RmsPx.ToString("F2", c), source, date);
                _hintText.Text = string.Empty;
                break;

            default:
                _statusText.Text = string.Format(c, _texts.CalibStatusCollecting, s.Points.Count, s.MinPoints);
                _hintText.Text = _texts.CalibStatusCollectingHint;
                break;
        }

        _failureText.Text = s.FailureReason is null ? string.Empty : string.Format(c, _texts.CalibFailureReason, s.FailureReason);
        _failureText.Visibility = s.FailureReason is null ? Visibility.Collapsed : Visibility.Visible;

        _pointsText.Text = s.Points.Count == 0
            ? _texts.CalibNoPoints
            : string.Format(c, _texts.CalibLastPoint, s.Points[^1].Utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));

        _residualText.Text = proposal is null
            ? string.Empty
            : string.Format(c, _texts.CalibResidualSummary, proposal.RmsPx.ToString("F2", c), proposal.PointsUsed, proposal.OutliersRejected);
        _residualText.Visibility = proposal is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderButtons()
    {
        CalibrationProposal? proposal = _snapshot.Proposal;
        _acceptButton.IsEnabled = !_busy && proposal is { Acceptable: true };
        _rejectButton.IsEnabled = !_busy && proposal is not null;
        _manualUseButton.IsEnabled = !_busy && _manualCheck is { IsValid: true } && _snapshot.FrameWidthPx > 0;
        _manualResetButton.IsEnabled = !_busy;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RenderButtons();
    }

    // Esegue un'azione del servizio fuori dal thread UI e ne mostra l'esito.
    private async Task RunAsync(Func<CalibrationActionResult> action, string successText, Action? onSuccess)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            CalibrationActionResult result = await Task.Run(action);
            if (!_attached)
            {
                return;
            }

            if (result == CalibrationActionResult.Ok)
            {
                _actionText.Text = successText;
                onSuccess?.Invoke();
            }
            else
            {
                _actionText.Text = ActionMessage(result);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Errore inatteso dal servizio: l'esito resta visibile e il dettaglio va nel log dell'Engine.
            _actionText.Text = _texts.CalibActionSaveFailed;
        }
        finally
        {
            if (_attached)
            {
                _snapshot = _calibration.GetSnapshot();
                SetBusy(false);
                Render();
            }
        }
    }

    private string ActionMessage(CalibrationActionResult result) => result switch
    {
        CalibrationActionResult.NoProposal => _texts.CalibActionNoProposal,
        CalibrationActionResult.NotAcceptable => _texts.CalibActionNotAcceptable,
        CalibrationActionResult.NoFrame => _texts.CalibActionNoFrame,
        CalibrationActionResult.SaveFailed => _texts.CalibActionSaveFailed,
        _ => string.Empty,
    };

    private async void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => _calibration.AcceptProposal(), _texts.CalibAcceptedNotice, null);
    }

    private async void OnRejectClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => _calibration.RejectProposal(), _texts.CalibRejectedNotice, null);
    }

    // ---- Modalita' manuale ----

    // Modello dell'anteprima manuale: solo dopo una modifica dell'utente e con tutti i campi validi.
    private FisheyeModel? ManualPreviewModel() => _manualTouched && _manualCheck is { IsValid: true } check ? check.Model : null;

    private void OnManualEdited()
    {
        if (_suppressManual)
        {
            return;
        }

        _manualTouched = true;
        RefreshManual();
    }

    private void RefreshManual()
    {
        _manualCheck = CalibrationManualForm.Check(
            _cxBox.Text, _cyBox.Text, _radiusBox.Text, _k2Box.Text, _rotBox.Text,
            _mirrorBox.IsChecked == true, _texts.Culture);

        SetProblem(_cxProblem, ProblemText(_manualCheck.CenterXProblem));
        SetProblem(_cyProblem, ProblemText(_manualCheck.CenterYProblem));
        SetProblem(_radiusProblem, ProblemText(_manualCheck.RadiusProblem));
        SetProblem(_k2Problem, ProblemText(_manualCheck.K2Problem));
        SetProblem(_rotProblem, ProblemText(_manualCheck.RotationProblem));

        bool preview = ManualPreviewModel() is not null;
        _manualPreviewText.Text = preview ? _texts.CalibManualPreviewOn : string.Empty;
        _manualPreviewText.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;

        bool invalid = _manualTouched && !_manualCheck.IsValid;
        _manualInvalidText.Text = invalid ? _texts.CalibManualInvalid : string.Empty;
        _manualInvalidText.Visibility = invalid ? Visibility.Visible : Visibility.Collapsed;

        Render();
    }

    private string? ProblemText(ManualFieldProblem problem) => problem switch
    {
        ManualFieldProblem.Empty => _texts.CalibProblemEmpty,
        ManualFieldProblem.NotNumber => _texts.CalibProblemNotNumber,
        ManualFieldProblem.NotPositive => _texts.CalibProblemNotPositive,
        ManualFieldProblem.OutOfRange => _texts.CalibProblemOutOfRange,
        _ => null,
    };

    private static void SetProblem(TextBlock block, string? text)
    {
        block.Text = text ?? string.Empty;
        block.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // Riempie il form con il modello di riferimento (proposta, poi accettato, poi valori centrati sul frame).
    private void FillManualFromReference()
    {
        FisheyeModel? reference = _snapshot.Proposal?.Model ?? _snapshot.Accepted?.ToModel();
        CultureInfo c = _texts.Culture;

        _suppressManual = true;
        try
        {
            if (reference is not null)
            {
                FillFields(reference.CenterX, reference.CenterY, reference.HorizonRadius, reference.K2,
                    reference.NorthRotationDeg, reference.Mirrored, c);
            }
            else if (_snapshot.FrameWidthPx > 0 && _snapshot.FrameHeightPx > 0)
            {
                int w = _snapshot.FrameWidthPx;
                int h = _snapshot.FrameHeightPx;
                FillFields(w / 2.0, h / 2.0, Math.Min(w, h) / 2.0, 0.0, 0.0, false, c);
            }
            else
            {
                _cxBox.Text = string.Empty;
                _cyBox.Text = string.Empty;
                _radiusBox.Text = string.Empty;
                _k2Box.Text = string.Empty;
                _rotBox.Text = string.Empty;
                _mirrorBox.IsChecked = false;
            }
        }
        finally
        {
            _suppressManual = false;
        }

        _manualTouched = false;
        _manualCheck = CalibrationManualForm.Check(
            _cxBox.Text, _cyBox.Text, _radiusBox.Text, _k2Box.Text, _rotBox.Text,
            _mirrorBox.IsChecked == true, c);
    }

    private void FillFields(double cx, double cy, double horizonRadius, double k2, double rotation, bool mirrored, CultureInfo c)
    {
        _cxBox.Text = CalibrationManualForm.Format(cx, c, 1);
        _cyBox.Text = CalibrationManualForm.Format(cy, c, 1);
        _radiusBox.Text = CalibrationManualForm.Format(horizonRadius, c, 1);
        _k2Box.Text = CalibrationManualForm.Format(k2, c, 3);
        _rotBox.Text = CalibrationManualForm.Format(rotation, c, 1);
        _mirrorBox.IsChecked = mirrored;
    }

    private async void OnManualUseClick(object sender, RoutedEventArgs e)
    {
        FisheyeModel? model = ManualPreviewModel() ?? _manualCheck?.Model;
        if (model is null)
        {
            _actionText.Text = _texts.CalibManualInvalid;
            return;
        }

        await RunAsync(() => _calibration.SetManual(model), string.Empty, () => _manualTouched = false);
    }

    private void OnManualResetClick(object sender, RoutedEventArgs e)
    {
        FillManualFromReference();
        Render();
    }

    // ---- Layout ----

    private UIElement BuildLayout()
    {
        var stack = new StackPanel();
        stack.Children.Add(BuildPanelTitle(_texts.CalibTitle));

        _statusText = BuildBodyText(string.Empty, "Brush.Text");
        _hintText = BuildBodyText(string.Empty, "Brush.TextSecondary", "Size.Caption");
        _failureText = BuildBodyText(string.Empty, "Brush.Warning", "Size.Caption");
        _failureText.Visibility = Visibility.Collapsed;
        _pointsText = BuildBodyText(string.Empty, "Brush.TextSecondary", "Size.Caption");
        stack.Children.Add(_statusText);
        stack.Children.Add(_hintText);
        stack.Children.Add(_failureText);
        stack.Children.Add(_pointsText);

        stack.Children.Add(BuildPreview());

        _residualText = BuildBodyText(string.Empty, "Brush.TextSecondary", "Size.Caption");
        _residualText.Visibility = Visibility.Collapsed;
        stack.Children.Add(_residualText);

        _acceptButton = BuildButton(_texts.CalibAccept, primary: true);
        _acceptButton.Click += OnAcceptClick;
        _rejectButton = BuildButton(_texts.CalibReject, primary: false);
        _rejectButton.Click += OnRejectClick;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(_acceptButton);
        _rejectButton.Margin = new Thickness(8, 4, 0, 0);
        actions.Children.Add(_rejectButton);
        stack.Children.Add(actions);

        _actionText = BuildBodyText(string.Empty, "Brush.Text");
        stack.Children.Add(_actionText);

        stack.Children.Add(BuildManualSection());
        return stack;
    }

    private UIElement BuildPreview()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

        _emptyFrameText = BuildBodyText(_texts.CalibFrameEmpty, "Brush.TextSecondary", "Size.Caption");
        stack.Children.Add(_emptyFrameText);

        _image = new Image
        {
            Stretch = System.Windows.Media.Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetName(_image, _texts.CalibFrameTitle);

        _overlay = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true,
        };
        AutomationProperties.SetName(_overlay, _texts.CalibGridLegend);

        _previewHost = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        _previewHost.Children.Add(_image);
        _previewHost.Children.Add(_overlay);
        _previewHost.SizeChanged += (_, _) => FitPreview();
        stack.Children.Add(_previewHost);

        _legendText = BuildBodyText(_texts.CalibGridLegend, "Brush.TextTertiary", "Size.Caption");
        stack.Children.Add(_legendText);

        var title = BuildBodyText(_texts.CalibFrameTitle, "Brush.TextSecondary", "Size.Caption");
        title.Margin = new Thickness(0, 0, 0, 4);
        stack.Children.Insert(0, title);
        return stack;
    }

    private UIElement BuildManualSection()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
        stack.Children.Add(BuildPanelTitle(_texts.CalibManualTitle));
        stack.Children.Add(BuildBodyText(_texts.CalibManualHint, "Brush.TextSecondary", "Size.Caption"));

        _cxBox = BuildTextBox(_texts.CalibManualCenterX);
        _cyBox = BuildTextBox(_texts.CalibManualCenterY);
        _radiusBox = BuildTextBox(_texts.CalibManualRadius);
        _k2Box = BuildTextBox(_texts.CalibManualK2);
        _rotBox = BuildTextBox(_texts.CalibManualRotation);
        _cxProblem = AddField(stack, _texts.CalibManualCenterX, _cxBox);
        _cyProblem = AddField(stack, _texts.CalibManualCenterY, _cyBox);
        _radiusProblem = AddField(stack, _texts.CalibManualRadius, _radiusBox);
        _k2Problem = AddField(stack, _texts.CalibManualK2, _k2Box);
        _rotProblem = AddField(stack, _texts.CalibManualRotation, _rotBox);

        _mirrorBox = new CheckBox { Content = _texts.CalibManualMirrored, Margin = new Thickness(0, 8, 0, 0) };
        _mirrorBox.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Brush.Text");
        _mirrorBox.Checked += (_, _) => OnManualEdited();
        _mirrorBox.Unchecked += (_, _) => OnManualEdited();
        stack.Children.Add(_mirrorBox);

        _manualInvalidText = BuildBodyText(string.Empty, "Brush.Error", "Size.Body");
        _manualInvalidText.Visibility = Visibility.Collapsed;
        _manualPreviewText = BuildBodyText(string.Empty, "Brush.Accent", "Size.Caption");
        _manualPreviewText.Visibility = Visibility.Collapsed;
        stack.Children.Add(_manualPreviewText);
        stack.Children.Add(_manualInvalidText);

        _manualUseButton = BuildButton(_texts.CalibManualUse, primary: true);
        _manualUseButton.Click += OnManualUseClick;
        _manualResetButton = BuildButton(_texts.CalibManualReset, primary: false);
        _manualResetButton.Click += OnManualResetClick;
        _manualResetButton.Margin = new Thickness(8, 4, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_manualUseButton);
        buttons.Children.Add(_manualResetButton);
        stack.Children.Add(buttons);

        return stack;
    }

    // Etichetta, casella e riga del problema di un campo. Restituisce il blocco del problema.
    private TextBlock AddField(StackPanel stack, string label, TextBox box)
    {
        stack.Children.Add(BuildLabel(label));
        stack.Children.Add(box);
        var problem = BuildBodyText(string.Empty, "Brush.Error", "Size.Caption");
        problem.Visibility = Visibility.Collapsed;
        stack.Children.Add(problem);
        return problem;
    }

    private TextBox BuildTextBox(string accessibleName)
    {
        var box = new TextBox { Width = FieldWidthPx, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(box, accessibleName);
        box.TextChanged += (_, _) => OnManualEdited();
        return box;
    }

    private TextBlock BuildPanelTitle(string text)
    {
        var title = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 8) };
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
