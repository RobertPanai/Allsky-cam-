using AllSky.Core.Nights;
using Xunit;

namespace AllSky.Core.Tests.Nights;

public sealed class KeogramScrubberTests
{
    [Fact]
    public void FractionAt_IsProportionalAndClamped()
    {
        Assert.Equal(0.25, KeogramScrubber.FractionAt(50, 200));
        Assert.Equal(0, KeogramScrubber.FractionAt(-30, 200));
        Assert.Equal(1, KeogramScrubber.FractionAt(260, 200));
    }

    [Fact]
    public void FractionAt_DegenerateInputs_AreZero()
    {
        Assert.Equal(0, KeogramScrubber.FractionAt(10, 0));
        Assert.Equal(0, KeogramScrubber.FractionAt(10, -5));
        Assert.Equal(0, KeogramScrubber.FractionAt(double.NaN, 200));
        Assert.Equal(0, KeogramScrubber.FractionAt(10, double.PositiveInfinity));
    }

    [Fact]
    public void FrameIndexAt_NoFrames_IsMinusOne()
    {
        Assert.Equal(-1, KeogramScrubber.FrameIndexAt(0.5, 0));
        Assert.Equal(-1, KeogramScrubber.FrameIndexAt(0.5, -3));
    }

    [Fact]
    public void FrameIndexAt_CoversTheWholeRange()
    {
        Assert.Equal(0, KeogramScrubber.FrameIndexAt(0, 10));
        Assert.Equal(5, KeogramScrubber.FrameIndexAt(0.5, 10));
        Assert.Equal(9, KeogramScrubber.FrameIndexAt(0.999, 10));
        Assert.Equal(9, KeogramScrubber.FrameIndexAt(1, 10));
    }

    [Fact]
    public void FrameIndexAt_OutOfRangeFractions_AreClamped()
    {
        Assert.Equal(0, KeogramScrubber.FrameIndexAt(-0.4, 10));
        Assert.Equal(9, KeogramScrubber.FrameIndexAt(3, 10));
        Assert.Equal(0, KeogramScrubber.FrameIndexAt(double.NaN, 10));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(480)]
    [InlineData(4096)]
    [InlineData(5000)]
    public void FrameOfCenter_RoundTrips_ForEveryFrame(int count)
    {
        for (int i = 0; i < count; i++)
        {
            double? fraction = KeogramScrubber.FractionOfFrame(i, count);

            Assert.NotNull(fraction);
            Assert.Equal(i, KeogramScrubber.FrameIndexAt(fraction!.Value, count));
        }
    }

    [Fact]
    public void FractionOfFrame_InvalidIndex_IsNull()
    {
        Assert.Null(KeogramScrubber.FractionOfFrame(-1, 10));
        Assert.Null(KeogramScrubber.FractionOfFrame(10, 10));
        Assert.Null(KeogramScrubber.FractionOfFrame(0, 0));
    }
}
