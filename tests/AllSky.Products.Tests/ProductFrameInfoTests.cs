using AllSky.Products;
using Xunit;

namespace AllSky.Products.Tests;

public class ProductFrameInfoTests
{
    [Fact]
    public void Record_HoldsTimestampAndPhase()
    {
        var ts = new DateTimeOffset(2026, 10, 18, 23, 30, 0, TimeSpan.FromHours(2));
        var info = new ProductFrameInfo(ts, "night");

        Assert.Equal(ts, info.Timestamp);
        Assert.Equal("night", info.Phase);
        Assert.Equal(info, new ProductFrameInfo(ts, "night"));
    }
}
