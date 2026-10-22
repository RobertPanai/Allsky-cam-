using System.Globalization;
using AllSky.Core.Settings;
using Xunit;

namespace AllSky.Core.Tests.Settings;

public sealed class SiteFormTests
{
    private static readonly CultureInfo Italian = new("it-IT");

    [Fact]
    public void Check_ValoriValidi_ConVirgolaEPunto()
    {
        SiteCheck check = SiteForm.Check("45,07", "7.69", "240", Italian);

        Assert.True(check.IsValid);
        Assert.Equal(45.07, check.Latitude!.Value, 6);
        Assert.Equal(7.69, check.Longitude!.Value, 6);
        Assert.Equal(240, check.ElevationM!.Value);
    }

    [Fact]
    public void Check_LatitudineFuoriIntervallo()
    {
        SiteCheck check = SiteForm.Check("90,5", "0", "0", Italian);

        Assert.False(check.IsValid);
        Assert.Equal(SiteFieldProblem.LatitudeOutOfRange, check.LatitudeProblem);
        Assert.Null(check.Latitude);
    }

    [Fact]
    public void Check_LongitudineFuoriIntervallo()
    {
        SiteCheck check = SiteForm.Check("0", "-180,1", "0", Italian);

        Assert.Equal(SiteFieldProblem.LongitudeOutOfRange, check.LongitudeProblem);
    }

    [Fact]
    public void Check_QuotaDalMarMortoAlleAlpi()
    {
        Assert.True(SiteForm.Check("0", "0", "-430", Italian).IsValid);
        Assert.True(SiteForm.Check("0", "0", "9000", Italian).IsValid);
        Assert.Equal(SiteFieldProblem.ElevationOutOfRange, SiteForm.Check("0", "0", "9000,1", Italian).ElevationProblem);
        Assert.Equal(SiteFieldProblem.ElevationOutOfRange, SiteForm.Check("0", "0", "-501", Italian).ElevationProblem);
    }

    [Fact]
    public void Check_CampoVuotoENonNumerico()
    {
        SiteCheck empty = SiteForm.Check("", " ", null, Italian);
        Assert.Equal(SiteFieldProblem.Empty, empty.LatitudeProblem);
        Assert.Equal(SiteFieldProblem.Empty, empty.LongitudeProblem);
        Assert.Equal(SiteFieldProblem.Empty, empty.ElevationProblem);

        Assert.Equal(SiteFieldProblem.NotNumber, SiteForm.Check("abc", "0", "0", Italian).LatitudeProblem);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Check_NonFiniti_NonSonoNumeriValidi(string text)
    {
        Assert.Equal(SiteFieldProblem.NotNumber, SiteForm.Check(text, "0", "0", CultureInfo.InvariantCulture).LatitudeProblem);
    }

    [Fact]
    public void HasChanges_SoloSeValidoEDiverso()
    {
        SiteCheck same = SiteForm.Check("45,07", "7,69", "240", Italian);
        Assert.False(SiteForm.HasChanges(same, 45.07, 7.69, 240));

        SiteCheck changed = SiteForm.Check("45,10", "7,69", "240", Italian);
        Assert.True(SiteForm.HasChanges(changed, 45.07, 7.69, 240));

        SiteCheck invalid = SiteForm.Check("99", "7,69", "240", Italian);
        Assert.False(SiteForm.HasChanges(invalid, 45.07, 7.69, 240));
    }

    [Fact]
    public void Format_NonArrotonda()
    {
        Assert.Equal("45.0712345", SiteForm.Format(45.0712345, CultureInfo.InvariantCulture));
    }
}
