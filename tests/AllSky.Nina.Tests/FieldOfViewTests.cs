using AllSky.Astro;
using AllSky.Sky;
using Xunit;
using AllSky.Nina;

namespace AllSky.Nina.Tests;

public sealed class FieldOfViewTests
{
    // Frame 1000x800, cerchio fisheye centrato in (500, 400), raggio di scala 400 px, equidistante (K2 = 0).
    private const int W = 1000;
    private const int H = 800;

    private static FisheyeModel Modello(double cx = 500, double cy = 400) =>
        new(cx, cy, 400.0, 0.0, false, 0.0);

    // Dati di prova: focale 1000 mm, sensore 23.5 x 15.6 mm (valori di esempio, non verificati su NINA).
    private static FovInput Input(double? focal = 1000, double? sw = 23.5, double? sh = 15.6, double? rot = null) =>
        new(focal, sw, sh, PixelSizeUm: 3.76, RotatorAngleDeg: rot);

    private static HorizontalPosition Pos(double alt, double az) => new() { AltitudeDeg = alt, AzimuthDeg = az };

    private static FieldOfViewResult Campo(FovInput input, HorizontalPosition? centro, FisheyeModel? model) =>
        FieldOfView.Compute(input, centro, model, W, H);

    [Fact]
    public void AngoloDaFocaleESensore_CorrispondeAlFormulaAttesa()
    {
        // 2 atan(larghezza / (2 f)): 2 atan(11.75 / 1000) = 1.34639 gradi; 2 atan(7.8 / 1000) = 0.89380 gradi.
        FieldOfViewResult r = Campo(Input(), Pos(60, 0), Modello());

        Assert.Equal(FieldOfViewStatus.Ok, r.Status);
        Assert.Equal(2.0 * Math.Atan(11.75 / 1000.0) * 180.0 / Math.PI, r.FovWidthDeg!.Value, 1e-9);
        Assert.Equal(2.0 * Math.Atan(7.8 / 1000.0) * 180.0 / Math.PI, r.FovHeightDeg!.Value, 1e-9);
        Assert.Equal(1.34639, r.FovWidthDeg!.Value, 1e-4);
        Assert.Equal(0.89380, r.FovHeightDeg!.Value, 1e-4);
    }

    [Fact]
    public void SpigoliAloZenit_StannoAllaDistanzaAttesa()
    {
        // Centro allo zenit: uno spigolo a coordinate del piano focale (u, v) sta a zenith = atan(hypot(u, v)),
        // e con K2 = 0 il raggio in pixel e' ScaleRadiusPx * z / 90.
        FieldOfViewResult r = Campo(Input(), Pos(90, 0), Modello());

        Assert.Equal(FieldOfViewStatus.Ok, r.Status);
        Assert.Equal(4, r.Corners.Count);
        Assert.Equal(4 * FieldOfView.SamplesPerSide, r.Outline.Count);

        double u = 11.75 / 1000.0;
        double v = 7.8 / 1000.0;
        double zenithDeg = Math.Atan(Math.Sqrt(u * u + v * v)) * 180.0 / Math.PI;
        double expectedRadius = 400.0 * zenithDeg / 90.0;
        foreach (PixelPosition c in r.Corners)
        {
            double d = Math.Sqrt((c.X - 500.0) * (c.X - 500.0) + (c.Y - 400.0) * (c.Y - 400.0));
            Assert.Equal(expectedRadius, d, 1e-6);
        }
    }

    [Fact]
    public void Rotatore_RuotaIlLatoSuperiore_EMisuratoSuiPixel()
    {
        // Zenit, azimut 0, rotatore 20 gradi: il lato TL->TR punta a azimut 110 gradi, sull'immagine e' ruotato di -20 gradi
        // (convenzione del modello: il Nord e' in alto, l'Est a sinistra). Convenzione del rotatore [NV].
        FieldOfViewResult r = Campo(Input(rot: 20), Pos(90, 0), Modello());

        Assert.Equal(FieldOfViewStatus.Ok, r.Status);
        Assert.Equal(-20.0, r.Rotation!.Value, 1e-6);
        Assert.False(r.RotatorAssumed);
    }

    [Fact]
    public void RotatoreZero_LatoSuperiorePiatto()
    {
        FieldOfViewResult r = Campo(Input(rot: 0), Pos(90, 0), Modello());

        Assert.Equal(0.0, r.Rotation!.Value, 1e-6);
        Assert.False(r.RotatorAssumed);
    }

    [Fact]
    public void RotatoreAssente_DichiaraRotazioneAssunta()
    {
        FieldOfViewResult r = Campo(Input(rot: null), Pos(60, 0), Modello());

        Assert.Equal(FieldOfViewStatus.Ok, r.Status);
        Assert.True(r.RotatorAssumed);
    }

    [Fact]
    public void FocaleMancante_RestituisceMissingInput()
    {
        FieldOfViewResult r = Campo(Input(focal: null), Pos(60, 0), Modello());

        Assert.Equal(FieldOfViewStatus.MissingInput, r.Status);
        Assert.Empty(r.Corners);
        Assert.Empty(r.Outline);
        Assert.Null(r.FovWidthDeg);
    }

    [Fact]
    public void SensoreMancanteONullo_RestituisceMissingInput()
    {
        Assert.Equal(FieldOfViewStatus.MissingInput, Campo(Input(sw: null), Pos(60, 0), Modello()).Status);
        Assert.Equal(FieldOfViewStatus.MissingInput, Campo(Input(sh: 0), Pos(60, 0), Modello()).Status);
        Assert.Equal(FieldOfViewStatus.MissingInput, Campo(Input(focal: double.NaN), Pos(60, 0), Modello()).Status);
    }

    [Fact]
    public void PixelSizeNonValida_RestituisceMissingInput()
    {
        FovInput input = new(1000, 23.5, 15.6, PixelSizeUm: -3.76);

        Assert.Equal(FieldOfViewStatus.MissingInput, Campo(input, Pos(60, 0), Modello()).Status);
    }

    [Fact]
    public void CentroMancante_RestituisceMissingInput()
    {
        Assert.Equal(FieldOfViewStatus.MissingInput, Campo(Input(), null, Modello()).Status);
    }

    [Fact]
    public void CentroFuoriFrame_RestituisceOutsideFrame()
    {
        // Cerchio spostato a x = -500: lo zenit cade fuori dal frame.
        FieldOfViewResult r = Campo(Input(), Pos(90, 0), Modello(-500, 400));

        Assert.Equal(FieldOfViewStatus.OutsideFrame, r.Status);
        Assert.Empty(r.Corners);
    }

    [Fact]
    public void CentroSottoLOrizzonte_RestituisceOutsideFrame()
    {
        FieldOfViewResult r = Campo(Input(), Pos(-5, 0), Modello());

        Assert.Equal(FieldOfViewStatus.OutsideFrame, r.Status);
    }

    [Fact]
    public void NessunaCalibrazione_RestituisceNoCalibration()
    {
        FieldOfViewResult r = Campo(Input(), Pos(60, 0), null);

        Assert.Equal(FieldOfViewStatus.NoCalibration, r.Status);
        Assert.Empty(r.Corners);
    }

    [Fact]
    public void CampoVicinoALOrizzonte_PortaIPuntiSottoLOrizzonteSulBordo()
    {
        // Obiettivo corto e sensore grande: campo di circa 36 x 24 gradi, centro a 5 gradi di altezza.
        FovInput grande = new(50, 36, 24);
        FisheyeModel modello = Modello();

        FieldOfViewResult r = Campo(grande, Pos(5, 0), modello);

        Assert.Equal(FieldOfViewStatus.Ok, r.Status);
        Assert.True(r.ClippedAtHorizon);
        foreach (PixelPosition p in r.Outline)
        {
            Assert.True(modello.IsInsideSky(p.X, p.Y));
        }
    }

    [Fact]
    public void Allineamento_SenzaPuntoDelTelescopio_NotMeasured()
    {
        AlignmentCheck a = FieldOfView.CheckAlignment(Modello(), Pos(60, 0), null, 1.0);

        Assert.Equal(AlignmentStatus.NotMeasured, a.Status);
        Assert.Null(a.OffsetPx);
        Assert.Null(a.OffsetDeg);
        Assert.Null(a.ExceedsThreshold);
    }

    [Fact]
    public void Allineamento_SenzaCalibrazione_NotMeasured()
    {
        AlignmentCheck a = FieldOfView.CheckAlignment(null, Pos(60, 0), new PixelPosition(500, 256.67), 1.0);

        Assert.Equal(AlignmentStatus.NotMeasured, a.Status);
    }

    [Fact]
    public void Allineamento_ScartoNoto_RestituisceValoriAttesi()
    {
        // Puntato: alt 60, az 0 -> pixel (500, 266.67). Telescopio 10 px piu' in alto: r = 143.33, zenit 32.25, alt 57.75.
        // Equidistante: 10 px = 400/90 px per grado -> 2.25 gradi, che coincidono con la differenza di altezza.
        AlignmentCheck a = FieldOfView.CheckAlignment(Modello(), Pos(60, 0), new PixelPosition(500, 256.6667), null);

        Assert.Equal(AlignmentStatus.Measured, a.Status);
        Assert.Equal(10.0, a.OffsetPx!.Value, 1e-3);
        Assert.Equal(2.25, a.OffsetDeg!.Value, 1e-3);
    }

    [Fact]
    public void Allineamento_SogliaNull_NessunAllarme()
    {
        AlignmentCheck a = FieldOfView.CheckAlignment(Modello(), Pos(60, 0), new PixelPosition(500, 256.6667), null);

        Assert.Null(a.ExceedsThreshold);
    }

    [Fact]
    public void Allineamento_SogliaSuperata_ExceedsThresholdTrue()
    {
        AlignmentCheck a = FieldOfView.CheckAlignment(Modello(), Pos(60, 0), new PixelPosition(500, 256.6667), 1.0);

        Assert.True(a.ExceedsThreshold!.Value);
    }

    [Fact]
    public void Allineamento_SogliaNonSuperata_ExceedsThresholdFalse()
    {
        AlignmentCheck a = FieldOfView.CheckAlignment(Modello(), Pos(60, 0), new PixelPosition(500, 256.6667), 5.0);

        Assert.False(a.ExceedsThreshold!.Value);
    }

    [Fact]
    public void Allineamento_TelescopioFuoriCielo_OffsetDegNullESogliaNull()
    {
        // Angolo (0, 0) del frame: a 583 px dal centro, fuori dal cerchio dell'orizzonte (raggio 400).
        AlignmentCheck a = FieldOfView.CheckAlignment(Modello(), Pos(60, 0), new PixelPosition(0, 0), 1.0);

        Assert.Equal(AlignmentStatus.Measured, a.Status);
        Assert.True(a.OffsetPx!.Value > 0.0);
        Assert.Null(a.OffsetDeg);
        Assert.Null(a.ExceedsThreshold);
    }

    [Fact]
    public void Allineamento_SogliaNegativa_Eccezione()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FieldOfView.CheckAlignment(Modello(), Pos(60, 0), new PixelPosition(500, 256.6667), -1.0));
    }
}
