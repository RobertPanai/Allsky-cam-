using System.Globalization;
using AllSky.Core.Publication;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class PublicationFieldMessagesTests
{
    [Theory]
    [InlineData("serverUrl", PublicationField.ServerUrl)]
    [InlineData("deviceId", PublicationField.DeviceId)]
    [InlineData("token", PublicationField.Token)]
    [InlineData("profileName", PublicationField.ProfileName)]
    [InlineData("intervalSeconds", PublicationField.IntervalSeconds)]
    [InlineData("longEdgePx", PublicationField.LongEdgePx)]
    [InlineData("jpegQuality", PublicationField.JpegQuality)]
    [InlineData("extraHeaderName", PublicationField.ExtraHeaderName)]
    [InlineData("extraHeaderValue", PublicationField.ExtraHeaderValue)]
    [InlineData("publish", PublicationField.General)]
    [InlineData("campo-sconosciuto", PublicationField.General)]
    [InlineData(null, PublicationField.General)]
    public void FieldOf_MapsServerPathsToFields(string? path, PublicationField expected)
    {
        Assert.Equal(expected, PublicationFieldMessages.FieldOf(path));
    }

    [Fact]
    public void NameOf_EveryField_HasANonEmptyNameInBothLanguages()
    {
        foreach (PublicationField field in Enum.GetValues<PublicationField>())
        {
            Assert.False(string.IsNullOrWhiteSpace(PublicationFieldMessages.NameOf(field, DesktopTexts.Italian)));
            Assert.False(string.IsNullOrWhiteSpace(PublicationFieldMessages.NameOf(field, DesktopTexts.English)));
        }
    }

    [Fact]
    public void IssueLine_ServerUrl_UsesTheFieldNameAndTheMessage()
    {
        var issue = new PublicationIssue("serverUrl", "Indirizzo non valido.");

        string line = PublicationFieldMessages.IssueLine(issue, DesktopTexts.English);

        Assert.Equal(DesktopTexts.English.PublicationServerUrl + ": Indirizzo non valido.", line);
    }

    [Fact]
    public void IssueLine_UnavailableModule_UsesTheLocalizedText()
    {
        var issue = new PublicationIssue("publish", "Messaggio del server.");

        Assert.Equal(DesktopTexts.Italian.PublicationUnavailable, PublicationFieldMessages.IssueLine(issue, DesktopTexts.Italian));
        Assert.DoesNotContain("Messaggio del server.", PublicationFieldMessages.IssueLine(issue, DesktopTexts.Italian));
    }

    [Fact]
    public void IssueLine_UnknownPath_UsesTheGeneralName()
    {
        var issue = new PublicationIssue("altro", "Qualcosa non va.");

        string line = PublicationFieldMessages.IssueLine(issue, DesktopTexts.Italian);

        Assert.StartsWith(DesktopTexts.Italian.PublicationFieldGeneral + ": ", line);
    }

    [Fact]
    public void ForNumber_Valid_HasNoMessage()
    {
        Assert.Null(PublicationFieldMessages.ForNumber(PublicationNumberProblem.None, 15, 86400, DesktopTexts.Italian));
    }

    [Fact]
    public void ForNumber_OutOfRange_ShowsBothLimits()
    {
        string? text = PublicationFieldMessages.ForNumber(PublicationNumberProblem.OutOfRange, 200, 2000, DesktopTexts.English);

        Assert.NotNull(text);
        Assert.Contains("200", text);
        Assert.Contains("2000", text);
    }

    [Fact]
    public void ForNumber_Empty_UsesTheEmptyText()
    {
        Assert.Equal(DesktopTexts.Italian.PublicationProblemEmpty,
            PublicationFieldMessages.ForNumber(PublicationNumberProblem.Empty, 1, 2, DesktopTexts.Italian));
    }

    [Fact]
    public void ForNumber_NotInteger_UsesTheIntegerText()
    {
        Assert.Equal(DesktopTexts.English.PublicationProblemNotInteger,
            PublicationFieldMessages.ForNumber(PublicationNumberProblem.NotInteger, 1, 2, DesktopTexts.English));
    }
}
