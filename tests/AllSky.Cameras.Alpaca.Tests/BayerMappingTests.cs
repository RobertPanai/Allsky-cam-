using AllSky.Core;
using Xunit;

namespace AllSky.Cameras.Alpaca.Tests;

/// <summary>Test della mappatura SensorType + BayerOffset in <see cref="BayerPattern"/>.</summary>
public sealed class BayerMappingTests
{
    [Theory]
    [InlineData(0, 0, BayerPattern.RGGB)]
    [InlineData(1, 0, BayerPattern.GRBG)]
    [InlineData(0, 1, BayerPattern.GBRG)]
    [InlineData(1, 1, BayerPattern.BGGR)]
    public void MapBayer_Rggb_FollowsOffsetPhase(int offsetX, int offsetY, BayerPattern expected)
    {
        Assert.Equal(expected, AlpacaCamera.MapBayer(2, offsetX, offsetY));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void MapBayer_NonRggbSensor_ThrowsNotSupported(int sensorType)
    {
        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => AlpacaCamera.MapBayer(sensorType, 0, 0));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }
}
