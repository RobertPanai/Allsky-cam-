using System.Globalization;
using AllSky.Core.Publication;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class PublicationSettingsFormTests
{
    private const string SecretToken = "tok-segreto-di-prova-987";

    private static PublicationSettingsSnapshot Saved(bool hasToken = true, bool hasHeader = false) => new(
        Enabled: true,
        ServerUrl: "https://server.example",
        DeviceId: "allsky-1",
        HasToken: hasToken,
        ProfileName: "Cielo di prova",
        IntervalSeconds: 60,
        LongEdgePx: 1200,
        JpegQuality: 75,
        ExtraHeaderName: null,
        HasExtraHeaderValue: hasHeader);

    private static PublicationSettingsChange SameAsSaved() => new(
        Enabled: true,
        ServerUrl: "https://server.example",
        DeviceId: "allsky-1",
        ProfileName: "Cielo di prova",
        IntervalSeconds: 60,
        LongEdgePx: 1200,
        JpegQuality: 75,
        ExtraHeaderName: null,
        NewToken: null,
        ClearToken: false,
        NewExtraHeaderValue: null,
        ClearExtraHeaderValue: false);

    [Fact]
    public void Check_ValidValues_IsValidAndReadsThem()
    {
        PublicationFormCheck check = PublicationSettingsForm.Check("60", "1200", "75", CultureInfo.InvariantCulture);

        Assert.True(check.IsValid);
        Assert.Equal(60, check.IntervalSeconds.Value);
        Assert.Equal(1200, check.LongEdgePx.Value);
        Assert.Equal(75, check.JpegQuality.Value);
    }

    [Fact]
    public void Check_SurroundingSpaces_AreIgnored()
    {
        PublicationNumberCheck check = PublicationSettingsForm.CheckInteger("  90  ", 15, 86400, CultureInfo.InvariantCulture);

        Assert.Equal(PublicationNumberProblem.None, check.Problem);
        Assert.Equal(90, check.Value);
    }

    [Fact]
    public void CheckInteger_Empty_IsEmptyProblem()
    {
        PublicationNumberCheck check = PublicationSettingsForm.CheckInteger("   ", 15, 86400, CultureInfo.InvariantCulture);

        Assert.Equal(PublicationNumberProblem.Empty, check.Problem);
        Assert.Null(check.Value);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("60s")]
    [InlineData("99999999999")]
    public void CheckInteger_NotAWholeNumber_IsNotInteger(string text)
    {
        PublicationNumberCheck check = PublicationSettingsForm.CheckInteger(text, 15, 86400, CultureInfo.InvariantCulture);

        Assert.Equal(PublicationNumberProblem.NotInteger, check.Problem);
        Assert.Null(check.Value);
    }

    [Theory]
    [InlineData("14")]
    [InlineData("86401")]
    [InlineData("-1")]
    public void CheckInteger_OutsideRange_IsOutOfRange(string text)
    {
        PublicationNumberCheck check = PublicationSettingsForm.CheckInteger(text, 15, 86400, CultureInfo.InvariantCulture);

        Assert.Equal(PublicationNumberProblem.OutOfRange, check.Problem);
    }

    [Theory]
    [InlineData("15")]
    [InlineData("86400")]
    public void CheckInteger_RangeLimits_AreIncluded(string text)
    {
        PublicationNumberCheck check = PublicationSettingsForm.CheckInteger(text, 15, 86400, CultureInfo.InvariantCulture);

        Assert.Equal(PublicationNumberProblem.None, check.Problem);
    }

    [Fact]
    public void Check_JpegQualityBounds_MatchTheServerRules()
    {
        Assert.True(PublicationSettingsForm.Check("60", "1200", "30", CultureInfo.InvariantCulture).IsValid);
        Assert.True(PublicationSettingsForm.Check("60", "1200", "95", CultureInfo.InvariantCulture).IsValid);
        Assert.False(PublicationSettingsForm.Check("60", "1200", "29", CultureInfo.InvariantCulture).IsValid);
        Assert.False(PublicationSettingsForm.Check("60", "1200", "96", CultureInfo.InvariantCulture).IsValid);
    }

    [Fact]
    public void Check_LongEdgeBounds_MatchTheServerRules()
    {
        Assert.False(PublicationSettingsForm.Check("60", "199", "75", CultureInfo.InvariantCulture).IsValid);
        Assert.False(PublicationSettingsForm.Check("60", "2001", "75", CultureInfo.InvariantCulture).IsValid);
        Assert.True(PublicationSettingsForm.Check("60", "2000", "75", CultureInfo.InvariantCulture).IsValid);
    }

    [Fact]
    public void Check_ItalianCulture_ReadsPlainIntegers()
    {
        PublicationFormCheck check = PublicationSettingsForm.Check("60", "1200", "75", new CultureInfo("it-IT"));

        Assert.True(check.IsValid);
    }

    [Fact]
    public void HasChanges_SameValues_IsFalse()
    {
        Assert.False(PublicationSettingsForm.HasChanges(SameAsSaved(), Saved()));
    }

    [Fact]
    public void HasChanges_Enabled_IsTrue()
    {
        PublicationSettingsChange change = SameAsSaved() with { Enabled = false };

        Assert.True(PublicationSettingsForm.HasChanges(change, Saved()));
    }

    [Fact]
    public void HasChanges_SurroundingSpacesInUrl_IsFalse()
    {
        PublicationSettingsChange change = SameAsSaved() with { ServerUrl = "  https://server.example " };

        Assert.False(PublicationSettingsForm.HasChanges(change, Saved()));
    }

    [Fact]
    public void HasChanges_EmptyAndNullProfileName_IsFalse()
    {
        PublicationSettingsChange change = SameAsSaved() with { ProfileName = null };
        PublicationSettingsSnapshot current = Saved() with { ProfileName = "   " };

        Assert.False(PublicationSettingsForm.HasChanges(change, current));
    }

    [Fact]
    public void HasChanges_NewToken_IsTrue()
    {
        PublicationSettingsChange change = SameAsSaved() with { NewToken = "nuovo-token" };

        Assert.True(PublicationSettingsForm.HasChanges(change, Saved()));
    }

    [Fact]
    public void HasChanges_WhitespaceToken_IsFalse()
    {
        PublicationSettingsChange change = SameAsSaved() with { NewToken = "   " };

        Assert.False(PublicationSettingsForm.HasChanges(change, Saved()));
    }

    [Fact]
    public void HasChanges_ClearToken_OnlyCountsWhenATokenIsSaved()
    {
        PublicationSettingsChange change = SameAsSaved() with { ClearToken = true };

        Assert.True(PublicationSettingsForm.HasChanges(change, Saved(hasToken: true)));
        Assert.False(PublicationSettingsForm.HasChanges(change, Saved(hasToken: false)));
    }

    [Fact]
    public void HasChanges_ClearExtraHeader_OnlyCountsWhenAValueIsSaved()
    {
        PublicationSettingsChange change = SameAsSaved() with { ClearExtraHeaderValue = true };

        Assert.True(PublicationSettingsForm.HasChanges(change, Saved(hasHeader: true)));
        Assert.False(PublicationSettingsForm.HasChanges(change, Saved(hasHeader: false)));
    }

    [Fact]
    public void HasChanges_Interval_IsTrue()
    {
        PublicationSettingsChange change = SameAsSaved() with { IntervalSeconds = 120 };

        Assert.True(PublicationSettingsForm.HasChanges(change, Saved()));
    }

    [Fact]
    public void ToString_NeverRevealsASecret()
    {
        PublicationSettingsChange change = SameAsSaved() with { NewToken = SecretToken, NewExtraHeaderValue = SecretToken };

        string text = change.ToString();

        Assert.DoesNotContain(SecretToken, text);
        Assert.Contains("nascosti", text);
    }
}
