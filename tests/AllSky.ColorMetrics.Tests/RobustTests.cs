using Xunit;

namespace AllSky.ColorMetrics.Tests;

public sealed class RobustTests
{
    [Fact]
    public void Mediana_NumeroPari_MediaDeiDueCentrali()
    {
        Assert.Equal(2.5, Robust.Median([4, 1, 2, 3]), 1e-12);
    }

    [Fact]
    public void MedianaMobile_RampaLineare_ResiduoZeroAncheAiBordi()
    {
        // Con finestra simmetrica ai bordi, una rampa lineare è esatta: la mediana coincide con il centro.
        double[] ramp = Enumerable.Range(0, 50).Select(i => 0.01 * i).ToArray();
        double[] trend = Robust.CenteredMedian(ramp, 15);
        for (int i = 0; i < ramp.Length; i++)
        {
            Assert.Equal(ramp[i], trend[i], 1e-12);
        }
    }

    [Fact]
    public void Pendenza_SenzaVarianzaInX_Null()
    {
        Assert.Null(Robust.Slope([1, 1, 1], [1, 2, 3]));
    }

    [Fact]
    public void DeviazioneStandard_UnSoloValore_Null()
    {
        Assert.Null(Robust.SampleStd([3.0]));
    }
}
