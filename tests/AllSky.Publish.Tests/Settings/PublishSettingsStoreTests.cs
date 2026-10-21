using System.Text;
using AllSky.Publish;
using Xunit;

namespace AllSky.Publish.Tests.Settings;

public sealed class PublishSettingsStoreTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var store = new PublishSettingsStore(_dir.File("publish-settings.json"), new FakeProtector());
        PublishSettings original = SampleSettings.Valid();

        store.Save(original);
        PublishSettings loaded = store.Load();

        Assert.Equal(original, loaded);
        Assert.Equal(SampleSettings.Token, loaded.Token.Reveal());
        Assert.Equal(SampleSettings.HeaderValue, loaded.ExtraHeaderValue.Reveal());
    }

    [Fact]
    public void RoundTrip_EmptySecrets_StayEmpty()
    {
        var store = new PublishSettingsStore(_dir.File("publish-settings.json"), new FakeProtector());
        var original = new PublishSettings { Enabled = false, ServerUrl = "", DeviceId = "" };

        store.Save(original);
        PublishSettings loaded = store.Load();

        Assert.True(loaded.Token.IsEmpty);
        Assert.True(loaded.ExtraHeaderValue.IsEmpty);
        Assert.False(loaded.Enabled);
    }

    [Fact]
    public void MissingFile_ReturnsDefaultsDisabled()
    {
        var store = new PublishSettingsStore(_dir.File("non-esiste.json"), new FakeProtector());

        PublishSettings s = store.Load();

        Assert.False(s.Enabled);
        Assert.Equal("", s.ServerUrl);
        Assert.True(s.Token.IsEmpty);
        Assert.Equal(60, s.IntervalSeconds);
        Assert.Equal(1200, s.LongEdgePx);
        Assert.Equal(75, s.JpegQuality);
    }

    [Fact]
    public void CorruptFile_IsMovedToBad_AndDefaultsReturned()
    {
        string path = _dir.File("publish-settings.json");
        File.WriteAllText(path, "{ questo non e' json");
        var store = new PublishSettingsStore(path, new FakeProtector());

        PublishSettings s = store.Load();

        Assert.False(s.Enabled);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bad"));
    }

    [Fact]
    public void UndecryptableSecret_BecomesEmpty_WithoutThrowing()
    {
        string path = _dir.File("publish-settings.json");
        new PublishSettingsStore(path, new FakeProtector()).Save(SampleSettings.Valid());

        var reader = new PublishSettingsStore(path, new FailingUnprotectProtector());
        PublishSettings s = reader.Load();

        Assert.True(s.Token.IsEmpty);
        Assert.True(s.ExtraHeaderValue.IsEmpty);
        // I campi non segreti restano leggibili.
        Assert.Equal("https://server.example.org/base", s.ServerUrl);
        Assert.Equal("cam-01.nord_2", s.DeviceId);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void UnsupportedProtector_OnLoad_DoesNotThrow()
    {
        string path = _dir.File("publish-settings.json");
        new PublishSettingsStore(path, new FakeProtector()).Save(SampleSettings.Valid());

        PublishSettings s = new PublishSettingsStore(path, new UnsupportedProtector()).Load();

        Assert.True(s.Token.IsEmpty);
        Assert.Equal("cam-01.nord_2", s.DeviceId);
    }

    [Fact]
    public void File_NeverContainsPlainSecrets()
    {
        string path = _dir.File("publish-settings.json");
        new PublishSettingsStore(path, new FakeProtector()).Save(SampleSettings.Valid());

        string text = File.ReadAllText(path, Encoding.UTF8);

        Assert.DoesNotContain(SampleSettings.Token, text);
        Assert.DoesNotContain(SampleSettings.HeaderValue, text);
        Assert.Contains("tokenProtected", text);
        Assert.Contains("extraHeaderValueProtected", text);
        Assert.Contains("\"serverUrl\"", text);
    }

    [Fact]
    public void Save_UnsupportedProtector_ThrowsItalianMessage_AndWritesNothing()
    {
        string path = _dir.File("publish-settings.json");
        var store = new PublishSettingsStore(path, new UnsupportedProtector());

        var ex = Assert.Throws<InvalidOperationException>(() => store.Save(SampleSettings.Valid()));

        Assert.Contains("non sono state salvate", ex.Message);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_WithoutSecrets_DoesNotNeedProtector()
    {
        string path = _dir.File("publish-settings.json");
        var store = new PublishSettingsStore(path, new UnsupportedProtector());

        store.Save(new PublishSettings { Enabled = false, ServerUrl = "", DeviceId = "cam" });

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Save_IsAtomic_ReplacesExistingAndLeavesNoTmp()
    {
        string path = _dir.File("publish-settings.json");
        var store = new PublishSettingsStore(path, new FakeProtector());

        store.Save(SampleSettings.Valid());
        store.Save(SampleSettings.Valid() with { IntervalSeconds = 300 });

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(300, store.Load().IntervalSeconds);
    }

    [Fact]
    public void Save_CreatesMissingFolder()
    {
        string path = Path.Combine(_dir.Path, "sotto", "cartella", "publish-settings.json");
        var store = new PublishSettingsStore(path, new FakeProtector());

        store.Save(SampleSettings.Valid());

        Assert.True(File.Exists(path));
    }
}
