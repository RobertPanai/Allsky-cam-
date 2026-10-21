using System.Text;
using AllSky.Publish;
using Xunit;

namespace AllSky.Publish.Tests.Settings;

public sealed class BootstrapLoaderTests : IDisposable
{
    private readonly TempFolder _dir = new();
    private readonly List<string> _logs = new();

    public void Dispose() => _dir.Dispose();

    private PublishSettingsStore NewStore() => new(_dir.File("publish-settings.json"), new FakeProtector());

    private string BootstrapPath => _dir.File("bootstrap.json");

    private void WriteBootstrap(string json) => File.WriteAllText(BootstrapPath, json, Encoding.UTF8);

    private const string ValidJson = """
        {
          "enabled": true,
          "serverUrl": "https://server.example.org",
          "deviceId": "cam-02",
          "token": "BOOT-TOKEN-SEGRETO-999",
          "intervalSeconds": 120,
          "campoSconosciuto": { "x": 1 }
        }
        """;

    [Fact]
    public void MissingFile_ReturnsNotFound_AndStoreUntouched()
    {
        var store = NewStore();

        BootstrapResult r = BootstrapLoader.Apply(BootstrapPath, store, _logs.Add);

        Assert.Equal(BootstrapResult.NotFound, r);
        Assert.False(File.Exists(store.FilePath));
        Assert.Empty(_logs);
    }

    [Fact]
    public void ValidFile_IsApplied_SavedAndDeleted()
    {
        var store = NewStore();
        WriteBootstrap(ValidJson);

        BootstrapResult r = BootstrapLoader.Apply(BootstrapPath, store, _logs.Add);

        Assert.Equal(BootstrapResult.Applied, r);
        Assert.False(File.Exists(BootstrapPath));

        PublishSettings s = store.Load();
        Assert.True(s.Enabled);
        Assert.Equal("https://server.example.org", s.ServerUrl);
        Assert.Equal("cam-02", s.DeviceId);
        Assert.Equal("BOOT-TOKEN-SEGRETO-999", s.Token.Reveal());
        Assert.Equal(120, s.IntervalSeconds);
        Assert.Contains("Impostazioni di avvio applicate", _logs);
    }

    [Fact]
    public void AbsentFields_KeepCurrentStoreValues()
    {
        var store = NewStore();
        store.Save(SampleSettings.Valid());
        WriteBootstrap("""{ "intervalSeconds": 300 }""");

        BootstrapResult r = BootstrapLoader.Apply(BootstrapPath, store, _logs.Add);

        Assert.Equal(BootstrapResult.Applied, r);
        PublishSettings s = store.Load();
        Assert.Equal(300, s.IntervalSeconds);
        Assert.Equal(SampleSettings.Token, s.Token.Reveal());
        Assert.Equal("cam-01.nord_2", s.DeviceId);
        Assert.Equal("Cielo di prova", s.ProfileName);
        Assert.Equal(SampleSettings.HeaderValue, s.ExtraHeaderValue.Reveal());
    }

    [Fact]
    public void InvalidFile_IsLeftIntact_AndNothingSaved()
    {
        var store = NewStore();
        string content = """{ "enabled": true, "serverUrl": "http://server.example.org", "deviceId": "cam", "token": "T-1" }""";
        WriteBootstrap(content);

        BootstrapResult r = BootstrapLoader.Apply(BootstrapPath, store, _logs.Add);

        Assert.Equal(BootstrapResult.Invalid, r);
        Assert.True(File.Exists(BootstrapPath));
        Assert.Equal(content, File.ReadAllText(BootstrapPath, Encoding.UTF8));
        Assert.False(File.Exists(store.FilePath));
        Assert.Contains(_logs, l => l.Contains("serverUrl"));
    }

    [Fact]
    public void MalformedJson_IsInvalid_AndKept()
    {
        var store = NewStore();
        WriteBootstrap("{ \"token\": \"BOOT-TOKEN-SEGRETO-999\", ");

        BootstrapResult r = BootstrapLoader.Apply(BootstrapPath, store, _logs.Add);

        Assert.Equal(BootstrapResult.Invalid, r);
        Assert.True(File.Exists(BootstrapPath));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void WrongFieldType_IsInvalid()
    {
        var store = NewStore();
        WriteBootstrap("""{ "intervalSeconds": "molto" }""");

        Assert.Equal(BootstrapResult.Invalid, BootstrapLoader.Apply(BootstrapPath, store, _logs.Add));
        Assert.True(File.Exists(BootstrapPath));
    }

    [Fact]
    public void Token_NeverAppearsInLogs_OnSuccess()
    {
        var store = NewStore();
        WriteBootstrap(ValidJson);

        BootstrapLoader.Apply(BootstrapPath, store, _logs.Add);

        Assert.NotEmpty(_logs);
        foreach (string line in _logs)
            Assert.DoesNotContain("BOOT-TOKEN-SEGRETO-999", line);
    }

    [Fact]
    public void Token_NeverAppearsInLogs_OnInvalid()
    {
        var store = NewStore();
        WriteBootstrap("""{ "serverUrl": "https://u:p@server.example.org", "token": "BOOT-TOKEN-SEGRETO-999" }""");

        BootstrapResult r = BootstrapLoader.Apply(BootstrapPath, store, _logs.Add);

        Assert.Equal(BootstrapResult.Invalid, r);
        Assert.NotEmpty(_logs);
        foreach (string line in _logs)
        {
            Assert.DoesNotContain("BOOT-TOKEN-SEGRETO-999", line);
            Assert.DoesNotContain("server.example.org", line);
        }
    }

    [Fact]
    public void ThrowingLogger_DoesNotBreakApply()
    {
        var store = NewStore();
        WriteBootstrap(ValidJson);

        BootstrapResult r = BootstrapLoader.Apply(BootstrapPath, store, _ => throw new IOException("log guasto"));

        Assert.Equal(BootstrapResult.Applied, r);
        Assert.True(store.Load().Enabled);
    }
}
