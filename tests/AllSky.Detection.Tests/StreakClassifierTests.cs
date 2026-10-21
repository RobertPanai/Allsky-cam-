using AllSky.Detection;
using Xunit;

namespace AllSky.Detection.Tests;

// Test del classificatore di scie su casi sintetici: scie costruite a mano (record Streak) e, in fondo,
// due prove end-to-end con il rilevatore reale su frame sintetici a seed fisso.
public class StreakClassifierTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 22, 0, 0, TimeSpan.Zero);

    // Scia di estremi dati, lunghezza coerente con gli estremi. Straightness e Score non sono usati dal classificatore.
    private static Streak Line(double x1, double y1, double x2, double y2, double peak = 40, double integrated = 400)
    {
        double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        return new Streak(x1, y1, x2, y2, length, peak, integrated, 0.2, 1.0);
    }

    // Scia orizzontale di lunghezza esatta, su una riga data, con i valori di luminosita' indicati.
    private static Streak Horizontal(double length, double y, double peak = 40, double integrated = 400)
        => new(100, y, 100 + length, y, length, peak, integrated, 0.2, 1.0);

    private static StreakClassifier NewClassifier(IPredictedTrackProvider? tracks = null, StreakClassifierOptions? options = null)
        => new(options ?? new StreakClassifierOptions(), tracks);

    private static StreakClass Only(FrameClassification frame)
    {
        Assert.False(frame.FrameDiscarded);
        Assert.Single(frame.Streaks);
        return frame.Streaks[0].Class;
    }

    private sealed class FakeTracks : IPredictedTrackProvider
    {
        public List<PredictedTrack> Tracks { get; } = new();
        public List<DateTimeOffset> Asked { get; } = new();

        public IReadOnlyList<PredictedTrack> GetTracks(DateTimeOffset utc)
        {
            Asked.Add(utc);
            return Tracks;
        }
    }

    // ---- Ramo (a): satellite previsto -------------------------------------------------------------

    [Fact]
    public void Satellite_StreakOnPredictedTrack_IsSatellite()
    {
        var tracks = new FakeTracks();
        tracks.Tracks.Add(new PredictedTrack("ISS", 100, 100, 300, 100));
        var classifier = NewClassifier(tracks);

        // Punto medio (200, 104): 4 px dalla traccia, angolo 0 gradi.
        FrameClassification frame = classifier.Classify(T0, new[] { Line(150, 104, 250, 104) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Satellite, Only(frame));
        Assert.Equal(T0, Assert.Single(tracks.Asked));
    }

    [Fact]
    public void Satellite_MidpointJustOutsideDistanceTolerance_IsNotSatellite()
    {
        var tracks = new FakeTracks();
        tracks.Tracks.Add(new PredictedTrack("ISS", 100, 100, 300, 100));
        var classifier = NewClassifier(tracks);

        // Distanza 12.5 px, oltre la tolleranza di 12 px.
        FrameClassification frame = classifier.Classify(T0, new[] { Line(150, 112.5, 250, 112.5) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Meteor, Only(frame));
    }

    [Fact]
    public void Satellite_AngleOutsideTolerance_IsNotSatellite()
    {
        var tracks = new FakeTracks();
        tracks.Tracks.Add(new PredictedTrack("ISS", 100, 100, 300, 100));
        var classifier = NewClassifier(tracks);

        // Stesso punto medio (200, 100), ma inclinata di 20 gradi contro la tolleranza di 8.
        double c = Math.Cos(20 * Math.PI / 180) * 50;
        double s = Math.Sin(20 * Math.PI / 180) * 50;
        FrameClassification frame = classifier.Classify(T0, new[] { Line(200 - c, 100 - s, 200 + c, 100 + s) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Meteor, Only(frame));
    }

    [Fact]
    public void Satellite_ZeroLengthTrack_MatchesByDistanceOnly()
    {
        var tracks = new FakeTracks();
        tracks.Tracks.Add(new PredictedTrack("punto", 50, 50, 50, 50));
        var classifier = NewClassifier(tracks);

        // Scia verticale: con una traccia puntuale l'angolo non conta.
        FrameClassification frame = classifier.Classify(T0, new[] { Line(50, 30, 50, 70) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Satellite, Only(frame));
    }

    [Fact]
    public void Satellite_TakesPriorityOverBolide()
    {
        var tracks = new FakeTracks();
        tracks.Tracks.Add(new PredictedTrack("ISS", 100, 100, 500, 100));
        var classifier = NewClassifier(tracks);

        // Lunga e saturata: sarebbe un bolide, ma coincide con la traccia prevista.
        FrameClassification frame = classifier.Classify(T0, new[] { Horizontal(400, 100, peak: 250, integrated: 9000) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Satellite, Only(frame));
    }

    // ---- Ramo (b): oggetto allineato che si ripete su frame consecutivi ----------------------------

    [Fact]
    public void Repeated_SameStreakOnConsecutiveFrames_SecondIsAereoOSatellite()
    {
        var classifier = NewClassifier();
        var first = classifier.Classify(T0, new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);
        var second = classifier.Classify(T0.AddSeconds(10), new[] { Line(105, 200, 185, 200) }, frameDiscarded: false);

        // La prima occorrenza e' provvisoria (Meteor), la ripetizione la conferma.
        Assert.Equal(StreakClass.Meteor, Only(first));
        Assert.Equal(StreakClass.AereoOSatellite, Only(second));
    }

    [Fact]
    public void Repeated_ShiftExactlyAtLimit_IsStillRepeated()
    {
        var options = new StreakClassifierOptions { RepeatShiftPx = 30.0 };
        var classifier = NewClassifier(options: options);
        classifier.Classify(T0, new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);

        // Punto medio spostato di esattamente 30 px: il limite e' incluso.
        var second = classifier.Classify(T0.AddSeconds(10), new[] { Line(130, 200, 210, 200) }, frameDiscarded: false);

        Assert.Equal(StreakClass.AereoOSatellite, Only(second));
    }

    [Fact]
    public void Repeated_ShiftBeyondLimit_IsNotRepeated()
    {
        var classifier = NewClassifier();
        classifier.Classify(T0, new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);

        // Spostamento di 31 px, oltre il limite di 30.
        var second = classifier.Classify(T0.AddSeconds(10), new[] { Line(131, 200, 211, 200) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Meteor, Only(second));
    }

    [Fact]
    public void Repeated_AngleBeyondLimit_IsNotRepeated()
    {
        var classifier = NewClassifier();
        classifier.Classify(T0, new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);

        // Stesso punto medio ma inclinata di 30 gradi, oltre la tolleranza di 8.
        double c = Math.Cos(30 * Math.PI / 180) * 40;
        double s = Math.Sin(30 * Math.PI / 180) * 40;
        var second = classifier.Classify(T0.AddSeconds(10), new[] { Line(140 - c, 200 - s, 140 + c, 200 + s) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Meteor, Only(second));
    }

    [Fact]
    public void Repeated_GapBeyondMaxFrameGap_IsNotRepeated()
    {
        var classifier = NewClassifier();
        classifier.Classify(T0, new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);

        // 31 s tra i frame, oltre il limite di 30 s: non sono consecutivi.
        var second = classifier.Classify(T0.AddSeconds(31), new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Meteor, Only(second));
    }

    [Fact]
    public void Repeated_ChainBrokenByDiscardedFrame_IsNotRepeated()
    {
        var classifier = NewClassifier();
        classifier.Classify(T0, new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);
        classifier.Classify(T0.AddSeconds(10), Array.Empty<Streak>(), frameDiscarded: true);

        var after = classifier.Classify(T0.AddSeconds(20), new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Meteor, Only(after));
    }

    [Fact]
    public void Repeated_AfterReset_IsNotRepeated()
    {
        var classifier = NewClassifier();
        classifier.Classify(T0, new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);
        classifier.Reset();

        var after = classifier.Classify(T0.AddSeconds(10), new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Meteor, Only(after));
    }

    [Fact]
    public void Repeated_DifferentStreakOnNextFrame_IsNotRepeated()
    {
        var classifier = NewClassifier();
        classifier.Classify(T0, new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);

        // Una scia lontana nel frame successivo non e' la stessa: la sua classe resta Meteor.
        var second = classifier.Classify(T0.AddSeconds(10), new[] { Line(500, 400, 580, 400) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Meteor, Only(second));
    }

    // ---- Ramo (c): frame scartato -----------------------------------------------------------------

    [Fact]
    public void FrameDiscarded_ReturnsNoStreaks()
    {
        var classifier = NewClassifier();

        FrameClassification frame = classifier.Classify(T0, new[] { Line(100, 200, 180, 200) }, frameDiscarded: true);

        Assert.True(frame.FrameDiscarded);
        Assert.Empty(frame.Streaks);
    }

    // ---- Meteora (scia singola, non attesa, non ripetuta) ----------------------------------------

    [Fact]
    public void Meteor_SingleFaintStreakWithoutTracks_IsMeteor()
    {
        var classifier = NewClassifier();

        FrameClassification frame = classifier.Classify(T0, new[] { Line(100, 200, 180, 200) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Meteor, Only(frame));
    }

    [Fact]
    public void Meteor_EmptyFrame_ReturnsNoStreaks()
    {
        var classifier = NewClassifier();

        FrameClassification frame = classifier.Classify(T0, Array.Empty<Streak>(), frameDiscarded: false);

        Assert.False(frame.FrameDiscarded);
        Assert.Empty(frame.Streaks);
    }

    // ---- Bolide: soglie alte, con i valori di confine ---------------------------------------------

    [Fact]
    public void Bolide_SaturatedPeak_IsBolide()
    {
        var classifier = NewClassifier();

        FrameClassification frame = classifier.Classify(T0, new[] { Horizontal(80, 200, peak: 250, integrated: 500) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Bolide, Only(frame));
    }

    [Fact]
    public void Bolide_HighIntegratedLuma_IsBolide()
    {
        var classifier = NewClassifier();

        FrameClassification frame = classifier.Classify(T0, new[] { Horizontal(80, 200, peak: 40, integrated: 6000) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Bolide, Only(frame));
    }

    [Fact]
    public void Bolide_VeryLongStreak_IsBolide()
    {
        var classifier = NewClassifier();

        FrameClassification frame = classifier.Classify(T0, new[] { Horizontal(350, 200, peak: 40, integrated: 500) }, frameDiscarded: false);

        Assert.Equal(StreakClass.Bolide, Only(frame));
    }

    [Theory]
    [InlineData(199.9, 400.0, 100.0, StreakClass.Meteor)]   // picco appena sotto la soglia di saturazione
    [InlineData(200.0, 400.0, 100.0, StreakClass.Bolide)]   // picco esattamente alla soglia: saturo
    [InlineData(40.0, 3999.9, 100.0, StreakClass.Meteor)]   // luminosita' integrata appena sotto
    [InlineData(40.0, 4000.0, 100.0, StreakClass.Bolide)]   // esattamente alla soglia
    [InlineData(40.0, 400.0, 299.9, StreakClass.Meteor)]    // lunghezza appena sotto
    [InlineData(40.0, 400.0, 300.0, StreakClass.Bolide)]    // esattamente alla soglia
    public void Bolide_ThresholdBoundaries(double peak, double integrated, double length, StreakClass expected)
    {
        var classifier = NewClassifier();

        FrameClassification frame = classifier.Classify(T0, new[] { Horizontal(length, 200, peak, integrated) }, frameDiscarded: false);

        Assert.Equal(expected, Only(frame));
    }

    // ---- Opzioni ----------------------------------------------------------------------------------

    [Fact]
    public void Options_InvalidValue_ThrowsOnConstruction()
    {
        var invalid = new StreakClassifierOptions { TrackDistancePx = -1.0 };

        Assert.Throws<ArgumentOutOfRangeException>(() => new StreakClassifier(invalid));
    }

    // ---- End-to-end con il rilevatore reale su frame sintetici ------------------------------------

    private static LumaFrame Frame(byte[] pixels)
        => new(StreakSyntheticSky.Width, StreakSyntheticSky.Height, pixels, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Pipeline_FaintTrailFromDetector_IsClassifiedAsMeteor()
    {
        var sky = new StreakSyntheticSky(3003);
        var detector = new StreakDetector(new StreakDetectorOptions());
        var classifier = NewClassifier();
        for (int i = 0; i < 8; i++)
        {
            detector.Detect(Frame(sky.Render()));
        }

        // Scia debole (SNR 5, lunghezza 60 px) su fondo pulito.
        var trail = new SyntheticTrail(300, 200, 360, 200, 5 * StreakSyntheticSky.NoiseSigma);
        IReadOnlyList<Streak> streaks = detector.Detect(Frame(sky.Render(new[] { trail })));
        FrameClassification frame = classifier.Classify(T0, streaks, detector.LastFrameDiscarded);

        Assert.False(frame.FrameDiscarded);
        ClassifiedStreak found = Assert.Single(frame.Streaks.Where(c => trail.Matches(c.Streak)));
        Assert.Equal(StreakClass.Meteor, found.Class);
    }

    [Fact]
    public void Pipeline_GlobalChangeFromDetector_FrameIsDiscarded()
    {
        var sky = new StreakSyntheticSky(4004);
        var detector = new StreakDetector(new StreakDetectorOptions());
        var classifier = NewClassifier();
        for (int i = 0; i < 8; i++)
        {
            detector.Detect(Frame(sky.Render()));
        }

        // Un 10 per cento dei pixel acceso di 40 livelli: nuvola o faro, oltre il limite del 2 per cento.
        IReadOnlyList<Streak> streaks = detector.Detect(Frame(sky.Render(illuminatedFraction: 0.10, illuminatedBoost: 40)));
        FrameClassification frame = classifier.Classify(T0, streaks, detector.LastFrameDiscarded);

        Assert.True(detector.LastFrameDiscarded);
        Assert.True(frame.FrameDiscarded);
        Assert.Empty(frame.Streaks);
    }
}
