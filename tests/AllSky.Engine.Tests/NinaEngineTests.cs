using AllSky.Core;
using AllSky.Engine;
using Xunit;

namespace AllSky.Engine.Tests;

/// <summary>Collegamento della sezione <c>nina</c> al motore. Nessuna connessione reale: la sorgente non e' iniettabile.</summary>
public sealed class NinaEngineTests
{
    [Fact]
    public async Task NinaSpento_LatestNinaStateENull()
    {
        using var h = new Harness();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero));
        h.SaveConfig(ConfigSenzaCamera(h.ArchiveFolder, new AppConfig.NinaConfig { Enabled = false }));

        await using var engine = new AllSkyEngine(Dipendenze(h, clock));
        await engine.StartAsync();

        Assert.Null(engine.LatestNinaState);
        await engine.StopAsync();
        Assert.Null(engine.LatestNinaState);
    }

    [Fact]
    public async Task NinaAttivoConSorgenteLog_NonAvviaNullaESegnalaNelLog()
    {
        using var h = new Harness();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 19, 15, 0, 0, TimeSpan.Zero));
        h.SaveConfig(ConfigSenzaCamera(h.ArchiveFolder, new AppConfig.NinaConfig { Enabled = true, Source = "log" }));

        await using var engine = new AllSkyEngine(Dipendenze(h, clock));
        await engine.StartAsync();

        Assert.Null(engine.LatestNinaState);
        Assert.Contains(h.Logs, m => m.Contains("'log' non è ancora supportata", StringComparison.Ordinal));
        await engine.StopAsync();
    }

    private static EngineDependencies Dipendenze(Harness h, FakeClock clock)
    {
        // Nessuna camera: Camera.Id e' vuoto, quindi la fabbrica non viene mai chiamata.
        var delay = new ScriptDelay(clock, (_, _) => Task.CompletedTask);
        return h.Dependencies(clock, new TestCameraProvider(_ => throw new InvalidOperationException("nessuna camera attesa")), delay);
    }

    private static AppConfig ConfigSenzaCamera(string folder, AppConfig.NinaConfig nina)
    {
        return new AppConfig
        {
            Camera = new AppConfig.CameraConfig { Id = string.Empty },
            Storage = new AppConfig.StorageConfig { Folder = folder },
            Nina = nina,
        };
    }
}
