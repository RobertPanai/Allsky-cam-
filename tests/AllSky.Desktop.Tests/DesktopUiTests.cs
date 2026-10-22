using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AllSky.Core.Design;
using AllSky.Desktop.Theme;
using Xunit;
using Xunit.Abstractions;

namespace AllSky.Desktop.Tests;

/// <summary>
/// Test dell'interfaccia WPF senza avviare l'applicazione: risorse del tema, ogni vista, finestra principale e riepilogo.
/// Tutti i casi girano sul thread STA di <see cref="StaRunner"/> e non mostrano nessuna finestra.
/// </summary>
public sealed partial class DesktopUiTests : IClassFixture<DesktopFixture>
{
    private static readonly string[] SizeKeys =
    [
        "Size.Caption", "Size.Body", "Size.Subtitle", "Size.Title", "Size.Display", "Size.Hero",
    ];

    private static readonly string[] SpaceKeys =
    [
        "Space.1", "Space.2", "Space.3", "Space.4", "Space.5", "Space.6", "Space.7",
    ];

    private static readonly string[] StyleKeys =
    [
        "Radius.Small", "Radius.Control", "Radius.Panel",
        "Inset.Button", "Inset.Field", "Inset.Tip", "Inset.Panel",
        "Focus.Ring", "Panel", "PanelTitle",
        "Button.Secondary", "Button.Primary", "Button.Danger",
        "TextBox.Template", "ToolTip.Template",
    ];

    private readonly DesktopFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DesktopUiTests(DesktopFixture fixture, ITestOutputHelper output)
    {
        try
        {
            _fixture = fixture;
            _output = output;
        }
        catch (Exception ex)
        {
            CiReport.Fault("DesktopUiTests (costruttore)", ex, output);
            throw;
        }
    }

    /// <summary>Risoluzioni dei rendering: laptop e schermo full HD.</summary>
    internal static readonly (int Width, int Height)[] Resolutions = [(1280, 800), (1920, 1080)];

    /// <summary>Etichetta della modalità nei titoli e nei log.</summary>
    internal static string ModeLabel(bool nightRed) => nightRed ? "notte rossa" : "normale";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Risorse_tema_e_stili_caricati(bool nightRed)
    {
        try
        {
            string mode = ModeLabel(nightRed);
            CaseOutcome outcome = StaRunner.Run(() => CheckResources(nightRed));
            Report("Risorse tema", mode, $"Risorse tema | {mode}", outcome);
            Assert.True(outcome.Passed, outcome.Detail);
        }
        catch (Exception ex)
        {
            CiReport.Fault($"{nameof(Risorse_tema_e_stili_caricati)} [{ModeLabel(nightRed)}]", ex, _output);
            throw;
        }
    }

    /// <summary>
    /// Verifica il tema in un <see cref="ResourceDictionary"/> nuovo con <c>Styles.xaml</c> unito, come fa l'applicazione.
    /// Gira sul thread STA.
    /// </summary>
    private static CaseOutcome CheckResources(bool nightRed)
    {
        var problems = new List<string>();
        int brushes = 0;
        int colors = 0;
        int sizes = 0;
        int spaces = 0;
        int styles = 0;
        string stage = "caricamento del tema";
        try
        {
            var dictionary = new ResourceDictionary();
            var manager = new ThemeManager(dictionary);
            dictionary.MergedDictionaries.Add(DesktopFixture.LoadStyles());
            manager.SetNightMode(nightRed);

            stage = "pennelli e colori";
            IReadOnlyList<KeyValuePair<string, string>> roles = (nightRed ? Palette.NightRed : Palette.Dark).Entries();
            foreach (KeyValuePair<string, string> role in roles)
            {
                string brushKey = ThemeManager.BrushPrefix + role.Key;
                if (Lookup(dictionary, brushKey) is not SolidColorBrush brush)
                {
                    problems.Add($"{brushKey}: manca o non è un SolidColorBrush");
                }
                else
                {
                    brushes++;
                    (byte r, byte g, byte b) = Palette.ParseHex(role.Value);
                    Color color = brush.Color;
                    if (color.R != r || color.G != g || color.B != b)
                    {
                        problems.Add($"{brushKey}: colore {color} diverso da {role.Value}");
                    }

                    if (nightRed && (color.G > RenderChecks.NightTolerance || color.B > RenderChecks.NightTolerance))
                    {
                        problems.Add($"{brushKey}: verde o blu nella notte rossa ({color})");
                    }
                }

                if (Lookup(dictionary, ThemeManager.ColorPrefix + role.Key) is Color)
                {
                    colors++;
                }
                else
                {
                    problems.Add($"{ThemeManager.ColorPrefix + role.Key}: manca");
                }
            }

            stage = "font, dimensioni e spaziature";
            foreach (string fontKey in new[] { ThemeManager.UiFontKey, ThemeManager.MonoFontKey })
            {
                if (Lookup(dictionary, fontKey) is not FontFamily)
                {
                    problems.Add($"{fontKey}: manca o non è un FontFamily");
                }
            }

            foreach (string key in SizeKeys)
            {
                if (Lookup(dictionary, key) is double) sizes++;
                else problems.Add($"{key}: manca o non è un double");
            }

            foreach (string key in SpaceKeys)
            {
                if (Lookup(dictionary, key) is double) spaces++;
                else problems.Add($"{key}: manca o non è un double");
            }

            stage = "stili";
            foreach (string key in StyleKeys)
            {
                if (Lookup(dictionary, key) is not null) styles++;
                else problems.Add($"{key}: manca");
            }
        }
        catch (Exception ex)
        {
            return CaseOutcome.Failure(stage, ex, string.Empty);
        }

        if (problems.Count > 0)
        {
            return new CaseOutcome(false, $"FALLITO: {problems.Count} problemi: " + string.Join("; ", problems), string.Empty);
        }

        return new CaseOutcome(true,
            $"OK: {brushes} pennelli, {colors} colori, 2 font, {sizes} dimensioni, {spaces} spaziature, {styles} stili.",
            string.Empty);
    }

    /// <summary>Cerca una chiave nel dizionario e nei dizionari uniti, che l'indicizzatore non esplora.</summary>
    private static object? Lookup(ResourceDictionary dictionary, string key)
    {
        if (dictionary.Contains(key))
        {
            return dictionary[key];
        }

        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            object? found = Lookup(merged, key);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Pubblica l'esito come notice (o error) nel log del runner e su xunit, e lo registra per il riepilogo.</summary>
    private void Report(string view, string mode, string title, CaseOutcome outcome)
    {
        string text = string.IsNullOrEmpty(outcome.Ascii) ? outcome.Detail : outcome.Detail + "\n" + outcome.Ascii;
        if (outcome.Passed)
        {
            CiReport.Notice(title, text, _output);
        }
        else
        {
            CiReport.Error(title, text, _output);
        }

        _fixture.Record(new CaseRecord(view, mode, title, outcome.Passed, outcome.Detail));
    }

    /// <summary>Esito di un caso: se è passato, il dettaglio, e l'anteprima ASCII del bitmap quando esiste.</summary>
    internal sealed record CaseOutcome(bool Passed, string Detail, string Ascii)
    {
        /// <summary>Esito fallito per un'eccezione in una fase, con le prime righe dello stack.</summary>
        public static CaseOutcome Failure(string stage, Exception ex, string ascii) =>
            new(false, $"FALLITO in fase '{stage}': {ex.GetType().Name}: {ex.Message}\n{ReducedStack(ex)}", ascii);

        private static string ReducedStack(Exception ex) => string.Join(
            "\n",
            (ex.StackTrace ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(6));
    }
}
