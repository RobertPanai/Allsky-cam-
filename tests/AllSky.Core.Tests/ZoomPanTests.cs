using AllSky.Core.Live;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class ZoomPanTests
{
    [Fact]
    public void FitContent_KeepsAspectRatioInsideView()
    {
        (double w, double h) = ZoomPan.FitContent(1600, 1200, 800, 800);

        Assert.Equal(800, w, 6);
        Assert.Equal(600, h, 6);
    }

    [Fact]
    public void FitContent_InvalidSize_ReturnsZero()
    {
        Assert.Equal((0, 0), ZoomPan.FitContent(1600, 1200, 0, 800));
        Assert.Equal((0, 0), ZoomPan.FitContent(double.NaN, 1200, 800, 800));
        Assert.Equal((0, 0), ZoomPan.FitContent(1600, -1, 800, 800));
    }

    [Fact]
    public void Clamp_ContentSmallerThanView_IsCentered()
    {
        ZoomPanState state = ZoomPan.Clamp(new ZoomPanState(1, 0, 0), 800, 600, 400, 300);

        Assert.Equal(1, state.Zoom, 6);
        Assert.Equal(200, state.OffsetX, 6);
        Assert.Equal(150, state.OffsetY, 6);
    }

    [Fact]
    public void Clamp_ZoomedContent_CannotExposeEdges()
    {
        // Immagine 800x600 in vista 800x600 a zoom 2: 1600x1200, offset ammesso tra -800 e 0.
        ZoomPanState past = ZoomPan.Clamp(new ZoomPanState(2, 50, 50), 800, 600, 800, 600);
        ZoomPanState far = ZoomPan.Clamp(new ZoomPanState(2, -2000, -2000), 800, 600, 800, 600);

        Assert.Equal(0, past.OffsetX, 6);
        Assert.Equal(0, past.OffsetY, 6);
        Assert.Equal(-800, far.OffsetX, 6);
        Assert.Equal(-600, far.OffsetY, 6);
    }

    [Fact]
    public void Clamp_ZoomOutsideRange_IsLimited()
    {
        Assert.Equal(ZoomPan.MaxZoom, ZoomPan.Clamp(new ZoomPanState(99, 0, 0), 800, 600, 800, 600).Zoom, 6);
        Assert.Equal(ZoomPan.MinZoom, ZoomPan.Clamp(new ZoomPanState(0.1, 0, 0), 800, 600, 800, 600).Zoom, 6);
    }

    [Fact]
    public void Clamp_NonFiniteValues_FallBackToSafeState()
    {
        ZoomPanState state = ZoomPan.Clamp(new ZoomPanState(double.NaN, double.NaN, double.PositiveInfinity), 800, 600, 800, 600);

        Assert.Equal(ZoomPan.MinZoom, state.Zoom, 6);
        Assert.Equal(0, state.OffsetX, 6);
        Assert.Equal(0, state.OffsetY, 6);
    }

    [Fact]
    public void ZoomAt_KeepsPointUnderCursorStill()
    {
        const double viewW = 800, viewH = 600, contentW = 800, contentH = 600;
        var before = new ZoomPanState(1, 0, 0);

        ZoomPanState after = ZoomPan.ZoomAt(before, 2, 100, 100, viewW, viewH, contentW, contentH);

        // Il punto (100, 100) della vista mostra lo stesso pixel dell'immagine prima e dopo lo zoom.
        double imageXBefore = (100 - before.OffsetX) / before.Zoom;
        double imageXAfter = (100 - after.OffsetX) / after.Zoom;
        Assert.Equal(2, after.Zoom, 6);
        Assert.Equal(imageXBefore, imageXAfter, 6);
        Assert.Equal(-100, after.OffsetX, 6);
        Assert.Equal(-100, after.OffsetY, 6);
    }

    [Fact]
    public void ZoomAt_StopsAtMaximum()
    {
        ZoomPanState state = new ZoomPanState(1, 0, 0);
        for (int i = 0; i < 20; i++)
        {
            state = ZoomPan.ZoomAt(state, ZoomPan.ButtonStep, 400, 300, 800, 600, 800, 600);
        }

        Assert.Equal(ZoomPan.MaxZoom, state.Zoom, 6);
    }

    [Fact]
    public void ZoomAt_OutAtMinimum_StaysCentered()
    {
        ZoomPanState state = ZoomPan.ZoomAt(new ZoomPanState(1, 0, 0), 0.5, 10, 10, 800, 600, 800, 600);

        Assert.Equal(ZoomPan.MinZoom, state.Zoom, 6);
        Assert.Equal(0, state.OffsetX, 6);
        Assert.Equal(0, state.OffsetY, 6);
    }

    [Fact]
    public void ZoomAt_InvalidFactor_OnlyClamps()
    {
        ZoomPanState state = ZoomPan.ZoomAt(new ZoomPanState(2, -100, -100), 0, 0, 0, 800, 600, 800, 600);

        Assert.Equal(2, state.Zoom, 6);
        Assert.Equal(-100, state.OffsetX, 6);
    }

    [Fact]
    public void PanBy_StopsAtEdges()
    {
        var zoomed = new ZoomPanState(2, -400, -300);

        ZoomPanState rightLimit = ZoomPan.PanBy(zoomed, 10000, 10000, 800, 600, 800, 600);
        ZoomPanState leftLimit = ZoomPan.PanBy(zoomed, -10000, -10000, 800, 600, 800, 600);

        Assert.Equal(0, rightLimit.OffsetX, 6);
        Assert.Equal(0, rightLimit.OffsetY, 6);
        Assert.Equal(-800, leftLimit.OffsetX, 6);
        Assert.Equal(-600, leftLimit.OffsetY, 6);
    }

    [Fact]
    public void PanBy_SmallerContent_DoesNotMove()
    {
        ZoomPanState state = ZoomPan.PanBy(new ZoomPanState(1, 0, 0), 50, 50, 800, 600, 400, 300);

        Assert.Equal(200, state.OffsetX, 6);
        Assert.Equal(150, state.OffsetY, 6);
    }

    [Fact]
    public void Reset_IsCenteredAtMinimumZoom()
    {
        ZoomPanState state = ZoomPan.Clamp(ZoomPan.Reset, 800, 600, 400, 300);

        Assert.Equal(ZoomPan.MinZoom, state.Zoom, 6);
        Assert.Equal(200, state.OffsetX, 6);
        Assert.Equal(150, state.OffsetY, 6);
    }

    [Theory]
    [InlineData(120, 1.2)]
    [InlineData(-120, 1.0 / 1.2)]
    [InlineData(0, 1.0)]
    public void WheelFactor_FollowsDirection(int delta, double expected)
    {
        Assert.Equal(expected, ZoomPan.WheelFactor(delta), 9);
    }
}
