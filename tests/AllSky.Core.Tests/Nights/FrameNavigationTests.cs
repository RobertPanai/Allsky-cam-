using AllSky.Core.Nights;
using Xunit;

namespace AllSky.Core.Tests.Nights;

public sealed class FrameNavigationTests
{
    [Fact]
    public void Step_MovesAndStaysInsideTheNight()
    {
        Assert.Equal(4, FrameNavigation.Step(3, 1, 10));
        Assert.Equal(2, FrameNavigation.Step(3, -1, 10));
        Assert.Equal(0, FrameNavigation.Step(0, -1, 10));
        Assert.Equal(9, FrameNavigation.Step(9, 1, 10));
        Assert.Equal(0, FrameNavigation.Step(2, -50, 10));
    }

    [Fact]
    public void Step_WithoutSelection_StartsAtAnEnd()
    {
        Assert.Equal(0, FrameNavigation.Step(-1, 1, 10));
        Assert.Equal(9, FrameNavigation.Step(-1, -1, 10));
    }

    [Fact]
    public void Step_StaleIndexBeyondTheNight_IsPulledBack()
    {
        Assert.Equal(3, FrameNavigation.Step(40, -1, 5));
        Assert.Equal(4, FrameNavigation.Step(40, 1, 5));
    }

    [Fact]
    public void Step_EmptyNight_IsMinusOne()
    {
        Assert.Equal(-1, FrameNavigation.Step(0, 1, 0));
        Assert.Equal(-1, FrameNavigation.Step(-1, -1, 0));
    }
}
