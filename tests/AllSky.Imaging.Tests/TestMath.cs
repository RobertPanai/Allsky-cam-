using Xunit;

namespace AllSky.Imaging.Tests;

internal static class TestMath
{
    /// <summary>Verifica che |expected - actual| sia entro la tolleranza.</summary>
    public static void AssertNear(double expected, double actual, double tolerance)
    {
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"atteso {expected}, ottenuto {actual} (tolleranza {tolerance})");
    }
}
