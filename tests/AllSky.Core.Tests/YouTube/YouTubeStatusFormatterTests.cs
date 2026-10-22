using AllSky.Core;
using AllSky.Core.YouTube;
using Xunit;

namespace AllSky.Core.Tests.YouTube;

public sealed class YouTubeStatusFormatterTests
{
    [Fact]
    public void ProblemText_OgniProblemaHaUnTesto()
    {
        foreach (YouTubeProblem problem in Enum.GetValues<YouTubeProblem>())
        {
            Assert.False(string.IsNullOrWhiteSpace(YouTubeStatusFormatter.ProblemText(problem, DesktopTexts.Italian)), problem.ToString());
            Assert.False(string.IsNullOrWhiteSpace(YouTubeStatusFormatter.ProblemText(problem, DesktopTexts.English)), problem.ToString());
        }
    }

    [Fact]
    public void ProblemText_SenzaProblema_Null()
    {
        Assert.Null(YouTubeStatusFormatter.ProblemText(null, DesktopTexts.Italian));
        Assert.Null(YouTubeStatusFormatter.LastProblemLine(null, DesktopTexts.Italian));
    }

    [Fact]
    public void OutcomeText_OgniEsitoHaUnTesto()
    {
        foreach (YouTubeUploadOutcomeKind kind in Enum.GetValues<YouTubeUploadOutcomeKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(YouTubeStatusFormatter.OutcomeText(kind, DesktopTexts.Italian)), kind.ToString());
            Assert.False(string.IsNullOrWhiteSpace(YouTubeStatusFormatter.OutcomeText(kind, DesktopTexts.English)), kind.ToString());
        }
    }

    [Fact]
    public void ErroriEForzaRiprova_Coerenti()
    {
        Assert.True(YouTubeStatusFormatter.IsErrorOutcome(YouTubeUploadOutcomeKind.Failed));
        Assert.False(YouTubeStatusFormatter.IsErrorOutcome(YouTubeUploadOutcomeKind.Completed));
        Assert.True(YouTubeStatusFormatter.CanForceRetry(YouTubeUploadOutcomeKind.NeedsReview));
        Assert.False(YouTubeStatusFormatter.CanForceRetry(YouTubeUploadOutcomeKind.QuotaBlocked));
    }

    [Fact]
    public void NextUploadText_SenzaProssimo_RigaDedicata()
    {
        Assert.Equal(DesktopTexts.Italian.YouTubeNoNextUpload, YouTubeStatusFormatter.NextUploadText(null, DesktopTexts.Italian));
    }

    [Fact]
    public void NextUploadText_ContieneLaData()
    {
        string text = YouTubeStatusFormatter.NextUploadText(new DateTimeOffset(2026, 10, 22, 10, 30, 0, TimeSpan.Zero), DesktopTexts.English);
        Assert.StartsWith("Next upload: ", text);
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(text, @"\d{2}/\d{2}/\d{4} \d{2}:\d{2}"));
    }

    [Fact]
    public void LastUploadText_SenzaCaricamenti_RigaDedicata()
    {
        Assert.Equal(DesktopTexts.Italian.YouTubeNoUploadYet, YouTubeStatusFormatter.LastUploadText(null, DesktopTexts.Italian));
    }

    [Fact]
    public void PrivacyName_Traduce()
    {
        Assert.Equal(DesktopTexts.Italian.YouTubePrivacyUnlisted, YouTubeStatusFormatter.PrivacyName("unlisted", DesktopTexts.Italian));
        Assert.Equal("weird", YouTubeStatusFormatter.PrivacyName("weird", DesktopTexts.Italian));
    }
}
