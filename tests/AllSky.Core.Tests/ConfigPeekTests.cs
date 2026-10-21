using Xunit;

namespace AllSky.Core.Tests;

public sealed class ConfigPeekTests
{
    /// <summary>Scrive il contenuto in un file temporaneo, esegue la lettura e cancella il file.</summary>
    private static (bool English, int Port) PeekContent(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-peek-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, content);
            return ConfigPeek.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), "allsky-missing-" + Guid.NewGuid().ToString("N") + ".json");

        (bool english, int port) = ConfigPeek.Read(path);

        Assert.False(english);
        Assert.Equal(ConfigPeek.DefaultPort, port);
    }

    [Fact]
    public void ReadsEnglishLanguageAndPort()
    {
        (bool english, int port) = PeekContent("{\"language\":\"en\",\"ui\":{\"port\":6000}}");

        Assert.True(english);
        Assert.Equal(6000, port);
    }

    [Fact]
    public void OtherLanguage_IsItalian()
    {
        var result = PeekContent("{\"language\":\"it\"}");

        Assert.False(result.English);
    }

    [Fact]
    public void InvalidJson_ReturnsDefaults()
    {
        (bool english, int port) = PeekContent("{ non è json");

        Assert.False(english);
        Assert.Equal(ConfigPeek.DefaultPort, port);
    }

    [Fact]
    public void RootNotObject_ReturnsDefaults()
    {
        (bool english, int port) = PeekContent("[1,2,3]");

        Assert.False(english);
        Assert.Equal(ConfigPeek.DefaultPort, port);
    }

    [Fact]
    public void PortNotNumber_KeepsDefaultPort()
    {
        var result = PeekContent("{\"ui\":{\"port\":\"6000\"}}");

        Assert.Equal(ConfigPeek.DefaultPort, result.Port);
    }
}
