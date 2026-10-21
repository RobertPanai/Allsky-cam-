using AllSky.Engine;
using Xunit;

namespace AllSky.Engine.Tests;

public class ExposureLimitsTests
{
    [Theory]
    [InlineData(0.01, 60000, 30000, 0.01, 30000)]
    [InlineData(0.01, 20000, 30000, 0.01, 20000)]
    [InlineData(0.5, 30000, 60000, 0.5, 30000)]
    public void ValoriNormali_RestanoInvariati(double infoMin, double infoMax, double configMax, double min, double max)
    {
        (double gotMin, double gotMax) = ExposureLimits.Sanitize(infoMin, infoMax, configMax);

        Assert.Equal(min, gotMin);
        Assert.Equal(max, gotMax);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void MinNonValido_UsaUnMicrosecondo(double infoMin)
    {
        (double min, double max) = ExposureLimits.Sanitize(infoMin, 60000, 30000);

        Assert.Equal(ExposureLimits.FallbackMinMs, min);
        Assert.Equal(0.001, min);
        Assert.Equal(30000, max);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MaxDelDriverNonValido_VieneIgnorato(double infoMax)
    {
        (double min, double max) = ExposureLimits.Sanitize(0.01, infoMax, 30000);

        Assert.Equal(0.01, min);
        Assert.Equal(30000, max);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ConfigMaxNonValido_UsaTrentaSecondi(double configMax)
    {
        (double min, double max) = ExposureLimits.Sanitize(0.01, 60000, configMax);

        Assert.Equal(0.01, min);
        Assert.Equal(ExposureLimits.FallbackMaxMs, max);
        Assert.Equal(30000, max);
    }

    [Fact]
    public void ConfigMaxPiuPiccolaDelDriver_VinceLaConfigurazione()
    {
        (double min, double max) = ExposureLimits.Sanitize(0.01, 60000, 1500);

        Assert.Equal(0.01, min);
        Assert.Equal(1500, max);
    }

    [Fact]
    public void ConfigMaxPiuPiccolaDelMinimo_PortaMaxAlMinimo()
    {
        (double min, double max) = ExposureLimits.Sanitize(5.0, 60000, 2.0);

        Assert.Equal(5.0, min);
        Assert.Equal(5.0, max);
    }

    [Fact]
    public void MaxDelDriverSottoIlMinimo_PortaMaxAlMinimo()
    {
        (double min, double max) = ExposureLimits.Sanitize(10.0, 5.0, 30000);

        Assert.Equal(10.0, min);
        Assert.Equal(10.0, max);
    }

    [Fact]
    public void DriverTuttoZero_DaRipiegoSicuro()
    {
        (double min, double max) = ExposureLimits.Sanitize(0.0, 0.0, 0.0);

        Assert.Equal(0.001, min);
        Assert.Equal(30000, max);
    }

    [Fact]
    public void Output_NonContieneMaiNaNOInfiniti()
    {
        double[] values =
        [
            double.NaN, double.PositiveInfinity, double.NegativeInfinity,
            -1e12, -1.0, 0.0, 1e-12, 0.001, 0.5, 30000, 1e12, double.MaxValue, double.Epsilon,
        ];

        foreach (double a in values)
        {
            foreach (double b in values)
            {
                foreach (double c in values)
                {
                    (double min, double max) = ExposureLimits.Sanitize(a, b, c);

                    Assert.True(double.IsFinite(min) && min > 0.0, $"min non valido per ({a}, {b}, {c}): {min}");
                    Assert.True(double.IsFinite(max), $"max non finito per ({a}, {b}, {c}): {max}");
                    Assert.True(max >= min, $"max < min per ({a}, {b}, {c}): {min}, {max}");
                }
            }
        }
    }
}
