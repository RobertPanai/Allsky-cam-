using System.Globalization;
using AllSky.Core.Calibration;
using AllSky.Sky;
using Xunit;

namespace AllSky.Core.Tests;

// Logica pura della calibrazione ottica (P3.3): fase del flusso, residui, griglia alt/az, form della modalita' manuale.
public class CalibrationFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly FisheyeModel Truth = new(640, 480, 300, 20, false, 0.01);

    [Fact]
    public void PhaseOf_FollowsProposalThenAccepted()
    {
        Assert.Equal(CalibrationPhase.Review, CalibrationFlow.PhaseOf(hasProposal: true, hasAccepted: false));
        Assert.Equal(CalibrationPhase.Review, CalibrationFlow.PhaseOf(hasProposal: true, hasAccepted: true));
        Assert.Equal(CalibrationPhase.Calibrated, CalibrationFlow.PhaseOf(hasProposal: false, hasAccepted: true));
        Assert.Equal(CalibrationPhase.Collecting, CalibrationFlow.PhaseOf(hasProposal: false, hasAccepted: false));
    }

    [Fact]
    public void Residuals_ExactPoints_HaveZeroError()
    {
        var points = new List<CalibrationPoint>();
        for (int i = 0; i < 4; i++)
        {
            double alt = 20.0 + 15.0 * i;
            double az = 45.0 + 80.0 * i;
            Assert.True(Truth.TryAltAzToPixel(alt, az, out double x, out double y));
            points.Add(new CalibrationPoint(Start.AddMinutes(i), x, y, alt, az));
        }

        IReadOnlyList<CalibrationResidual> residuals = CalibrationFlow.Residuals(Truth, points);

        Assert.All(residuals, r => Assert.True(r.ErrorPx < 1e-9));
        Assert.Equal(0.0, CalibrationFlow.RmsPx(residuals), 9);
    }

    [Fact]
    public void Residuals_UnprojectablePoint_HasNoPredictionAndNaNError()
    {
        var points = new[] { new CalibrationPoint(Start, 100, 100, -5.0, 10.0) };

        IReadOnlyList<CalibrationResidual> residuals = CalibrationFlow.Residuals(Truth, points);

        Assert.Null(residuals[0].Predicted);
        Assert.True(double.IsNaN(residuals[0].ErrorPx));
        Assert.Equal(0.0, CalibrationFlow.RmsPx(residuals));
    }

    [Fact]
    public void Residuals_Offset_ReportsDistance()
    {
        Assert.True(Truth.TryAltAzToPixel(40.0, 120.0, out double x, out double y));
        var points = new[] { new CalibrationPoint(Start, x + 3.0, y + 4.0, 40.0, 120.0) };

        IReadOnlyList<CalibrationResidual> residuals = CalibrationFlow.Residuals(Truth, points);

        Assert.Equal(5.0, residuals[0].ErrorPx, 9);
    }

    [Fact]
    public void AcceptanceFraction_UsesSkyThreshold()
    {
        Assert.True(CalibrationFlow.IsAcceptableFraction(0.005));
        Assert.False(CalibrationFlow.IsAcceptableFraction(0.02));
        Assert.False(CalibrationFlow.IsAcceptableFraction(double.NaN));
    }

    [Fact]
    public void HorizonCircle_LiesOnHorizonRadius()
    {
        IReadOnlyList<GridLine> circles = SkyOverlayGeometry.AltitudeCircles(Truth, new[] { 0.0 });

        Assert.Single(circles);
        Assert.True(circles[0].IsAltitude);
        double radius = Truth.HorizonRadius;
        foreach (PixelPoint p in circles[0].Points)
        {
            double d = Math.Sqrt((p.X - Truth.CenterX) * (p.X - Truth.CenterX) + (p.Y - Truth.CenterY) * (p.Y - Truth.CenterY));
            Assert.Equal(radius, d, 6);
        }
    }

    [Fact]
    public void DefaultGrid_HasAltitudeCirclesAndMeridians()
    {
        IReadOnlyList<GridLine> grid = SkyOverlayGeometry.DefaultGrid(Truth);

        Assert.Equal(3, grid.Count(l => l.IsAltitude));
        Assert.Equal(12, grid.Count(l => !l.IsAltitude));
    }

    [Fact]
    public void Meridian_EndsAtZenithCenter()
    {
        IReadOnlyList<GridLine> meridians = SkyOverlayGeometry.Meridians(Truth, 90.0);

        Assert.All(meridians, m =>
        {
            PixelPoint last = m.Points[^1];
            Assert.Equal(Truth.CenterX, last.X, 6);
            Assert.Equal(Truth.CenterY, last.Y, 6);
        });
    }

    [Fact]
    public void Cardinals_NorthUp_EastLeft_WhenNotMirrored()
    {
        var model = new FisheyeModel(100, 100, 80, 0, Mirrored: false, K2: 0);

        Dictionary<CardinalDirection, PixelPoint> marks = SkyOverlayGeometry.Cardinals(model)
            .ToDictionary(m => m.Direction, m => m.Horizon);

        Assert.True(marks[CardinalDirection.North].Y < 100);
        Assert.True(marks[CardinalDirection.East].X < 100);
        Assert.True(marks[CardinalDirection.South].Y > 100);
        Assert.True(marks[CardinalDirection.West].X > 100);
    }

    [Fact]
    public void Cardinals_Mirrored_SwapsEastAndWest()
    {
        var model = new FisheyeModel(100, 100, 80, 0, Mirrored: true, K2: 0);

        Dictionary<CardinalDirection, PixelPoint> marks = SkyOverlayGeometry.Cardinals(model)
            .ToDictionary(m => m.Direction, m => m.Horizon);

        Assert.True(marks[CardinalDirection.East].X > 100);
        Assert.True(marks[CardinalDirection.West].X < 100);
    }

    [Fact]
    public void Cardinals_RotationNinety_PutsNorthOnTheRight()
    {
        var model = new FisheyeModel(100, 100, 80, 90, Mirrored: false, K2: 0);

        PixelPoint north = SkyOverlayGeometry.Cardinals(model).Single(m => m.Direction == CardinalDirection.North).Horizon;

        Assert.True(north.X > 100);
        Assert.Equal(100.0, north.Y, 6);
    }

    [Fact]
    public void Manual_ValidFields_BuildModelWithTypedHorizonRadius()
    {
        ManualCalibrationCheck check = CalibrationManualForm.Check(
            "640", "480", "300", "0.1", "20", mirrored: true, CultureInfo.InvariantCulture);

        Assert.True(check.IsValid);
        Assert.Equal(300.0, check.Model!.HorizonRadius, 9);
        Assert.Equal(300.0 / 1.1, check.Model.ScaleRadiusPx, 9);
        Assert.True(check.Model.Mirrored);
    }

    [Fact]
    public void Manual_ZeroRadius_IsNotPositive()
    {
        ManualCalibrationCheck check = CalibrationManualForm.Check(
            "1", "1", "0", "0", "0", false, CultureInfo.InvariantCulture);

        Assert.False(check.IsValid);
        Assert.Equal(ManualFieldProblem.NotPositive, check.RadiusProblem);
    }

    [Fact]
    public void Manual_K2BelowLimit_IsOutOfRange()
    {
        ManualCalibrationCheck check = CalibrationManualForm.Check(
            "1", "1", "10", "-0.5", "0", false, CultureInfo.InvariantCulture);

        Assert.Equal(ManualFieldProblem.OutOfRange, check.K2Problem);
        Assert.Null(check.Model);
    }

    [Fact]
    public void Manual_EmptyAndNotNumber_AreReportedPerField()
    {
        ManualCalibrationCheck check = CalibrationManualForm.Check(
            "", "abc", "10", "0", "0", false, CultureInfo.InvariantCulture);

        Assert.Equal(ManualFieldProblem.Empty, check.CenterXProblem);
        Assert.Equal(ManualFieldProblem.NotNumber, check.CenterYProblem);
        Assert.Equal(ManualFieldProblem.None, check.RadiusProblem);
    }

    [Fact]
    public void Manual_ItalianDecimalComma_IsAccepted()
    {
        ManualCalibrationCheck check = CalibrationManualForm.Check(
            "12,5", "3,25", "10", "0", "0", false, new CultureInfo("it-IT"));

        Assert.True(check.IsValid);
        Assert.Equal(12.5, check.Model!.CenterX, 9);
        Assert.Equal(3.25, check.Model.CenterY, 9);
    }

    [Fact]
    public void Manual_NonFiniteNumber_IsNotNumber()
    {
        ManualCalibrationCheck check = CalibrationManualForm.Check(
            "NaN", "1", "10", "0", "0", false, CultureInfo.InvariantCulture);

        Assert.Equal(ManualFieldProblem.NotNumber, check.CenterXProblem);
    }
}
