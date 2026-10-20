using Xunit;

namespace AllSky.Imaging.Tests;

public class DarkSubtractTests
{
    private static MasterDark Dark(int width, int height, ushort value)
    {
        var data = new ushort[width * height];
        Array.Fill(data, value);
        return new MasterDark(width, height, data, 300.0, 120.0, 10.0);
    }

    [Fact]
    public void UniformDark_SubtractedFromUniformFrame_GivesPedestal()
    {
        // Frame e dark uguali ovunque: dopo la sottrazione ogni pixel vale 0, poi + pedestal.
        const int pedestal = 64;
        var frame = new ushort[6 * 4];
        Array.Fill(frame, (ushort)1000);

        DarkSubtract.Apply(frame, Dark(6, 4, 1000), pedestal);

        foreach (ushort v in frame)
        {
            Assert.Equal(pedestal, v);
        }
    }

    [Fact]
    public void NegativeDifference_ClampsToZeroBeforePedestal()
    {
        // frame 500 - dark 1000 < 0 -> 0, poi + pedestal 10 = 10.
        var frame = new ushort[] { 500 };

        DarkSubtract.Apply(frame, Dark(1, 1, 1000), 10);

        Assert.Equal(10, frame[0]);
    }

    [Fact]
    public void PedestalSum_SaturatesAt65535()
    {
        var frame = new ushort[] { 65535 };

        DarkSubtract.Apply(frame, Dark(1, 1, 0), 100);

        Assert.Equal(65535, frame[0]);
    }

    [Fact]
    public void SignalAboveDark_IsPreservedPlusPedestal()
    {
        var frame = new ushort[] { 5000, 1200 };
        var dark = new MasterDark(2, 1, new ushort[] { 1000, 1000 }, 300.0, 120.0, null);

        DarkSubtract.Apply(frame, dark, 50);

        Assert.Equal(4050, frame[0]);
        Assert.Equal(250, frame[1]);
    }

    [Fact]
    public void LengthMismatch_Throws()
    {
        var frame = new ushort[3];

        Assert.Throws<ArgumentException>(() =>
            DarkSubtract.Apply(frame, Dark(2, 2, 0), 0));
    }

    [Fact]
    public void PedestalOutOfRange_Throws()
    {
        var frame = new ushort[1];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DarkSubtract.Apply(frame, Dark(1, 1, 0), -1));
    }
}
