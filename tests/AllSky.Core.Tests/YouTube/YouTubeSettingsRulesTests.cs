using AllSky.Core;
using AllSky.Core.YouTube;
using Xunit;

namespace AllSky.Core.Tests.YouTube;

public sealed class YouTubeSettingsRulesTests
{
    private static YouTubeSettingsChange Valid() => new("12:30", "private", ["timelapse", "allsky"], "28");

    [Fact]
    public void Validate_ModificaValida_NessunProblema()
    {
        Assert.Empty(YouTubeSettingsRules.Validate(Valid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("8.00")]
    [InlineData("25:00")]
    [InlineData("12:30:00")]
    [InlineData("12:3")]
    public void Validate_OraNonValida_Segnalata(string time)
    {
        var problems = YouTubeSettingsRules.Validate(Valid() with { DailyTime = time });
        Assert.Contains(YouTubeSettingsProblem.DailyTimeInvalid, problems);
    }

    [Theory]
    [InlineData("Private")]
    [InlineData("hidden")]
    [InlineData("")]
    public void Validate_PrivacySconosciuta_Segnalata(string privacy)
    {
        var problems = YouTubeSettingsRules.Validate(Valid() with { Privacy = privacy });
        Assert.Contains(YouTubeSettingsProblem.PrivacyInvalid, problems);
    }

    [Theory]
    [InlineData("")]
    [InlineData("28a")]
    [InlineData("-1")]
    [InlineData("  ")]
    public void Validate_CategoriaNonNumerica_Segnalata(string category)
    {
        var problems = YouTubeSettingsRules.Validate(Valid() with { CategoryId = category });
        Assert.Contains(YouTubeSettingsProblem.CategoryIdInvalid, problems);
    }

    [Fact]
    public void Validate_TagVuoto_Segnalato()
    {
        var problems = YouTubeSettingsRules.Validate(Valid() with { Tags = ["timelapse", " "] });
        Assert.Contains(YouTubeSettingsProblem.TagEmpty, problems);
    }

    [Fact]
    public void ParseTags_SeparaVirgolaTogliSpaziEVociVuote()
    {
        Assert.Equal(["a", "b", "c"], YouTubeSettingsRules.ParseTags(" a, b ,,c, "));
        Assert.Empty(YouTubeSettingsRules.ParseTags("   "));
    }

    [Fact]
    public void Apply_NormalizzaOraETagEMantieEnabledEClientId()
    {
        var current = new AppConfig.YouTubeConfig { Enabled = true, ClientId = "cid-1" };
        var change = new YouTubeSettingsChange(" 07:05 ", "unlisted", [" tag1 ", "tag2"], "27");

        AppConfig.YouTubeConfig next = YouTubeSettingsRules.Apply(current, change);

        Assert.Equal("07:05", next.DailyTime);
        Assert.Equal("unlisted", next.Privacy);
        Assert.Equal(["tag1", "tag2"], next.Tags);
        Assert.Equal("27", next.CategoryId);
        Assert.True(next.Enabled);
        Assert.Equal("cid-1", next.ClientId);
    }

    [Fact]
    public void ValidateConfig_Predefinita_NessunErrore()
    {
        Assert.Empty(YouTubeSettingsRules.ValidateConfig(new AppConfig.YouTubeConfig()));
    }

    [Fact]
    public void ValidateConfig_FinestraFuoriIntervallo_Segnalata()
    {
        var errors = YouTubeSettingsRules.ValidateConfig(new AppConfig.YouTubeConfig { CatchUpWindowHours = 25 });
        Assert.Contains(errors, e => e.Path == "youtube.catchUpWindowHours");
    }

    [Fact]
    public void ValidateConfig_LuogoVuoto_Segnalato()
    {
        var errors = YouTubeSettingsRules.ValidateConfig(new AppConfig.YouTubeConfig { Location = " " });
        Assert.Contains(errors, e => e.Path == "youtube.location");
    }
}
