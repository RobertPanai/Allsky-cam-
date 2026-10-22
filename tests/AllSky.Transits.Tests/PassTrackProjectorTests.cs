using AllSky.Satellites;
using AllSky.Sky;
using AllSky.Sky.Calibration;
using AllSky.Transits;
using Xunit;

namespace AllSky.Transits.Tests;

public sealed class PassTrackProjectorTests
{
    private static readonly DateTimeOffset T0 = TransitFixtures.T0;

    [Fact]
    public void Alt_az_noti_cadono_nei_pixel_attesi()
    {
        // Alt 45 gradi: raggio 230 * 45/90 = 115 px dal centro (256, 256). Nord in alto, Est a sinistra.
        var fixedNorth = new ScriptedPropagator(T0, _ => (0.0, 45.0));
        var fixedEast = new ScriptedPropagator(T0, _ => (90.0, 45.0));
        SatellitePass pass = TransitFixtures.PassFor(fixedNorth);

        ProjectedPassTrack north = PassTrackProjector.Project(pass, fixedNorth, TransitFixtures.Site, TransitFixtures.AcceptedCalibration());
        ProjectedPassTrack east = PassTrackProjector.Project(TransitFixtures.PassFor(fixedEast), fixedEast, TransitFixtures.Site, TransitFixtures.AcceptedCalibration());

        Assert.True(north.Calibrated);
        Assert.Single(north.Runs);
        foreach (TrackPixel p in north.Runs[0].Points)
        {
            Assert.Equal(256.0, p.X, 6);
            Assert.Equal(141.0, p.Y, 6);
        }

        Assert.Equal(256.0 - 115.0, east.Runs[0].Points[0].X, 6);
        Assert.Equal(256.0, east.Runs[0].Points[0].Y, 6);
    }

    [Fact]
    public void Scarta_i_campioni_sotto_l_orizzonte()
    {
        // Sotto l'orizzonte per i primi 20 s, poi sopra: un solo tratto, che inizia a 20 s.
        var propagator = new ScriptedPropagator(T0, s => (45.0, s < 20.0 ? -5.0 : 30.0));
        SatellitePass pass = new(
            new PassPoint(T0, 45.0, 30.0), new PassPoint(T0.AddSeconds(40), 45.0, 30.0), new PassPoint(T0.AddSeconds(60), 45.0, 30.0),
            30.0, 1.0, false, false, false);

        ProjectedPassTrack track = PassTrackProjector.Project(pass, propagator, TransitFixtures.Site, TransitFixtures.AcceptedCalibration());

        Assert.Single(track.Runs);
        Assert.Equal(T0.AddSeconds(20), track.Runs[0].Points[0].Utc);
        Assert.Equal(T0.AddSeconds(60), track.Runs[0].Points[^1].Utc);
    }

    [Fact]
    public void Un_passaggio_che_esce_e_rientra_dal_cielo_produce_due_tratti()
    {
        var propagator = new ScriptedPropagator(T0, s => (45.0, s is >= 20.0 and < 30.0 ? -5.0 : 30.0));
        SatellitePass pass = new(
            new PassPoint(T0, 45.0, 30.0), new PassPoint(T0.AddSeconds(40), 45.0, 30.0), new PassPoint(T0.AddSeconds(60), 45.0, 30.0),
            30.0, 1.0, false, false, false);

        ProjectedPassTrack track = PassTrackProjector.Project(pass, propagator, TransitFixtures.Site, TransitFixtures.AcceptedCalibration());

        Assert.Equal(2, track.Runs.Count);
    }

    [Fact]
    public void Passaggio_parzialmente_fuori_orizzonte_proietta_solo_la_parte_visibile()
    {
        var propagator = TransitFixtures.PartialArc();
        SatellitePass pass = TransitFixtures.PassFor(propagator);

        ProjectedPassTrack track = PassTrackProjector.Project(pass, propagator, TransitFixtures.Site, TransitFixtures.AcceptedCalibration());

        Assert.Single(track.Runs);
        TrackPixel first = track.Runs[0].Points[0];
        Assert.True(first.Utc > pass.Start.Utc, "La parte iniziale sotto l'orizzonte deve essere tagliata.");
        Assert.True(track.Runs[0].Points[^1].Utc < pass.End.Utc, "La parte finale sotto l'orizzonte deve essere tagliata.");
        foreach (TrackPixel p in track.Runs[0].Points)
        {
            Assert.True(propagator.Observe(p.Utc, 0, 0, 0).ElevationDeg >= 0.0);
        }
    }

    [Fact]
    public void Senza_calibrazione_il_risultato_e_vuoto_e_non_calibrato()
    {
        var propagator = TransitFixtures.FullArc();
        SatellitePass pass = TransitFixtures.PassFor(propagator);

        ProjectedPassTrack none = PassTrackProjector.Project(pass, propagator, TransitFixtures.Site, null);
        var rejected = new SkyCalibrationResult(TransitFixtures.Model, 0.5, 0.05, 10, 5, Accepted: false);
        ProjectedPassTrack notAccepted = PassTrackProjector.Project(pass, propagator, TransitFixtures.Site, rejected);

        Assert.False(none.Calibrated);
        Assert.Empty(none.Runs);
        Assert.False(notAccepted.Calibrated);
        Assert.Empty(notAccepted.Runs);
    }

    [Fact]
    public void Lunghezza_della_traccia_e_la_somma_dei_segmenti()
    {
        var propagator = TransitFixtures.FullArc();
        ProjectedPassTrack track = PassTrackProjector.Project(TransitFixtures.PassFor(propagator), propagator, TransitFixtures.Site, TransitFixtures.AcceptedCalibration());

        double expected = 0.0;
        var pts = track.Runs[0].Points;
        for (int i = 1; i < pts.Count; i++)
        {
            expected += Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2));
        }

        Assert.Equal(expected, track.LengthPx, 9);
        Assert.True(track.LengthPx > 100.0);
    }
}
