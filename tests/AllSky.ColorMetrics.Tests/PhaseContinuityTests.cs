using Xunit;

namespace AllSky.ColorMetrics.Tests;

public sealed class PhaseContinuityTests
{
    /// <summary>
    /// 20 frame al minuto. Luminanza, R/G e B/G crescono linearmente dentro ogni fase (passo 0.001 per frame).
    /// Dal frame 10 la fase cambia da "day" a "twilight" e, se <paramref name="jump"/> è vero, i tre valori saltano.
    /// </summary>
    private static List<FrameSample> Night(bool jump, double lumaJump = 0.05, double rgJump = 0.1, double bgJump = 0.04)
    {
        var list = new List<FrameSample>();
        for (int k = 0; k < 20; k++)
        {
            bool after = k >= 10;
            double j = jump && after ? 1 : 0;
            list.Add(new FrameSample
            {
                TimeUtc = ColorTestData.T0.AddMinutes(k),
                MeanLuminance = 0.3 + 0.001 * k + j * lumaJump,
                SkyR = 0.5 * (1.0 + 0.002 * k + j * rgJump),
                SkyG = 0.5,
                SkyB = 0.5 * (0.8 + 0.001 * k + j * bgJump),
                Mode = after ? "twilight" : "day",
            });
        }

        return list;
    }

    [Fact]
    public void SaltoNetto_RapportoAttesoSalto_piu_5Passi_fratto_passoTipico()
    {
        // Mediane di 5 frame: prima = valore al frame 7, dopo = valore al frame 12 (distanza 5 frame).
        // Salto luminanza = 0.05 + 5 * 0.001 = 0.055; tipico = 0.001 => rapporto 55.
        // R/G: 0.1 + 5 * 0.002 = 0.11, tipico 0.002 => 55. B/G: 0.04 + 5 * 0.001 = 0.045, tipico 0.001 => 45.
        PhaseContinuityResult r = PhaseContinuity.Measure(Night(jump: true));
        Assert.Single(r.Boundaries);
        PhaseBoundary b = r.Boundaries[0];
        Assert.Equal("day", b.From);
        Assert.Equal("twilight", b.To);
        Assert.Equal(0.001, r.TypicalLumaStep!.Value, 1e-9);
        Assert.Equal(55.0, b.LumaRatio!.Value, 1e-6);
        Assert.Equal(55.0, b.RgRatio!.Value, 1e-6);
        Assert.Equal(45.0, b.BgRatio!.Value, 1e-6);
    }

    [Fact]
    public void RampaLisciaSenzaSalto_RapportoCinque()
    {
        // Senza salto il rapporto vale 5 (le due mediane distano 5 frame): è la base di riferimento.
        PhaseContinuityResult r = PhaseContinuity.Measure(Night(jump: false));
        PhaseBoundary b = Assert.Single(r.Boundaries);
        Assert.Equal(5.0, b.LumaRatio!.Value, 1e-6);
        Assert.Equal(5.0, b.RgRatio!.Value, 1e-6);
        Assert.Equal(5.0, b.BgRatio!.Value, 1e-6);
    }

    [Fact]
    public void NessunCambioDiFase_NessunConfine()
    {
        var frames = Night(jump: false).Select(f => f with { Mode = "night" }).ToList();
        Assert.Empty(PhaseContinuity.Measure(frames).Boundaries);
    }
}
