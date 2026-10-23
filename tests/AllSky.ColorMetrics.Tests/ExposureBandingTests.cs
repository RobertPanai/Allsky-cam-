using Xunit;

namespace AllSky.ColorMetrics.Tests;

public sealed class ExposureBandingTests
{
    [Fact]
    public void GradinoNoto_AiCambiDiEsposizioneEGuadagno_RapportoAtteso()
    {
        // Luminanza in rampa dello 0.1 % per frame. Al frame 20 l'esposizione cambia e la luminanza sale del 10 %;
        // al frame 40 cambia il guadagno e sale di nuovo del 10 %. Salto relativo ai cambi ~ 0.101;
        // salto negli altri passi ~ 0.001 (rampa). Rapporto atteso ~ 100.
        var frames = new List<FrameSample>();
        for (int k = 0; k < 60; k++)
        {
            double lum = 0.4 * (1 + 0.001 * k) * (k >= 20 ? 1.1 : 1.0) * (k >= 40 ? 1.1 : 1.0);
            double exposure = k < 20 ? 2 : 10;
            double gain = k < 40 ? 250 : 500;
            frames.Add(ColorTestData.Sample(lum, 0.2, 0.4, 0.6, k, "night", exposure, gain));
        }

        ExposureBandingResult r = ExposureBanding.Measure(frames);
        Assert.Equal(2, r.Changes);
        Assert.Equal(0, r.ChangesAtPhaseChange);
        Assert.InRange(r.MeanAbsRelJumpAtChanges!.Value, 0.100, 0.102);
        Assert.InRange(r.Ratio!.Value, 100.0, 110.0);
        Assert.InRange(r.ChangeList[0].RelativeJump, 0.100, 0.102);
        Assert.Equal(2.0, r.ChangeList[0].FromExposureMs);
        Assert.Equal(10.0, r.ChangeList[0].ToExposureMs);
        Assert.Equal(500.0, r.ChangeList[1].ToGain);
    }

    [Fact]
    public void SenzaCambi_RapportoNonCalcolabile()
    {
        var frames = Enumerable.Range(0, 30)
            .Select(k => ColorTestData.Sample(0.4 * (1 + 0.001 * k), 0.2, 0.4, 0.6, k))
            .ToList();
        ExposureBandingResult r = ExposureBanding.Measure(frames);
        Assert.Equal(0, r.Changes);
        Assert.Null(r.Ratio);
    }

    [Fact]
    public void CambioCoincidenteConFase_Conteggiato()
    {
        var frames = new List<FrameSample>();
        for (int k = 0; k < 30; k++)
        {
            frames.Add(ColorTestData.Sample(0.4 * (1 + 0.001 * k), 0.2, 0.4, 0.6, k, k < 15 ? "day" : "twilight", k < 15 ? 2 : 10));
        }

        ExposureBandingResult r = ExposureBanding.Measure(frames);
        Assert.Equal(1, r.Changes);
        Assert.Equal(1, r.ChangesAtPhaseChange);
    }
}
