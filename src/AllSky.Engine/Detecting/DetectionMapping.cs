using AllSky.Capture;
using AllSky.Core;
using AllSky.Detection;
using AllSky.Events;
using AllSky.Transits;

namespace AllSky.Engine.Detecting;

/// <summary>Traduce la sezione <c>detection</c> della configurazione nelle opzioni dei moduli (P5.5).</summary>
internal static class DetectionMapping
{
    public static StreakDetectorOptions ToDetector(AppConfig.DetectionConfig.StreakSettings s) => new()
    {
        BackgroundFrames = s.BackgroundFrames,
        ThresholdSigma = s.ThresholdSigma,
        MinDifferenceLuma = s.MinDifferenceLuma,
        MinAreaPx = s.MinAreaPx,
        MinLengthPx = s.MinLengthPx,
        MaxWidthPx = s.MaxWidthPx,
        MinElongation = s.MinElongation,
        MaxChangedFraction = s.MaxChangedFraction,
    };

    public static StreakClassifierOptions ToClassifier(AppConfig.DetectionConfig.StreakSettings s) => new()
    {
        TrackDistancePx = s.TrackDistancePx,
        TrackAngleDeg = s.TrackAngleDeg,
        RepeatShiftPx = s.RepeatShiftPx,
        RepeatAngleDeg = s.RepeatAngleDeg,
        MaxFrameGap = TimeSpan.FromSeconds(s.MaxFrameGapSeconds),
        BolideSaturatedPeakLuma = s.BolideSaturatedPeakLuma,
        BolideIntegratedLuma = s.BolideIntegratedLuma,
        BolideLengthPx = s.BolideLengthPx,
    };

    public static TransitConfirmationOptions ToConfirmation(AppConfig.DetectionConfig.TransitSettings t) => new()
    {
        CorridorHalfWidthPx = t.CorridorHalfWidthPx,
        FlankWidthPx = t.FlankWidthPx,
        SampleSpacingPx = t.SampleSpacingPx,
        SigmaMultiplier = t.SigmaMultiplier,
        MinExcessLevels = t.MinExcessLevels,
        ScoreThreshold = t.ScoreThreshold,
        MinVisibleTrackPx = t.MinVisibleTrackPx,
    };

    public static EventSaverOptions ToEventSaver(AppConfig.DetectionConfig d) => new() { ClipFps = d.ClipFps };

    public static TransitSaveOptions ToTransitSave(AppConfig.DetectionConfig d) => new() { ClipFps = d.ClipFps };
}
