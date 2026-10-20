using AllSky.Capture;
using Xunit;

namespace AllSky.Capture.Tests;

public class FrameStatsTests
{
    [Fact]
    public void MedianAdu_OddCount_ReturnsMiddleValue()
    {
        Assert.Equal(3, FrameStats.MedianAdu([5, 1, 3], 1));
    }

    [Fact]
    public void MedianAdu_EvenCount_ReturnsLowerMiddle()
    {
        Assert.Equal(2, FrameStats.MedianAdu([4, 1, 3, 2], 1));
    }

    [Fact]
    public void MedianAdu_SubsamplesEveryStepPixel()
    {
        // Campione con step 2: indici 0, 2, 4 -> valori 10, 20, 30 -> mediana 20.
        Assert.Equal(20, FrameStats.MedianAdu([10, 0, 20, 0, 30, 0], 2));
    }

    [Fact]
    public void MedianAdu_StepLargerThanLength_UsesFirstPixelOnly()
    {
        Assert.Equal(7, FrameStats.MedianAdu([7, 9], 5));
    }

    [Fact]
    public void MedianAdu_HandlesFullRange()
    {
        Assert.Equal(65535, FrameStats.MedianAdu([0, 65535, 65535], 1));
        Assert.Equal(0, FrameStats.MedianAdu([0, 0, 65535], 1));
    }

    [Fact]
    public void MedianAdu_RejectsEmptySampleAndInvalidStep()
    {
        Assert.Throws<ArgumentException>(() => FrameStats.MedianAdu([], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameStats.MedianAdu([1, 2], 0));
    }
}
