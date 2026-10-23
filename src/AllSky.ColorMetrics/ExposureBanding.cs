namespace AllSky.ColorMetrics;

/// <summary>Un cambio di esposizione o di guadagno fra due frame consecutivi.</summary>
/// <param name="Time">Istante del frame dopo il cambio.</param>
/// <param name="FromExposureMs">Esposizione prima del cambio.</param>
/// <param name="ToExposureMs">Esposizione dopo il cambio.</param>
/// <param name="FromGain">Guadagno prima del cambio.</param>
/// <param name="ToGain">Guadagno dopo il cambio.</param>
/// <param name="RelativeJump">|L_i - L_{i-1}| / L_{i-1}, con L la luminanza media.</param>
/// <param name="CoincidesWithPhaseChange">True se la fase cambia nello stesso passo.</param>
public sealed record ExposureChange(
    DateTimeOffset Time,
    double? FromExposureMs,
    double? ToExposureMs,
    double? FromGain,
    double? ToGain,
    double RelativeJump,
    bool CoincidesWithPhaseChange);

/// <summary>
/// Bande agli scatti di esposizione o guadagno. Un passo è un «cambio» se ExposureMs o Gain differiscono dal frame
/// precedente (null compreso). Si confronta la media di |Δ luminanza relativa| sui passi con cambio con la stessa media
/// sugli altri passi. Il rapporto vicino a 1 indica nessuna banda legata al cambio.
/// </summary>
public sealed record ExposureBandingResult(
    int Frames,
    int Changes,
    int ChangesAtPhaseChange,
    double? MeanAbsRelJumpAtChanges,
    double? MeanAbsRelJumpWithoutChanges,
    double? Ratio,
    IReadOnlyList<ExposureChange> ChangeList);

/// <summary>Calcolo delle bande agli scatti di esposizione e guadagno.</summary>
public static class ExposureBanding
{
    /// <summary>Calcola il rapporto fra i passi con cambio e quelli senza.</summary>
    public static ExposureBandingResult Measure(IReadOnlyList<FrameSample> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        FrameSample[] ordered = frames.OrderBy(f => f.TimeUtc).ToArray();
        var changeJumps = new List<double>();
        var plainJumps = new List<double>();
        var changes = new List<ExposureChange>();
        int atPhase = 0;

        for (int i = 1; i < ordered.Length; i++)
        {
            FrameSample prev = ordered[i - 1];
            FrameSample cur = ordered[i];
            if (prev.MeanLuminance <= 0)
            {
                continue;
            }

            double rel = Math.Abs(cur.MeanLuminance - prev.MeanLuminance) / prev.MeanLuminance;
            bool changed = cur.ExposureMs != prev.ExposureMs || cur.Gain != prev.Gain;
            if (!changed)
            {
                plainJumps.Add(rel);
                continue;
            }

            bool phase = cur.Mode != prev.Mode;
            if (phase)
            {
                atPhase++;
            }

            changeJumps.Add(rel);
            changes.Add(new ExposureChange(cur.TimeUtc, prev.ExposureMs, cur.ExposureMs, prev.Gain, cur.Gain, rel, phase));
        }

        double? meanChange = changeJumps.Count > 0 ? changeJumps.Average() : null;
        double? meanPlain = plainJumps.Count > 0 ? plainJumps.Average() : null;
        double? ratio = meanChange is { } mc && meanPlain is { } mp && mp > 0 ? mc / mp : null;
        return new ExposureBandingResult(ordered.Length, changes.Count, atPhase, meanChange, meanPlain, ratio, changes);
    }
}
