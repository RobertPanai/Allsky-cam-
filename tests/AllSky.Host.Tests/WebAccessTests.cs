using System.Text;
using AllSky.Core;
using AllSky.Core.Settings;
using AllSky.Host;
using Xunit;

namespace AllSky.Host.Tests;

/// <summary>Interfaccia web opzionale: spenta di default, nessuna porta HTTP se non richiesta.</summary>
public sealed class WebAccessTests : IDisposable
{
    private readonly HostFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Senza_ui_webEnabled_l_host_non_apre_alcuna_porta_http()
    {
        // Configurazione senza la chiave: il predefinito e' spento.
        new ConfigStore().Save(_fixture.ConfigPath, new AppConfig
        {
            Camera = new AppConfig.CameraConfig { Id = string.Empty },
            Storage = new AppConfig.StorageConfig { Folder = _fixture.DataFolder },
        });

        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        Assert.False(runtime.WebRunning);
        Assert.Null(runtime.Url);
        Assert.NotNull(runtime.Service);
    }

    [Fact]
    public async Task Con_ui_webEnabled_l_host_ascolta_su_loopback()
    {
        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        Assert.True(runtime.WebRunning);
        Assert.StartsWith("http://127.0.0.1:", runtime.Url);
    }

    [Fact]
    public async Task Senza_interfaccia_web_la_richiesta_di_rete_locale_senza_password_non_attiva_il_server()
    {
        new ConfigStore().Save(_fixture.ConfigPath, new AppConfig
        {
            Camera = new AppConfig.CameraConfig { Id = string.Empty },
            Storage = new AppConfig.StorageConfig { Folder = _fixture.DataFolder },
            Ui = new AppConfig.UiConfig { WebEnabled = false, AllowLan = true },
        });

        await using AllSkyHostRuntime runtime = await AllSkyHost.StartCoreAsync(_fixture.Options(0), HostFixture.Tuning, default);

        Assert.False(runtime.WebRunning);
        Assert.Null(runtime.Url);
    }

    [Fact]
    public void LanAccess_accetta_solo_la_password_giusta()
    {
        string hash = LanPassword.Hash("parola-segreta");
        var guard = new LanAccess();

        string good = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("robert:parola-segreta"));
        string wrong = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("robert:altra-parola"));

        Assert.True(guard.IsAuthorized(good, hash));
        Assert.True(guard.IsAuthorized(good, hash));
        Assert.False(guard.IsAuthorized(wrong, hash));
        Assert.False(guard.IsAuthorized(null, hash));
        Assert.False(guard.IsAuthorized(good, string.Empty));
    }
}
