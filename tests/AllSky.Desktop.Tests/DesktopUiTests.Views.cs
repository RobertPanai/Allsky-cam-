using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AllSky.Core;
using AllSky.Core.Calibration;
using AllSky.Desktop.Theme;
using AllSky.Desktop.Views.Camera;
using AllSky.Desktop.Views.Events;
using AllSky.Desktop.Views.Live;
using AllSky.Desktop.Views.Log;
using AllSky.Desktop.Views.Mask;
using AllSky.Desktop.Views.Nights;
using AllSky.Desktop.Views.Publication;
using AllSky.Desktop.Views.Settings;
using AllSky.Desktop.Views.Tonight;
using AllSky.Engine;
using Xunit;

namespace AllSky.Desktop.Tests;

public sealed partial class DesktopUiTests
{
    /// <summary>Le viste da controllare, con il nome mostrato nei log.</summary>
    internal static readonly string[] ViewNames =
    [
        "Stanotte", "Live", "Camera", "Notti", "Eventi", "Pubblicazione", "Impostazioni", "Log", "Editor maschera", "Calibrazione",
    ];

    /// <summary>Casi di vista: ogni vista, in entrambe le modalità, a entrambe le risoluzioni.</summary>
    public static IEnumerable<object[]> ViewCases()
    {
        foreach (string view in ViewNames)
        {
            foreach (bool nightRed in new[] { false, true })
            {
                foreach ((int width, int height) in Resolutions)
                {
                    yield return new object[] { view, nightRed, width, height };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(ViewCases))]
    public void Vista_costruita_misurata_e_disegnata(string view, bool nightRed, int width, int height)
    {
        string mode = ModeLabel(nightRed);
        string title = $"{view} | {mode} {width}x{height}";
        CaseOutcome outcome = StaRunner.Run(() => RenderView(_fixture, view, nightRed, width, height));
        Report(view, mode, title, outcome);
        Assert.True(outcome.Passed, outcome.Detail);
    }

    /// <summary>Costruisce la vista con il runtime reale, la dispone e la disegna. Gira sul thread STA.</summary>
    private static CaseOutcome RenderView(DesktopFixture fixture, string view, bool nightRed, int width, int height)
    {
        string stage = "impostazione del tema";
        string ascii = string.Empty;
        try
        {
            fixture.Manager.SetNightMode(nightRed);

            stage = "costruzione";
            FrameworkElement content = CreateView(fixture, view);

            var root = new Grid();
            root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Brush.Background");
            root.Children.Add(content);

            stage = "misura, disposizione e disegno";
            List<UIElement> hidden = nightRed ? RenderChecks.HideImages(root) : new List<UIElement>();
            RenderTargetBitmap bitmap;
            try
            {
                bitmap = RenderChecks.RenderOffscreen(root, width, height);
            }
            finally
            {
                RenderChecks.Restore(hidden);
            }

            stage = "analisi dei pixel";
            return Judge(fixture, $"{Slug(view)}-{ShotMode(nightRed)}-{width}x{height}", root, bitmap, nightRed);
        }
        catch (Exception ex)
        {
            return CaseOutcome.Failure(stage, ex, ascii);
        }
        finally
        {
            fixture.Manager.SetNightMode(false);
        }
    }

    /// <summary>Crea la vista con i costruttori reali e i parametri del runtime di test.</summary>
    private static FrameworkElement CreateView(DesktopFixture fixture, string view) => view switch
    {
        "Stanotte" => new TonightView(fixture.Service, fixture.Texts),
        "Live" => new LiveView(fixture.Service, fixture.Texts),
        "Camera" => new CameraView(fixture.Service, fixture.Texts),
        "Notti" => new NightsView(fixture.Service, fixture.Texts),
        "Eventi" => new EventsView(fixture.Runtime.Events, fixture.Texts),
        "Pubblicazione" => new PublicationView(fixture.Runtime.PublishSettings, fixture.Texts, fixture.Runtime.YouTube),
        "Impostazioni" => new SettingsView(fixture.Runtime, fixture.Paths, fixture.Texts, fixture.Manager),
        "Log" => new LogView(fixture.Paths, fixture.Texts),
        "Editor maschera" => new MaskEditorView(fixture.Service, fixture.Paths, fixture.Texts),
        "Calibrazione" => new CalibrationPanel(fixture.Service, RequireCalibration(fixture.Service), fixture.Texts),
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Vista sconosciuta."),
    };

    private static ICalibrationService RequireCalibration(IAllSkyService service) =>
        service as ICalibrationService
        ?? throw new InvalidOperationException("Il servizio dell'Engine non implementa ICalibrationService.");

    /// <summary>
    /// Controlli comuni a viste e finestra: dimensioni valide, bitmap non vuoto, colore solo rosso in notte, PNG salvato.
    /// Il PNG non fa fallire il caso se non si può scrivere.
    /// </summary>
    internal static CaseOutcome Judge(DesktopFixture fixture, string fileStem, DependencyObject measured, RenderTargetBitmap bitmap, bool nightRed)
    {
        List<string> invalid = RenderChecks.InvalidDimensions(measured);
        PixelAnalysis analysis = RenderChecks.Analyze(bitmap, nightRed);
        string png = TrySavePng(fixture.ShotsFolder, fileStem, bitmap);

        var problems = new List<string>();
        if (invalid.Count > 0)
        {
            problems.Add("dimensioni non valide: " + string.Join("; ", invalid));
        }

        if (analysis.ContentPixels < RenderChecks.MinContentPixels)
        {
            problems.Add($"bitmap vuoto: solo {analysis.ContentPixels} pixel diversi dallo sfondo");
        }

        string? tint = null;
        if (nightRed && analysis.TintViolationRatio > RenderChecks.NightViolationBudget)
        {
            problems.Add(
                $"colore fuori dal rosso: {analysis.TintViolations} pixel con verde o blu oltre {RenderChecks.NightTolerance} " +
                $"({analysis.TintViolationRatio:P3} del bitmap)");
            tint = SafeDescribeTint(bitmap, measured);
        }

        string head = problems.Count == 0 ? "OK" : "FALLITO";
        string detail = $"{head}: {analysis.ContentPixels} pixel di contenuto; PNG {png}";
        if (problems.Count > 0)
        {
            detail += ". " + string.Join(". ", problems);
        }

        if (tint is not null)
        {
            detail += "\n" + tint;
        }

        return new CaseOutcome(problems.Count == 0, detail, analysis.Ascii);
    }

    /// <summary>
    /// Diagnostica dei pixel fuori dal rosso. Gira solo dopo un fallimento: se qualcosa va storto qui, il messaggio
    /// resta quello del controllo e l'errore della diagnostica ne occupa una riga.
    /// </summary>
    private static string SafeDescribeTint(RenderTargetBitmap bitmap, DependencyObject measured)
    {
        try
        {
            return RenderChecks.DescribeTintViolations(bitmap, measured);
        }
        catch (Exception ex)
        {
            return "Diagnostica fuori dal rosso non disponibile: " + ex.GetType().Name + " " + ex.Message;
        }
    }

    /// <summary>Salva il PNG nella cartella dei test. Un errore di scrittura non fa fallire il caso.</summary>
    private static string TrySavePng(string folder, string stem, BitmapSource bitmap)
    {
        string path = Path.Combine(folder, stem + ".png");
        try
        {
            return RenderChecks.SavePng(bitmap, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "non salvato (" + ex.GetType().Name + ")";
        }
    }

    internal static string ShotMode(bool nightRed) => nightRed ? "notte-rossa" : "normale";

    /// <summary>Nome di file dalla vista: minuscole e spazi trasformati in trattini.</summary>
    internal static string Slug(string text) => text.ToLowerInvariant().Replace(' ', '-');
}
