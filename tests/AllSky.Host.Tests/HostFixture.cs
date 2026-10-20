using AllSky.Core;
using AllSky.Host;

namespace AllSky.Host.Tests;

/// <summary>
/// Cartella temporanea con configurazione, cartella dati e una UI di prova. La camera e' vuota in configurazione:
/// l'Host si avvia senza catturare, cosi' l'archivio resta vuoto e i test sono deterministici.
/// </summary>
internal sealed class HostFixture : IDisposable
{
    public HostFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "allsky-host-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(WebRoot);

        File.WriteAllText(Path.Combine(WebRoot, "index.html"), "<!doctype html><title>AllSky test</title>");
        File.WriteAllText(Path.Combine(WebRoot, "app.js"), "console.log('test');");

        var config = new AppConfig
        {
            Camera = new AppConfig.CameraConfig { Id = string.Empty },
            Storage = new AppConfig.StorageConfig { Folder = DataFolder },
        };
        new ConfigStore().Save(ConfigPath, config);
    }

    /// <summary>Cartella radice temporanea di questo test.</summary>
    public string Root { get; }

    /// <summary>Cartella dati (archivio in nights, log in logs).</summary>
    public string DataFolder => Path.Combine(Root, "data");

    /// <summary>File di configurazione.</summary>
    public string ConfigPath => Path.Combine(Root, "config.json");

    /// <summary>Cartella dei file statici della UI di prova.</summary>
    public string WebRoot => Path.Combine(Root, "web");

    /// <summary>Opzioni di avvio con la porta indicata (0 = libera).</summary>
    public HostOptions Options(int? port = 0)
    {
        return new HostOptions(ConfigPath, DataFolder, WebRoot, port);
    }

    /// <summary>Parametri di test: nessuna ricerca Alpaca sulla rete.</summary>
    public static HostTuning Tuning { get; } = new(TimeSpan.FromMilliseconds(100), EnableAlpacaDiscovery: false);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cartella temporanea del sistema: un file ancora aperto non fa fallire il test.
        }
    }
}
