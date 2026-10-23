using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AllSky.Core;
using AllSky.Desktop.Views.Live;
using AllSky.Nina;
using Xunit;

namespace AllSky.Desktop.Tests;

public sealed partial class DesktopUiTests
{
    /// <summary>Nome del pannello NINA nei log.</summary>
    internal const string NinaPanelName = "Pannello NINA";

    /// <summary>Riquadro di render del pannello NINA: il pannello sta in alto a sinistra, a larghezza fissa.</summary>
    internal const int NinaShotWidth = 640;
    internal const int NinaShotHeight = 420;

    /// <summary>Istante fisso dello stato sintetico, così i PNG non dipendono dall'orologio.</summary>
    internal static readonly DateTimeOffset NinaSyntheticReadAtUtc = new(2026, 10, 23, 22, 0, 0, TimeSpan.Zero);

    /// <summary>Modalità del pannello NINA: normale e notte rossa.</summary>
    public static IEnumerable<object[]> NinaPanelCases()
    {
        foreach (bool nightRed in new[] { false, true })
        {
            yield return new object[] { nightRed };
        }
    }

    [Theory]
    [MemberData(nameof(NinaPanelCases))]
    public void Pannello_nina_con_stato_sintetico_misurato_e_disegnato(bool nightRed)
    {
        try
        {
            string mode = ModeLabel(nightRed);
            string title = $"{NinaPanelName} | {mode} {NinaShotWidth}x{NinaShotHeight}";
            string stem = nightRed ? "nina-status-nightred" : "nina-status-normal";
            CaseOutcome outcome = StaRunner.Run(() => RenderNinaPanel(_fixture, stem, nightRed, SyntheticNinaState(), SyntheticNinaTexts(_fixture.Texts)));
            Report(NinaPanelName, mode, title, outcome);
            Assert.True(outcome.Passed, outcome.Detail);
        }
        catch (Exception ex)
        {
            CiReport.Fault($"{nameof(Pannello_nina_con_stato_sintetico_misurato_e_disegnato)} [{ModeLabel(nightRed)}]", ex, _output);
            throw;
        }
    }

    [Theory]
    [MemberData(nameof(NinaPanelCases))]
    public void Pannello_nina_senza_stato_non_crasha_e_mostra_non_attivo(bool nightRed)
    {
        try
        {
            string mode = ModeLabel(nightRed);
            string title = $"{NinaPanelName} senza stato | {mode} {NinaShotWidth}x{NinaShotHeight}";
            string stem = nightRed ? "nina-status-inattivo-nightred" : "nina-status-inattivo-normal";
            string[] expected = [_fixture.Texts.NinaPanelTitle, _fixture.Texts.NinaOff];
            CaseOutcome outcome = StaRunner.Run(() => RenderNinaPanel(_fixture, stem, nightRed, null, expected));
            Report(NinaPanelName, mode, title, outcome);
            Assert.True(outcome.Passed, outcome.Detail);
        }
        catch (Exception ex)
        {
            CiReport.Fault($"{nameof(Pannello_nina_senza_stato_non_crasha_e_mostra_non_attivo)} [{ModeLabel(nightRed)}]", ex, _output);
            throw;
        }
    }

    /// <summary>
    /// Stato NINA di esempio: connesso, sequenza in corso su M31, mount in inseguimento e non parcheggiato.
    /// NinaState non ha campi per guida e meridian flip: il pannello non li mostra, quindi non compaiono qui.
    /// </summary>
    internal static NinaState SyntheticNinaState() => new(
        Connected: true,
        ReadAtUtc: NinaSyntheticReadAtUtc,
        Mount: new NinaMountState(AtPark: false, Slewing: false, TrackingMode: "Sidereal", RaDeg: 10.68, DecDeg: 41.27),
        Target: new NinaTargetState("M31 Andromeda", 10.68, 41.27),
        SequenceState: "Running",
        Error: null);

    /// <summary>Testi attesi con lo stato sintetico, presi dalla stessa lingua del pannello.</summary>
    private static string[] SyntheticNinaTexts(DesktopTexts texts) =>
    [
        texts.NinaPanelTitle,
        texts.NinaSubjectLabel,
        "M31 Andromeda",
        texts.NinaParkLabel,
        texts.NinaParkTracking,
        texts.NinaConnectionLabel,
        texts.NinaConnected,
    ];

    /// <summary>
    /// Costruisce il pannello con un servizio fake e lo disegna in un riquadro fisso. Controlla i testi attesi e i pixel
    /// (Judge). Gira sul thread STA.
    /// </summary>
    private static CaseOutcome RenderNinaPanel(DesktopFixture fixture, string stem, bool nightRed, NinaState? state, IReadOnlyList<string> expectedTexts)
    {
        string stage = "impostazione del tema";
        try
        {
            fixture.Manager.SetNightMode(nightRed);

            stage = "costruzione del pannello";
            var panel = new NinaStatusPanel(new NinaStatusPanelTests.FakeNinaService(state), fixture.Texts)
            {
                Width = 400,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(24),
            };

            var root = new Grid();
            root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Brush.Background");
            root.Children.Add(panel);

            stage = "misura, disposizione e disegno";
            List<UIElement> hidden = nightRed ? RenderChecks.HideImages(root) : new List<UIElement>();
            RenderTargetBitmap bitmap;
            try
            {
                bitmap = RenderChecks.RenderOffscreen(root, NinaShotWidth, NinaShotHeight);
            }
            finally
            {
                RenderChecks.Restore(hidden);
            }

            stage = "controllo dei testi";
            List<string> texts = NinaStatusPanelTests.TextsOf(panel);
            List<string> missing = expectedTexts.Where(expected => !texts.Contains(expected)).ToList();

            stage = "analisi dei pixel";
            CaseOutcome outcome = Judge(fixture, stem, root, bitmap, nightRed);
            if (missing.Count > 0)
            {
                return new CaseOutcome(false, "FALLITO: testi attesi mancanti: " + string.Join("; ", missing) + ". " + outcome.Detail, outcome.Ascii);
            }

            return outcome;
        }
        catch (Exception ex)
        {
            return CaseOutcome.Failure(stage, ex, string.Empty);
        }
        finally
        {
            fixture.Manager.SetNightMode(false);
        }
    }
}
