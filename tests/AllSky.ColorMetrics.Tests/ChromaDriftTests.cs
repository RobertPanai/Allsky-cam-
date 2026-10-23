using Xunit;

namespace AllSky.ColorMetrics.Tests;

public sealed class ChromaDriftTests
{
    [Fact]
    public void RampaLineareNota_PendenzaEscursioneEResiduo()
    {
        // R/G = 1.00 + 0.02 * ore, un frame al minuto per 600 frame; B/G costante a 0.90.
        // Pendenza attesa 0.02 /h; escursione attesa 0.02 * 599/60 = 0.199667; residuo nullo (rampa esatta).
        var frames = new List<FrameSample>();
        for (int i = 0; i < 600; i++)
        {
            double rg = 1.0 + 0.02 * (i / 60.0);
            frames.Add(ColorTestData.Sample(0.3, 0.5 * rg, 0.5, 0.5 * 0.9, i));
        }

        ChromaDrift.ChromaDriftResult r = ChromaDrift.Measure(frames);
        Assert.Equal(0.02, r.RG.SlopePerHour!.Value, 1e-9);
        Assert.Equal(0.02 * 599 / 60.0, r.RG.Excursion!.Value, 1e-9);
        Assert.Equal(0.0, r.RG.ResidualStd!.Value, 1e-9);
        Assert.Equal(0.0, r.BG.Excursion!.Value, 1e-12);
        Assert.Equal(0.0, r.BG.SlopePerHour!.Value, 1e-12);
    }

    [Fact]
    public void PendenzaSulTempoReale_NonSullIndice()
    {
        // Frame a intervalli irregolari (60 s e 85 s alternati). La deriva è lineare nel tempo reale: la pendenza
        // deve restare 0.02 /h. Un calcolo sull'indice darebbe un valore diverso.
        var frames = new List<FrameSample>();
        double seconds = 0;
        for (int i = 0; i < 400; i++)
        {
            seconds += i % 2 == 0 ? 60 : 85;
            double hours = seconds / 3600.0;
            double rg = 1.0 + 0.02 * hours;
            frames.Add(new FrameSample
            {
                TimeUtc = ColorTestData.T0.AddSeconds(seconds),
                MeanLuminance = 0.3,
                SkyR = 0.5 * rg,
                SkyG = 0.5,
                SkyB = 0.45,
                Mode = "night",
            });
        }

        ChromaDrift.ChromaDriftResult r = ChromaDrift.Measure(frames);
        Assert.Equal(0.02, r.RG.SlopePerHour!.Value, 1e-9);
    }

    [Fact]
    public void FrameConGVerde_EsclusiDalCalcolo()
    {
        var frames = new List<FrameSample>
        {
            ColorTestData.Sample(0.3, 0.5, 0.0, 0.5, 0),
            ColorTestData.Sample(0.3, 0.5, 0.5, 0.5, 1),
            ColorTestData.Sample(0.3, 0.5, 0.5, 0.5, 2),
        };
        ChromaDrift.ChromaDriftResult r = ChromaDrift.Measure(frames);
        Assert.Equal(2, r.UsedFrames);
    }
}
