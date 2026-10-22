using AllSky.Sky;
using Xunit;
using AllSky.Nina;

namespace AllSky.Nina.Tests;

public sealed class TargetMarkerTests
{
    // Frame di prova 1000x800. Il cerchio fisheye sta tutto dentro il frame: centro (500, 400), raggio di scala 400 px.
    private const int W = 1000;
    private const int H = 800;
    private const double Lat = 0.0;   // equatore: formule semplici da verificare a mano
    private const double Lon = 9.0;

    private static readonly DateTimeOffset Now = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private static FisheyeModel Modello(double cx = 500, double cy = 400) =>
        new(cx, cy, 400.0, 0.0, false, 0.0);

    /// <summary>RA che mette il soggetto sul meridiano locale in questo istante (angolo orario 0).</summary>
    private static double RaAlMeridiano(DateTimeOffset utc, double lonDeg) =>
        EquatorialToHorizontal.LocalMeanSiderealDeg(utc, lonDeg);

    private static TargetMarkerResult Calcola(
        FisheyeModel? model, double? ra, double? dec,
        DateTimeOffset? letturaUtc = null, TimeSpan? eta = null, double lat = Lat, double lon = Lon,
        int w = W, int h = H)
    {
        return TargetMarker.Compute(
            model, w, h, ra, dec,
            letturaUtc ?? Now, Now, eta ?? MaxAge, lat, lon);
    }

    [Fact]
    public void Zenit_CadeAlCentroDelCerchio()
    {
        // Dec = latitudine e RA sul meridiano: altezza 90 gradi.
        double ra = RaAlMeridiano(Now, Lon);
        TargetMarkerResult r = Calcola(Modello(), ra, Lat);

        Assert.Equal(TargetMarkerStatus.Visible, r.Status);
        Assert.NotNull(r.Pixel);
        Assert.Equal(500.0, r.Pixel!.Value.X, 1e-6);
        Assert.Equal(400.0, r.Pixel!.Value.Y, 1e-6);
        Assert.Equal(90.0, r.Horizontal!.Value.AltitudeDeg, 1e-6);
    }

    [Fact]
    public void Altezza30NelNord_CadeSopraIlCentro()
    {
        // Equatore, H = 0, Dec = 60: altezza 30 gradi, azimut 0 (nord). Distanza zenitale 60, r = 400 * 60/90.
        double ra = RaAlMeridiano(Now, Lon);
        TargetMarkerResult r = Calcola(Modello(), ra, 60.0);

        Assert.Equal(TargetMarkerStatus.Visible, r.Status);
        Assert.Equal(30.0, r.Horizontal!.Value.AltitudeDeg, 1e-6);
        Assert.Equal(500.0, r.Pixel!.Value.X, 1e-3);
        Assert.Equal(400.0 - 400.0 * 60.0 / 90.0, r.Pixel!.Value.Y, 1e-3);
    }

    [Fact]
    public void Est_CadeASinistraConLaConvenzioneDelModello()
    {
        // Equatore, H = -30 gradi (a est del meridiano): altezza 60, azimut 90 (est), x = centro - r.
        double ra = RaAlMeridiano(Now, Lon) + 30.0;
        TargetMarkerResult r = Calcola(Modello(), ra, 0.0);

        Assert.Equal(TargetMarkerStatus.Visible, r.Status);
        Assert.Equal(60.0, r.Horizontal!.Value.AltitudeDeg, 1e-6);
        Assert.Equal(90.0, r.Horizontal!.Value.AzimuthDeg, 1e-6);
        Assert.Equal(500.0 - 400.0 / 3.0, r.Pixel!.Value.X, 1e-3);
        Assert.Equal(400.0, r.Pixel!.Value.Y, 1e-3);
    }

    [Fact]
    public void SottoLOrizzonte_RestituisceBelowHorizon()
    {
        // Equatore (lat 0), dec 30, angolo orario 180 (culmine inferiore): sin(h) = -cos(dec) -> altezza -60.
        // Al meridiano superiore, con lat 0, un oggetto sta sempre sopra l'orizzonte: il caso va costruito qui.
        double ra = RaAlMeridiano(Now, Lon) + 180.0;
        TargetMarkerResult r = Calcola(Modello(), ra, 30.0);

        Assert.Equal(TargetMarkerStatus.BelowHorizon, r.Status);
        Assert.Null(r.Pixel);
        Assert.Equal(-60.0, r.Horizontal!.Value.AltitudeDeg, 1e-6);
    }

    [Fact]
    public void SulBordoDellOrizzonte_Visibile_OppureBelowHorizon()
    {
        // Caso al bordo: lat 0, dec 0, angolo orario 90 gradi -> altezza 0, azimut 270 (ovest).
        double ra = RaAlMeridiano(Now, Lon) - 90.0;
        TargetMarkerResult r = Calcola(Modello(), ra, 0.0);

        Assert.Equal(0.0, r.Horizontal!.Value.AltitudeDeg, 1e-6);
        // Il bordo cade sul cerchio di raggio HorizonRadius (400 px). Tollerante: BelowHorizon se l'arrotondamento e' negativo.
        Assert.True(r.Status is TargetMarkerStatus.Visible or TargetMarkerStatus.BelowHorizon);
        if (r.Status == TargetMarkerStatus.Visible)
        {
            double dx = r.Pixel!.Value.X - 500.0;
            double dy = r.Pixel!.Value.Y - 400.0;
            Assert.Equal(400.0, Math.Sqrt(dx * dx + dy * dy), 1e-6);
        }
    }

    [Fact]
    public void FuoriDallImmagine_RestituisceOutsideCircle()
    {
        // Cerchio centrato in (100, 100) con raggio 400: il punto a 30 gradi nel nord (y = -166.7) esce dal frame.
        double ra = RaAlMeridiano(Now, Lon);
        TargetMarkerResult r = Calcola(Modello(100, 100), ra, 60.0);

        Assert.Equal(TargetMarkerStatus.OutsideCircle, r.Status);
        Assert.Null(r.Pixel);
        Assert.NotNull(r.Horizontal);
    }

    [Fact]
    public void NessunaCalibrazione_RestituisceNoCalibration_SenzaEccezione()
    {
        double ra = RaAlMeridiano(Now, Lon);
        TargetMarkerResult r = Calcola(null, ra, Lat);

        Assert.Equal(TargetMarkerStatus.NoCalibration, r.Status);
        Assert.Null(r.Pixel);
        Assert.Null(r.Horizontal);
    }

    [Fact]
    public void TargetAssente_RestituisceNoTarget()
    {
        Assert.Equal(TargetMarkerStatus.NoTarget, Calcola(Modello(), null, Lat).Status);
        Assert.Equal(TargetMarkerStatus.NoTarget, Calcola(Modello(), 10.0, null).Status);
        Assert.Equal(TargetMarkerStatus.NoTarget, Calcola(Modello(), double.NaN, 10.0).Status);
    }

    [Fact]
    public void CoordinateVecchie_RestituisconoStale_ConSogliaParametro()
    {
        double ra = RaAlMeridiano(Now, Lon);
        DateTimeOffset dieciMinutiFa = Now.AddMinutes(-10);

        TargetMarkerResult troppoVecchie = Calcola(Modello(), ra, Lat, dieciMinutiFa, TimeSpan.FromMinutes(5));
        Assert.Equal(TargetMarkerStatus.StaleCoordinates, troppoVecchie.Status);
        Assert.Null(troppoVecchie.Pixel);

        TargetMarkerResult accettate = Calcola(Modello(), ra, Lat, dieciMinutiFa, TimeSpan.FromMinutes(15));
        Assert.Equal(TargetMarkerStatus.Visible, accettate.Status);
    }

    [Fact]
    public void CoordinateConIstanteFuturo_SonoTrattateComeStale()
    {
        // Orologio di NINA avanti rispetto al PC: lettura nel futuro.
        double ra = RaAlMeridiano(Now, Lon);
        TargetMarkerResult r = Calcola(Modello(), ra, Lat, Now.AddMinutes(1));
        Assert.Equal(TargetMarkerStatus.StaleCoordinates, r.Status);
    }

    [Fact]
    public void FrameNonValido_LanciaArgomentoFuoriIntervallo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TargetMarker.Compute(Modello(), 0, H, 1.0, 1.0, Now, Now, MaxAge, Lat, Lon));
    }

    [Fact]
    public void TempoSidereo_MeeusEsempio12a()
    {
        // Meeus, "Astronomical Algorithms", esempio 12.a: 1987 aprile 10, 0h UT, theta0 = 13h 10m 46.3668s = 197.693195 gradi.
        var istante = new DateTimeOffset(1987, 4, 10, 0, 0, 0, TimeSpan.Zero);
        double gmst = EquatorialToHorizontal.LocalMeanSiderealDeg(istante, 0.0);
        Assert.Equal(197.693195, gmst, 1e-4);
    }

    [Fact]
    public void TempoSidereoLocale_SommaLaLongitudineEst()
    {
        var istante = new DateTimeOffset(1987, 4, 10, 0, 0, 0, TimeSpan.Zero);
        double a = EquatorialToHorizontal.LocalMeanSiderealDeg(istante, 0.0);
        double b = EquatorialToHorizontal.LocalMeanSiderealDeg(istante, 15.0);
        Assert.Equal(15.0, (b - a + 360.0) % 360.0, 1e-9);
    }
}
