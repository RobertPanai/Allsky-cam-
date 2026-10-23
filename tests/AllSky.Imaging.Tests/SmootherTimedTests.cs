using AllSky.Imaging;
using Xunit;

namespace AllSky.Imaging.Tests;

/// <summary>Alpha per chiamata e riscalatura dello stato (P14.2). Il comportamento con l'alpha fisso e' coperto dagli altri test.</summary>
public class SmootherTimedTests
{
    [Fact]
    public void Smoother_UpdateWithAlpha_UsesThatAlphaOnlyForThatCall()
    {
        var s = new Smoother(0.5);
        Assert.Equal((10.0, 10.0, 10.0), s.Update((10.0, 10.0, 10.0)));

        // Con alpha 0.25: 0.25 * 20 + 0.75 * 10 = 12.5.
        var a = s.Update((20.0, 20.0, 20.0), 0.25);
        Assert.Equal(12.5, a.R, 9);

        // Senza alpha torna quello dello smoother (0.5): 0.5 * 20 + 0.5 * 12.5 = 16.25.
        var b = s.Update((20.0, 20.0, 20.0));
        Assert.Equal(16.25, b.R, 9);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Smoother_UpdateWithAlpha_RejectsOutOfRange(double alpha)
    {
        var s = new Smoother(0.5);
        s.Update((1.0, 1.0, 1.0));

        Assert.Throws<ArgumentOutOfRangeException>(() => s.Update((2.0, 2.0, 2.0), alpha));
    }

    [Fact]
    public void Smoother_AlphaOne_ReturnsTheNewValue()
    {
        var s = new Smoother(0.1);
        s.Update((1.0, 2.0, 3.0));

        Assert.Equal((5.0, 6.0, 7.0), s.Update((5.0, 6.0, 7.0), 1.0));
    }

    [Fact]
    public void StretchParamsSmoother_UpdateWithAlpha_MixesEveryField()
    {
        var s = new StretchParamsSmoother(0.5);
        s.Update(new StretchParams(100f, 100f, 100f, 1000f, 0.5f));

        // alpha 0.25: nero 0.25*200 + 0.75*100 = 125; bianco 0.25*2000 + 0.75*1000 = 1250; midtone 0.25*0.6 + 0.75*0.5 = 0.525.
        StretchParams p = s.Update(new StretchParams(200f, 200f, 200f, 2000f, 0.6f), 0.25);

        Assert.Equal(125f, p.BlackR, 3);
        Assert.Equal(125f, p.BlackG, 3);
        Assert.Equal(125f, p.BlackB, 3);
        Assert.Equal(1250f, p.White, 3);
        Assert.Equal(0.525f, p.Midtone, 5);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    public void StretchParamsSmoother_UpdateWithAlpha_RejectsOutOfRange(double alpha)
    {
        var s = new StretchParamsSmoother(0.5);
        s.Update(new StretchParams(1f, 1f, 1f, 100f, 0.5f));

        Assert.Throws<ArgumentOutOfRangeException>(() => s.Update(new StretchParams(1f, 1f, 1f, 100f, 0.5f), alpha));
    }

    [Fact]
    public void Rescale_ScalesBlackAndWhite_AndKeepsMidtone()
    {
        var s = new StretchParamsSmoother(0.15);
        s.Update(new StretchParams(100f, 110f, 120f, 1000f, 0.3f));

        s.Rescale(2.0);

        // Il prossimo aggiornamento con gli stessi dati riscalati non deve cambiare nulla (nessun salto).
        StretchParams p = s.Update(new StretchParams(200f, 220f, 240f, 2000f, 0.3f));
        Assert.Equal(200f, p.BlackR, 3);
        Assert.Equal(220f, p.BlackG, 3);
        Assert.Equal(240f, p.BlackB, 3);
        Assert.Equal(2000f, p.White, 3);
        Assert.Equal(0.3f, p.Midtone, 5);
    }

    [Fact]
    public void Rescale_ThenUpdate_ReproducesRescaledStateExactly_WhenInputsAgree()
    {
        // Segnale che raddoppia (esposizione x2) con scena costante: senza riscalatura la media mescola scale diverse.
        var s = new StretchParamsSmoother(0.15);
        s.Update(new StretchParams(300f, 300f, 300f, 4000f, 0.4f));
        s.Rescale(2.0);

        StretchParams p = s.Update(new StretchParams(600f, 600f, 600f, 8000f, 0.4f));

        Assert.Equal(600f, p.BlackR, 3);
        Assert.Equal(8000f, p.White, 3);
    }

    [Fact]
    public void Rescale_Upwards_ClampsToFullScale()
    {
        var s = new StretchParamsSmoother(0.5);
        s.Update(new StretchParams(1000f, 1000f, 1000f, 60000f, 0.5f));

        s.Rescale(100.0);

        StretchParams p = s.Update(new StretchParams(65534f, 65534f, 65534f, 65535f, 0.5f));
        Assert.True(p.White <= 65535f + 1e-3f, $"Bianco oltre 65535: {p.White}.");
        Assert.True(p.BlackR >= 0f && p.BlackR <= 65534f + 1e-3f, $"Nero fuori 0..65534: {p.BlackR}.");
        Assert.True(p.White - p.BlackR >= 1f - 1e-3f, $"White - Black = {p.White - p.BlackR}.");
    }

    [Fact]
    public void Rescale_Downwards_KeepsWhiteMinusBlackAtLeastOne()
    {
        // Bianco e nero vicini: riscalando per 0.01 la differenza andrebbe sotto 1, quindi il bianco si alza.
        var s = new StretchParamsSmoother(0.5);
        s.Update(new StretchParams(500f, 500f, 500f, 510f, 0.5f));

        s.Rescale(0.01);

        // Il prossimo update con parametri validi restituisce parametri validi (il vincolo W - B >= 1 regge).
        StretchParams p = s.Update(new StretchParams(5f, 5f, 5f, 6f, 0.5f));
        Assert.True(p.White - p.BlackR >= 1f - 1e-4f, $"White - Black = {p.White - p.BlackR}.");
        Assert.True(p.White - p.BlackG >= 1f - 1e-4f);
        Assert.True(p.White - p.BlackB >= 1f - 1e-4f);
    }

    [Fact]
    public void Rescale_WithoutState_IsNoOp()
    {
        var s = new StretchParamsSmoother(0.5);
        s.Rescale(3.0);

        StretchParams p = s.Update(new StretchParams(10f, 10f, 10f, 500f, 0.5f));

        Assert.Equal(10f, p.BlackR);
        Assert.Equal(500f, p.White);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Rescale_InvalidRatio_Throws(double ratio)
    {
        var s = new StretchParamsSmoother(0.5);
        s.Update(new StretchParams(10f, 10f, 10f, 500f, 0.5f));

        Assert.Throws<ArgumentOutOfRangeException>(() => s.Rescale(ratio));
    }

    [Fact]
    public void Rescale_ByOne_LeavesStateUnchanged()
    {
        var s = new StretchParamsSmoother(0.5);
        s.Update(new StretchParams(100f, 100f, 100f, 1000f, 0.5f));
        s.Rescale(1.0);

        StretchParams p = s.Update(new StretchParams(100f, 100f, 100f, 1000f, 0.5f));
        Assert.Equal(100f, p.BlackR, 3);
        Assert.Equal(1000f, p.White, 3);
        Assert.Equal(0.5f, p.Midtone, 5);
    }
}
