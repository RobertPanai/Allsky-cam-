using AllSky.Core.CameraView;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class CameraSourcesTests
{
    [Theory]
    [InlineData("sim", CameraSourceChoice.Simulator)]
    [InlineData("ascom-com", CameraSourceChoice.Ascom)]
    [InlineData("ascom-alpaca", CameraSourceChoice.Alpaca)]
    public void ChoiceOf_KnownKinds_MapToTheirSource(string kind, CameraSourceChoice expected)
    {
        Assert.Equal(expected, CameraSources.ChoiceOf(kind));
    }

    [Theory]
    [InlineData("sconosciuto")]
    [InlineData("")]
    [InlineData(null)]
    public void ChoiceOf_UnknownKind_IsNull(string? kind)
    {
        Assert.Null(CameraSources.ChoiceOf(kind));
    }

    [Fact]
    public void Matches_AllAcceptsEveryKind()
    {
        Assert.True(CameraSources.Matches(CameraSourceChoice.All, "sim"));
        Assert.True(CameraSources.Matches(CameraSourceChoice.All, "qualcosa"));
        Assert.True(CameraSources.Matches(CameraSourceChoice.All, null));
    }

    [Fact]
    public void Matches_OnlySameSource()
    {
        Assert.True(CameraSources.Matches(CameraSourceChoice.Alpaca, "ascom-alpaca"));
        Assert.False(CameraSources.Matches(CameraSourceChoice.Alpaca, "ascom-com"));
        Assert.False(CameraSources.Matches(CameraSourceChoice.Simulator, "ascom-alpaca"));
    }

    [Fact]
    public void Choices_ListEveryFilterOnce_AllFirst()
    {
        IReadOnlyList<CameraSourceChoice> choices = CameraSources.Choices;

        Assert.Equal(CameraSourceChoice.All, choices[0]);
        Assert.Equal(Enum.GetValues<CameraSourceChoice>().Length, choices.Count);
        Assert.Equal(choices.Count, choices.Distinct().Count());
    }

    [Fact]
    public void KindLabel_UnknownKind_UsesUnknownText()
    {
        Assert.Equal(DesktopTexts.Italian.StateUnknown, CameraSources.KindLabel("boh", DesktopTexts.Italian));
        Assert.Equal(DesktopTexts.English.CameraSourceAlpaca, CameraSources.KindLabel("ascom-alpaca", DesktopTexts.English));
    }

    [Fact]
    public void EmptyHint_OnlySpecificSourcesHaveAHint()
    {
        Assert.Equal(DesktopTexts.Italian.CameraHintAscom, CameraSources.EmptyHint(CameraSourceChoice.Ascom, DesktopTexts.Italian));
        Assert.Equal(DesktopTexts.Italian.CameraHintAlpaca, CameraSources.EmptyHint(CameraSourceChoice.Alpaca, DesktopTexts.Italian));
        Assert.Null(CameraSources.EmptyHint(CameraSourceChoice.All, DesktopTexts.Italian));
    }

    [Fact]
    public void Alpaca_HintMentionsDiscoveryPort()
    {
        Assert.Contains("32227", DesktopTexts.Italian.CameraHintAlpaca);
        Assert.Contains("32227", DesktopTexts.English.CameraHintAlpaca);
    }
}
