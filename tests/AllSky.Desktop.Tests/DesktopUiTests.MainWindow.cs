using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AllSky.Core;
using Xunit;

namespace AllSky.Desktop.Tests;

public sealed partial class DesktopUiTests
{
    /// <summary>Nome della finestra principale nei log.</summary>
    internal const string MainWindowName = "Finestra principale";

    public static IEnumerable<object[]> MainWindowCases()
    {
        foreach (bool nightRed in new[] { false, true })
        {
            foreach ((int width, int height) in Resolutions)
            {
                yield return new object[] { nightRed, width, height };
            }
        }
    }

    [Theory]
    [MemberData(nameof(MainWindowCases))]
    public void Finestra_principale_misurata_e_disegnata(bool nightRed, int width, int height)
    {
        string mode = ModeLabel(nightRed);
        string title = $"{MainWindowName} | {mode} {width}x{height}";
        CaseOutcome outcome = StaRunner.Run(() => RenderMainWindow(_fixture, nightRed, width, height));
        Report(MainWindowName, mode, title, outcome);
        Assert.True(outcome.Passed, outcome.Detail);
    }

    /// <summary>
    /// Crea la <see cref="MainWindow"/> completa (shell, viste, barra di stato) senza mostrarla: solo Measure, Arrange e render.
    /// Gira sul thread STA.
    /// </summary>
    private static CaseOutcome RenderMainWindow(DesktopFixture fixture, bool nightRed, int width, int height)
    {
        string stage = "impostazione del tema";
        MainWindow? window = null;
        try
        {
            fixture.Manager.SetNightMode(nightRed);

            stage = "costruzione della finestra";
            window = new MainWindow(fixture.Service, fixture.Texts, fixture.Runtime, fixture.Paths);
            window.Width = width;
            window.Height = height;

            stage = "misura e disposizione";
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));
            window.UpdateLayout();

            if (window.Content is not Grid shell)
            {
                return new CaseOutcome(false, "FALLITO: il contenuto della finestra non è la shell attesa.", string.Empty);
            }

            stage = "disegno";
            shell.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Brush.Background");
            List<UIElement> hidden = nightRed ? RenderChecks.HideImages(shell) : new List<UIElement>();
            RenderTargetBitmap bitmap;
            try
            {
                bitmap = RenderChecks.RenderOffscreen(shell, width, height);
            }
            finally
            {
                RenderChecks.Restore(hidden);
            }

            stage = "analisi dei pixel";
            return Judge(fixture, $"finestra-principale-{ShotMode(nightRed)}-{width}x{height}", shell, bitmap, nightRed);
        }
        catch (Exception ex)
        {
            return CaseOutcome.Failure(stage, ex, string.Empty);
        }
        finally
        {
            // Smette di ascoltare il servizio: la finestra non verrà mai mostrata né chiusa dall'utente.
            window?.PrepareForExit();
            fixture.Manager.SetNightMode(false);
        }
    }
}
