using AllSky.Core.Calibration;
using AllSky.Sky;
using AllSky.Sky.Calibration;
using Xunit;

namespace AllSky.Engine.Tests;

// Stato della calibrazione dal Sole (P3.3): raccolta, proposta, accetta/rifiuta, modalita' manuale. Punti sintetici senza rumore.
public class SunCalibrationSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly FisheyeModel Truth = new(640, 480, 300, 20, false, 0.0);

    // Osservazioni distribuite su tutto l'azimut e con altezze tra 15 e 63 gradi, a 10 minuti di distanza.
    private static void AddPoints(SunCalibrationSession session, int count, int startIndex = 0)
    {
        for (int i = startIndex; i < startIndex + count; i++)
        {
            double az = i * 360.0 / 12.0;
            double alt = 15.0 + (i % 5) * 12.0;
            Assert.True(Truth.TryAltAzToPixel(alt, az, out double x, out double y));
            Assert.True(session.AddSighting(Start.AddMinutes(10.0 * i), x, y, alt, az), $"Punto {i} rifiutato.");
        }
    }

    [Fact]
    public void EnoughPoints_ProposeAcceptableModel()
    {
        var session = new SunCalibrationSession();
        session.ObserveFrame(640, 480);

        AddPoints(session, 8);
        CalibrationSnapshot snapshot = session.Snapshot(null);

        Assert.Equal(CalibrationPhase.Review, snapshot.Phase);
        Assert.NotNull(snapshot.Proposal);
        Assert.Equal(OpticalCalibrationSource.Sun, snapshot.Proposal!.Source);
        Assert.True(snapshot.Proposal.Acceptable);
        Assert.Equal(8, snapshot.Points.Count);
        Assert.Equal(8, snapshot.Proposal.Residuals.Count);
        Assert.Equal(640, snapshot.FrameWidthPx);
        Assert.InRange(snapshot.Proposal.Model.HorizonRadius, 299.0, 301.0);
    }

    [Fact]
    public void TooFewPoints_StaysCollecting()
    {
        var session = new SunCalibrationSession();
        session.ObserveFrame(640, 480);

        AddPoints(session, 3);
        CalibrationSnapshot snapshot = session.Snapshot(null);

        Assert.Equal(CalibrationPhase.Collecting, snapshot.Phase);
        Assert.Null(snapshot.Proposal);
        Assert.Null(snapshot.FailureReason);
        Assert.Equal(SunCalibrationCollector.MinPoints, snapshot.MinPoints);
    }

    [Fact]
    public void PoorCoverage_ReportsFailureReason()
    {
        var session = new SunCalibrationSession();
        session.ObserveFrame(640, 480);

        // Tutti i punti in un settore stretto: copertura azimutale insufficiente.
        for (int i = 0; i < 8; i++)
        {
            double alt = 15.0 + i * 6.0;
            double az = 10.0 + i;
            Assert.True(Truth.TryAltAzToPixel(alt, az, out double x, out double y));
            session.AddSighting(Start.AddMinutes(10.0 * i), x, y, alt, az);
        }

        CalibrationSnapshot snapshot = session.Snapshot(null);

        Assert.Null(snapshot.Proposal);
        Assert.NotNull(snapshot.FailureReason);
        Assert.Equal(CalibrationPhase.Collecting, snapshot.Phase);
    }

    [Fact]
    public void ShouldDetect_OnlyWithSunInRangeAndAfterMiss()
    {
        var session = new SunCalibrationSession();
        DateTimeOffset now = Start;

        Assert.False(session.ShouldDetect(now, 5.0));
        Assert.False(session.ShouldDetect(now, 95.0));
        Assert.True(session.ShouldDetect(now, 30.0));

        session.RecordMiss(now);
        Assert.False(session.ShouldDetect(now.AddSeconds(30), 30.0));
        Assert.True(session.ShouldDetect(now.AddSeconds(61), 30.0));
    }

    [Fact]
    public void PointsTooCloseInTime_AreRejected()
    {
        var session = new SunCalibrationSession();
        Assert.True(session.AddSighting(Start, 320, 240, 30.0, 10.0));

        Assert.False(session.AddSighting(Start.AddMinutes(1), 330, 240, 31.0, 20.0));
        Assert.Single(session.Snapshot(null).Points);
    }

    [Fact]
    public void Reject_DropsProposalButKeepsPoints()
    {
        var session = new SunCalibrationSession();
        session.ObserveFrame(640, 480);
        AddPoints(session, 8);

        session.Reject();
        CalibrationSnapshot snapshot = session.Snapshot(null);

        Assert.Null(snapshot.Proposal);
        Assert.Equal(8, snapshot.Points.Count);
        Assert.Equal(CalibrationPhase.Collecting, snapshot.Phase);
    }

    [Fact]
    public void Consume_RemovesOnlyTheSameProposal()
    {
        var session = new SunCalibrationSession();
        session.ObserveFrame(640, 480);
        AddPoints(session, 8);
        CalibrationProposal first = session.Pending()!;

        session.Consume(first);

        Assert.Null(session.Pending());
    }

    [Fact]
    public void SetManual_NeedsAFrame()
    {
        var session = new SunCalibrationSession();

        Assert.False(session.SetManual(Truth));
        Assert.Null(session.Pending());
    }

    [Fact]
    public void SetManual_ProposesManualModelWithResiduals()
    {
        var session = new SunCalibrationSession();
        session.ObserveFrame(640, 480);
        AddPoints(session, 4);

        Assert.True(session.SetManual(Truth));
        CalibrationProposal? proposal = session.Pending();

        Assert.NotNull(proposal);
        Assert.Equal(OpticalCalibrationSource.Manual, proposal!.Source);
        Assert.True(proposal.Acceptable);
        Assert.Equal(4, proposal.Residuals.Count);
        Assert.True(proposal.RmsPx < 1e-6);
    }

    [Fact]
    public void ManualProposal_IsNotReplacedByNewSunPoints()
    {
        var session = new SunCalibrationSession();
        session.ObserveFrame(640, 480);
        AddPoints(session, 7);
        session.SetManual(Truth);

        AddPoints(session, 2, startIndex: 7);

        Assert.Equal(OpticalCalibrationSource.Manual, session.Pending()!.Source);
    }

    [Fact]
    public void Snapshot_IgnoresUnusableAcceptedConfig()
    {
        var session = new SunCalibrationSession();
        var broken = new AllSky.Core.Calibration.OpticalCalibrationConfig { ScaleRadiusPx = 0 };

        CalibrationSnapshot snapshot = session.Snapshot(broken);

        Assert.Null(snapshot.Accepted);
        Assert.Equal(CalibrationPhase.Collecting, snapshot.Phase);
    }

    [Fact]
    public void Snapshot_WithAcceptedConfigAndNoProposal_IsCalibrated()
    {
        var session = new SunCalibrationSession();
        var accepted = OpticalCalibrationConfig.FromModel(
            Truth, 640, 480, OpticalCalibrationSource.Sun, 0.1, Start);

        CalibrationSnapshot snapshot = session.Snapshot(accepted);

        Assert.Equal(CalibrationPhase.Calibrated, snapshot.Phase);
        Assert.Equal(accepted, snapshot.Accepted);
    }
}
