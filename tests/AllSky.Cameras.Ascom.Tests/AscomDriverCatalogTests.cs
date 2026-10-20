using Xunit;

namespace AllSky.Cameras.Ascom.Tests;

public class AscomDriverCatalogTests
{
    [Fact]
    public void Discover_UneLeVisteEDeduplicaPerProgId()
    {
        var registry = new FakeRegistryReader();
        registry.Views[RegistryViewKind.Native] =
        [
            new RegistrySubKey("ASCOM.Sim.Camera", "Simulatore"),
            new RegistrySubKey("ASCOM.Foo.Camera", null),
        ];
        registry.Views[RegistryViewKind.Wow6432] =
        [
            new RegistrySubKey("ascom.foo.camera", "Foo a 32 bit"),
            new RegistrySubKey("ASCOM.Bar.Camera", "  Bar  "),
            new RegistrySubKey("   ", "vuota"),
        ];

        var catalog = new AscomDriverCatalog(registry);
        IReadOnlyList<AscomDriverInfo> drivers = catalog.Discover();

        Assert.Equal(
            new[]
            {
                new AscomDriverInfo("ASCOM.Bar.Camera", "Bar"),
                new AscomDriverInfo("ASCOM.Foo.Camera", "Foo a 32 bit"),
                new AscomDriverInfo("ASCOM.Sim.Camera", "Simulatore"),
            },
            drivers.ToArray());
        Assert.Equal(
            new[]
            {
                (RegistryViewKind.Native, AscomDriverCatalog.CameraDriversKeyPath),
                (RegistryViewKind.Wow6432, AscomDriverCatalog.CameraDriversKeyPath),
            },
            registry.Queries.ToArray());
    }

    [Fact]
    public void Discover_DescrizioneAssenteUsaIlProgId()
    {
        var registry = new FakeRegistryReader();
        registry.Views[RegistryViewKind.Native] = [new RegistrySubKey("ASCOM.Solo.Camera", null)];

        IReadOnlyList<AscomDriverInfo> drivers = new AscomDriverCatalog(registry).Discover();

        AscomDriverInfo driver = Assert.Single(drivers);
        Assert.Equal("ASCOM.Solo.Camera", driver.Description);
    }

    [Fact]
    public void Discover_VistaNonLeggibile_VieneSaltata()
    {
        var registry = new FakeRegistryReader();
        registry.Views[RegistryViewKind.Native] = [new RegistrySubKey("ASCOM.Sim.Camera", "Simulatore")];
        registry.Failures[RegistryViewKind.Wow6432] = new UnauthorizedAccessException("negato");

        IReadOnlyList<AscomDriverInfo> drivers = new AscomDriverCatalog(registry).Discover();

        AscomDriverInfo driver = Assert.Single(drivers);
        Assert.Equal("ASCOM.Sim.Camera", driver.ProgId);
    }

    [Fact]
    public void Discover_ChiaveMancante_RestituisceListaVuota()
    {
        var registry = new FakeRegistryReader();

        IReadOnlyList<AscomDriverInfo> drivers = new AscomDriverCatalog(registry).Discover();

        Assert.Empty(drivers);
    }

    [Fact]
    public void Discover_SenzaLettore_SuNonWindowsRestituisceVuoto()
    {
        // Sul registro reale il risultato dipende dalla macchina: il test vale solo fuori da Windows.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        IReadOnlyList<AscomDriverInfo> drivers = new AscomDriverCatalog().Discover();

        Assert.Empty(drivers);
    }
}
