using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using AllSky.Core;
using AllSky.Core.Design;
using AllSky.Desktop.Theme;
using AllSky.Engine;
using AllSky.Host;

namespace AllSky.Desktop.Tests;

/// <summary>
/// Ambiente condiviso dei test di interfaccia: un runtime reale (camera simulata, cartella dati temporanea, nessuna
/// interfaccia web) e una <see cref="App"/> con il tema caricato, creati sul thread STA di <see cref="StaRunner"/>.
/// </summary>
/// <remarks>
/// Il runtime non passa da Kestrel (<c>ui.webEnabled = false</c>) e la ricerca Alpaca non parte all'avvio.
/// La <see cref="App"/> viene creata senza <c>OnStartup</c>: niente splash, niente icona nell'area di notifica,
/// niente finestra visibile. Il tema si collega come in <c>App.OnStartup</c>.
/// </remarks>
public sealed class DesktopFixture : IDisposable
{
    private readonly string _root;
    private readonly List<CaseRecord> _records = new();
    private readonly object _recordGate = new();

    /// <summary>Crea l'ambiente. Può richiedere alcuni secondi: avvia il runtime dell'Host.</summary>
    public DesktopFixture()
    {
        try
        {
            _root = Path.Combine(Path.GetTempPath(), "allsky-desktop-tests-" + Guid.NewGuid().ToString("N"));
            string dataFolder = Path.Combine(_root, "data");
            string webRoot = Path.Combine(_root, "web");
            string configPath = Path.Combine(_root, "config.json");
            Directory.CreateDirectory(dataFolder);
            Directory.CreateDirectory(webRoot);
            File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><title>AllSky test</title>");

            new ConfigStore().Save(configPath, new AppConfig
            {
                Camera = new AppConfig.CameraConfig { Id = "sim-1" },
                Storage = new AppConfig.StorageConfig { Folder = dataFolder },
                Ui = new AppConfig.UiConfig { WebEnabled = false },
            });

            Paths = new AppPaths(dataFolder, configPath, webRoot);
            ShotsFolder = Path.Combine(AppContext.BaseDirectory, "TestResults", "shots");
            Directory.CreateDirectory(ShotsFolder);

            using var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Runtime = AllSkyHost.StartAsync(new HostOptions(configPath, dataFolder, webRoot), startCts.Token)
                .GetAwaiter().GetResult();

            (WpfApp, Manager) = StaRunner.Run(() =>
            {
                var app = new App(Runtime, Paths, DesktopTexts.Italian);
                var manager = new ThemeManager(app.Resources);
                // Stessa sequenza di App.OnStartup: il tema per primo, poi gli stili, poi la modalità salvata (qui normale).
                typeof(App).GetProperty(nameof(App.Theme), BindingFlags.Public | BindingFlags.Instance)!
                    .GetSetMethod(nonPublic: true)!
                    .Invoke(app, new object[] { manager });
                app.Resources.MergedDictionaries.Add(LoadStyles());
                return (app, manager);
            });
        }
        catch (Exception ex)
        {
            CiReport.Fault("DesktopFixture (costruttore)", ex, output: null);
            throw;
        }
    }

    /// <summary>Percorsi dell'app nella cartella temporanea.</summary>
    public AppPaths Paths { get; }

    /// <summary>Runtime reale dell'Host, con camera simulata.</summary>
    public AllSkyHostRuntime Runtime { get; }

    /// <summary>Applicazione WPF creata sul thread STA. Va usata solo tramite <see cref="StaRunner"/>.</summary>
    public App WpfApp { get; }

    /// <summary>Gestore del tema dell'applicazione (modalità notte rossa).</summary>
    public ThemeManager Manager { get; }

    /// <summary>Servizio applicativo del runtime.</summary>
    public IAllSkyService Service => Runtime.Service;

    /// <summary>Testi in italiano, la lingua predefinita.</summary>
    public DesktopTexts Texts => DesktopTexts.Italian;

    /// <summary>Cartella dei PNG prodotti dai test (dentro la cartella di output del test).</summary>
    public string ShotsFolder { get; }

    /// <summary>Carica <c>Theme/Styles.xaml</c> in un nuovo dizionario, come fa l'applicazione.</summary>
    public static ResourceDictionary LoadStyles() =>
        new() { Source = new Uri("/AllSkyBrain;component/Theme/Styles.xaml", UriKind.Relative) };

    /// <summary>Registra l'esito di un caso, per il riepilogo finale.</summary>
    public void Record(CaseRecord record)
    {
        lock (_recordGate)
        {
            _records.Add(record);
        }
    }

    /// <summary>Copia dei casi registrati finora, nell'ordine di esecuzione.</summary>
    public IReadOnlyList<CaseRecord> Snapshot()
    {
        lock (_recordGate)
        {
            return _records.ToArray();
        }
    }

    /// <summary>Arresta il runtime e cancella la cartella temporanea. Gli errori di pulizia non fanno fallire i test.</summary>
    public void Dispose()
    {
        try
        {
            Task.Run(async () =>
            {
                await Runtime.StopAsync().ConfigureAwait(false);
                await Runtime.DisposeAsync().ConfigureAwait(false);
            }).Wait(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            // Arresto non completato: la cartella temporanea viene comunque tolta sotto, se possibile.
            CiReport.Fault("DesktopFixture (Dispose, arresto del runtime)", ex, output: null);
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un file ancora aperto dal motore non fa fallire il test.
        }
        catch (Exception ex)
        {
            CiReport.Fault("DesktopFixture (Dispose)", ex, output: null);
        }
    }
}

/// <summary>Esito di un caso (vista, modalità, risoluzione) per il riepilogo.</summary>
public sealed record CaseRecord(string View, string Mode, string Title, bool Passed, string Detail);
