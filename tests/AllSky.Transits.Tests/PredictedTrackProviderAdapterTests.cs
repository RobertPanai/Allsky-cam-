using AllSky.Detection;
using AllSky.Satellites;
using AllSky.Sky.Calibration;
using AllSky.Transits;
using Xunit;

namespace AllSky.Transits.Tests;

public sealed class PredictedTrackProviderAdapterTests
{
    private static readonly DateTimeOffset T0 = TransitFixtures.T0;

    /// <summary>Un solo passaggio: alt sinusoidale sopra 10 gradi tra 96 s e 504 s circa, negativa altrove.</summary>
    private static ScriptedPropagator SinglePassSatellite() =>
        new(T0, s => (0.3 * s, s is >= 0.0 and <= 600.0 ? 60.0 * Math.Sin(Math.PI * s / 600.0) : -30.0));

    [Fact]
    public void Fornisce_la_traccia_durante_il_passaggio()
    {
        var adapter = new PredictedTrackProviderAdapter(
            new[] { new TrackedSatellite("ISS", SinglePassSatellite()) },
            TransitFixtures.Site,
            TransitFixtures.AcceptedCalibration());

        IReadOnlyList<PredictedTrack> tracks = adapter.GetTracks(T0.AddSeconds(300));

        PredictedTrack track = Assert.Single(tracks);
        Assert.Equal("ISS", track.Name);
        // Il segmento deve stare dentro il cerchio del cielo.
        Assert.True(TransitFixtures.Model.IsInsideSky(track.X1, track.Y1));
        Assert.True(TransitFixtures.Model.IsInsideSky(track.X2, track.Y2));
        Assert.True(Math.Abs(track.X1 - track.X2) + Math.Abs(track.Y1 - track.Y2) > 50.0);
    }

    [Fact]
    public void Nessuna_traccia_fuori_dal_passaggio()
    {
        var adapter = new PredictedTrackProviderAdapter(
            new[] { new TrackedSatellite("ISS", SinglePassSatellite()) },
            TransitFixtures.Site,
            TransitFixtures.AcceptedCalibration());

        Assert.Empty(adapter.GetTracks(T0.AddSeconds(1800)));
    }

    [Fact]
    public void Senza_calibrazione_valida_non_ci_sono_tracce_in_pixel()
    {
        var rejected = new SkyCalibrationResult(TransitFixtures.Model, 0.5, 0.05, 10, 5, Accepted: false);
        var noCal = new PredictedTrackProviderAdapter(new[] { new TrackedSatellite("ISS", SinglePassSatellite()) }, TransitFixtures.Site, null);
        var notAccepted = new PredictedTrackProviderAdapter(new[] { new TrackedSatellite("ISS", SinglePassSatellite()) }, TransitFixtures.Site, rejected);

        Assert.Empty(noCal.GetTracks(T0.AddSeconds(300)));
        Assert.Empty(notAccepted.GetTracks(T0.AddSeconds(300)));
    }

    [Fact]
    public void La_cache_si_rinnova_quando_il_frame_esce_dalla_finestra()
    {
        var adapter = new PredictedTrackProviderAdapter(
            new[] { new TrackedSatellite("ISS", SinglePassSatellite()) },
            TransitFixtures.Site,
            TransitFixtures.AcceptedCalibration());

        // Primo passaggio e poi un frame lontano, con un secondo passaggio nella stessa finestra cercata di nuovo.
        Assert.Single(adapter.GetTracks(T0.AddSeconds(300)));
        Assert.Empty(adapter.GetTracks(T0.AddHours(5)));
        Assert.Single(adapter.GetTracks(T0.AddSeconds(300)));
    }
}
