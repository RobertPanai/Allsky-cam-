using AllSky.Detection;
using AllSky.Satellites;
using AllSky.Sky.Calibration;
using AllSky.Transits;
using Xunit;

namespace AllSky.Transits.Tests;

public sealed class TransitConfirmerTests
{
    private const double Background = 30.0;
    private const double NoiseSigma = 2.0;
    private const double StreakAmplitude = 60.0;

    private static readonly DateTimeOffset T0 = TransitFixtures.T0;

    /// <summary>Frame di controllo ogni 20 s dentro il passaggio, più uno fuori dalla finestra.</summary>
    private static List<LumaFrame> NoiseFrames(int seedBase = 100)
    {
        var frames = new List<LumaFrame>();
        for (int i = 0; i <= 6; i++)
        {
            frames.Add(TransitFixtures.NoisyFrame(T0.AddSeconds(20 * i), Background, NoiseSigma, seedBase + i));
        }

        return frames;
    }

    private static ProjectedPassTrack ProjectArc(ScriptedPropagator propagator, SkyCalibrationResult? calibration = null, bool useCalibration = true)
    {
        SatellitePass pass = TransitFixtures.PassFor(propagator);
        return PassTrackProjector.Project(pass, propagator, TransitFixtures.Site, useCalibration ? (calibration ?? TransitFixtures.AcceptedCalibration()) : null);
    }

    [Fact]
    public void Scia_iniettata_lungo_la_traccia_e_identificata()
    {
        var propagator = TransitFixtures.FullArc();
        ProjectedPassTrack track = ProjectArc(propagator);
        List<LumaFrame> frames = NoiseFrames();
        var ideal = TransitFixtures.IdealVisibleTrack(s => (1.5 * s, 20.0 + 50.0 * Math.Sin(Math.PI * s / TransitFixtures.DurationSeconds)));
        TransitFixtures.DrawPath(frames[3], ideal, StreakAmplitude);

        TransitConfirmation result = new TransitConfirmer().Confirm(track, frames);

        Assert.Equal(TransitVerdict.Identificato, result.Verdict);
        Assert.Equal(T0.AddSeconds(60), result.BestFrameUtc);
        Assert.True(result.BestScore >= 0.6, $"Punteggio {result.BestScore:F3} sotto soglia.");
        Assert.Equal(7, result.FramesEvaluated);
    }

    [Fact]
    public void Scia_altrove_non_e_identificata()
    {
        var propagator = TransitFixtures.FullArc();
        ProjectedPassTrack track = ProjectArc(propagator);
        List<LumaFrame> frames = NoiseFrames();

        // Stessa forma ma spostata di 60 px: fuori dal corridoio di ±4 px.
        var ideal = TransitFixtures.IdealVisibleTrack(s => (1.5 * s, 20.0 + 50.0 * Math.Sin(Math.PI * s / TransitFixtures.DurationSeconds)));
        var shifted = ideal.Select(p => (p.X + 60.0, p.Y)).ToList();
        TransitFixtures.DrawPath(frames[3], shifted, StreakAmplitude);

        TransitConfirmation result = new TransitConfirmer().Confirm(track, frames);

        Assert.Equal(TransitVerdict.NonIdentificato, result.Verdict);
        Assert.True(result.BestScore < 0.2, $"Punteggio {result.BestScore:F3} troppo alto senza scia sulla traccia.");
    }

    [Fact]
    public void Senza_scia_il_rumore_non_e_identificato()
    {
        var propagator = TransitFixtures.FullArc();
        ProjectedPassTrack track = ProjectArc(propagator);

        TransitConfirmation result = new TransitConfirmer().Confirm(track, NoiseFrames());

        Assert.Equal(TransitVerdict.NonIdentificato, result.Verdict);
        Assert.True(result.BestScore < 0.2, $"Punteggio {result.BestScore:F3} con solo rumore.");
    }

    [Fact]
    public void Nuvole_e_rumore_diffuso_non_sono_identificati()
    {
        var propagator = TransitFixtures.FullArc();
        ProjectedPassTrack track = ProjectArc(propagator);
        List<LumaFrame> frames = NoiseFrames();
        foreach (LumaFrame f in frames)
        {
            // Nuvola larga sul centro, con trama: il fondo locale la segue e l'eccesso resta nel rumore.
            TransitFixtures.AddCloud(f, amplitude: 80.0, sigmaPx: 120.0, seed: 7);
        }

        TransitConfirmation result = new TransitConfirmer().Confirm(track, frames);

        Assert.Equal(TransitVerdict.NonIdentificato, result.Verdict);
        Assert.True(result.BestScore < 0.5, $"Punteggio {result.BestScore:F3} con nuvole.");
    }

    [Fact]
    public void Senza_calibrazione_il_passaggio_e_previsto_non_verificato()
    {
        var propagator = TransitFixtures.FullArc();
        ProjectedPassTrack track = ProjectArc(propagator, useCalibration: false);
        List<LumaFrame> frames = NoiseFrames();
        var ideal = TransitFixtures.IdealVisibleTrack(s => (1.5 * s, 20.0 + 50.0 * Math.Sin(Math.PI * s / TransitFixtures.DurationSeconds)));
        TransitFixtures.DrawPath(frames[3], ideal, StreakAmplitude);

        TransitConfirmation result = new TransitConfirmer().Confirm(track, frames);

        Assert.Equal(TransitVerdict.PrevistoNonVerificato, result.Verdict);
        Assert.Equal(0, result.FramesEvaluated);
    }

    [Fact]
    public void Frame_fuori_dalla_finestra_del_passaggio_non_contano()
    {
        var propagator = TransitFixtures.FullArc();
        ProjectedPassTrack track = ProjectArc(propagator);

        // Scia sulla traccia, ma in un frame prima dell'inizio del passaggio.
        LumaFrame before = TransitFixtures.NoisyFrame(T0.AddSeconds(-60), Background, NoiseSigma, 5);
        var ideal = TransitFixtures.IdealVisibleTrack(s => (1.5 * s, 20.0 + 50.0 * Math.Sin(Math.PI * s / TransitFixtures.DurationSeconds)));
        TransitFixtures.DrawPath(before, ideal, StreakAmplitude);

        TransitConfirmation result = new TransitConfirmer().Confirm(track, new[] { before });

        Assert.Equal(TransitVerdict.PrevistoNonVerificato, result.Verdict);
        Assert.Equal(0, result.FramesEvaluated);
        Assert.Null(result.BestFrameUtc);
    }

    [Fact]
    public void Traccia_parzialmente_fuori_orizzonte_conta_solo_la_parte_visibile()
    {
        var propagator = TransitFixtures.PartialArc();
        ProjectedPassTrack track = ProjectArc(propagator);
        Assert.True(track.Runs.Count == 1 && track.LengthPx > 40.0, "Il tratto visibile deve esistere e superare la soglia minima.");

        List<LumaFrame> frames = NoiseFrames();
        var visible = TransitFixtures.IdealVisibleTrack(s => (1.5 * s, -15.0 + 75.0 * Math.Sin(Math.PI * s / TransitFixtures.DurationSeconds)));
        TransitFixtures.DrawPath(frames[3], visible, StreakAmplitude);

        TransitConfirmation result = new TransitConfirmer().Confirm(track, frames);

        Assert.Equal(TransitVerdict.Identificato, result.Verdict);
        Assert.Equal(track.LengthPx, result.VisibleTrackLengthPx, 9);
    }

    [Fact]
    public void Traccia_visibile_troppo_corta_non_e_verificabile()
    {
        // Alt sopra l'orizzonte solo per pochi secondi: tratto sotto la soglia minima.
        var propagator = new ScriptedPropagator(T0, s => (1.5 * s, s is >= 58.0 and <= 61.0 ? 30.0 : -5.0));
        ProjectedPassTrack track = PassTrackProjector.Project(
            TransitFixtures.PassFor(TransitFixtures.FullArc()), propagator, TransitFixtures.Site, TransitFixtures.AcceptedCalibration());

        TransitConfirmation result = new TransitConfirmer().Confirm(track, NoiseFrames());

        Assert.Equal(TransitVerdict.PrevistoNonVerificato, result.Verdict);
    }
}
