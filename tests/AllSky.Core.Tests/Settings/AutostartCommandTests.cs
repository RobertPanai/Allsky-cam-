using AllSky.Core.Settings;
using Xunit;

namespace AllSky.Core.Tests.Settings;

public sealed class AutostartCommandTests
{
    [Fact]
    public void For_RacchiudeIlPercorsoTraVirgolette()
    {
        Assert.Equal("\"C:\\Programmi\\All Sky Brain\\AllSkyBrain.exe\"",
            AutostartCommand.For(@"C:\Programmi\All Sky Brain\AllSkyBrain.exe"));
    }

    [Fact]
    public void For_PercorsoVuotoLancia()
    {
        Assert.Throws<ArgumentException>(() => AutostartCommand.For("  "));
    }

    [Fact]
    public void Nomi_RestanoQuelliDelleVersioniPrecedenti()
    {
        Assert.Equal("AllSkyBrain", AutostartCommand.ValueName);
        Assert.Equal("AllSky", AutostartCommand.LegacyValueName);
    }
}
