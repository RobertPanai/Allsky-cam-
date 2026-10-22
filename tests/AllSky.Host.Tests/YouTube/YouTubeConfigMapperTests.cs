using AllSky.Core;
using Xunit;

namespace AllSky.Host.Tests.YouTube;

public sealed class YouTubeConfigMapperTests
{
    [Fact]
    public void Predefinita_EValida()
    {
        Assert.Empty(YouTubeConfigMapper.Errors(new AppConfig.YouTubeConfig()));
    }

    [Fact]
    public void ModelloDiTitoloNonValido_Segnalato()
    {
        var config = new AppConfig.YouTubeConfig { TitleTemplate = "{notto:yyyy}" };
        Assert.Contains(YouTubeConfigMapper.Errors(config), e => e.Path == "youtube.titleTemplate");
    }

    [Fact]
    public void ToUploadSettings_MappaValoriEPrivacy()
    {
        var config = new AppConfig.YouTubeConfig
        {
            DailyTime = "07:45",
            Privacy = "unlisted",
            CatchUpWindowHours = 6,
            Tags = [" a ", "b"],
            CategoryId = "27",
        };

        var settings = YouTubeConfigMapper.ToUploadSettings(config);

        Assert.Equal(new TimeOnly(7, 45), settings.DailyTime);
        Assert.Equal(TimeSpan.FromHours(6), settings.CatchUpWindow);
        Assert.Equal(AllSky.YouTube.YouTubePrivacy.Unlisted, settings.Privacy);
        Assert.Equal(["a", "b"], settings.Tags);
        Assert.Equal("27", settings.CategoryId);
        Assert.Equal(1, settings.MaxUploadsPerDay);
    }

    [Fact]
    public void ToUploadSettings_ConfigurazioneNonValida_Lancia()
    {
        Assert.Throws<ArgumentException>(() => YouTubeConfigMapper.ToUploadSettings(new AppConfig.YouTubeConfig { DailyTime = "x" }));
    }

    [Fact]
    public void ResolveZone_NomeSconosciuto_UsaUtc()
    {
        Assert.Equal(TimeZoneInfo.Utc, YouTubeConfigMapper.ResolveZone("Nessun/Fuso"));
    }
}
