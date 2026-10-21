using System.Runtime.Versioning;
using AllSky.Publish;
using Xunit;

namespace AllSky.Publish.Tests.Settings;

/// <summary>
/// DPAPI vero non e' eseguibile fuori da Windows: qui si verifica solo il comportamento non Windows.
/// La cifratura reale va provata su Windows (Protect/Unprotect di un token, poi decifratura nello stesso utente).
/// </summary>
public sealed class DpapiSecretProtectorTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void NonWindows_ProtectAndUnprotect_ThrowPlatformNotSupported()
    {
        if (OperatingSystem.IsWindows())
            return;

        var protector = new DpapiSecretProtector();

        Assert.Throws<PlatformNotSupportedException>(() => protector.Protect(new byte[] { 1, 2, 3 }));
        Assert.Throws<PlatformNotSupportedException>(() => protector.Unprotect(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void NonWindows_StoreSave_ReportsItalianMessage_AndWritesNothing()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var dir = new TempFolder();
        string path = dir.File("publish-settings.json");
        var store = new PublishSettingsStore(path, new DpapiSecretProtector());

        var ex = Assert.Throws<InvalidOperationException>(() => store.Save(SampleSettings.Valid()));

        Assert.Contains("non e' disponibile", ex.Message);
        Assert.False(File.Exists(path));
    }
}
