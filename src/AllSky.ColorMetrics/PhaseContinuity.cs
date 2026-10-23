namespace AllSky.ColorMetrics;

/// <summary>Un confine fra due fasi (per esempio day → twilight). Il rapporto è salto / salto tipico.</summary>
/// <param name="From">Fase prima del confine.</param>
/// <param name="To">Fase dopo il confine.</param>
/// <param name="Time">Istante del primo frame dopo il confine.</param>
/// <param name="LumaJump">|mediana dopo - mediana prima| della luminanza media.</param>
/// <param name="RgJump">Stesso salto per R/G del fondo.</param>
/// <param name="BgJump">Stesso salto per B/G del fondo.</param>
/// <param name="LumaRatio">LumaJump / salto tipico della luminanza; null se il tipico è zero.</param>
/// <param name="RgRatio">RgJump / salto tipico di R/G.</param>
/// <param name="BgRatio">BgJump / salto tipico di B/G.</param>
public sealed record PhaseBoundary(
    string From,
    string To,
    DateTimeOffset Time,
    double LumaJump,
    double RgJump,
    double BgJump,
    double? LumaRatio,
    double? RgRatio,
    double? BgRatio);

/// <summary>Risultato della continuità di fase. I salti tipici sono le mediane dei |Δ| fra frame consecutivi della stessa fase.</summary>
public sealed record PhaseContinuityResult(
    IReadOnlyList<PhaseBoundary> Boundaries,
    double? TypicalLumaStep,
    double? TypicalRgStep,
    double? TypicalBgStep);

/// <summary>
/// Continuità ai confini di fase. Per ogni confine fra due Mode diversi si confrontano le mediane dei 5 frame prima
/// e dei 5 dopo. Il rapporto «salto/tipico» divide il salto per la mediana dei |Δ| fra frame consecutivi della stessa fase.
/// Attenzione: due mediane di 5 frame sono distanti 5 frame, quindi una rampa lineare liscia dà un rapporto di circa 5
/// anche senza salto; un salto netto si legge come rapporto sopra quella base.
/// </summary>
public static class PhaseContinuity
{
    /// <summary>Frame usati da ciascun lato del confine.</summary>
    public const int SideFrames = 5;

    /// <summary>Calcola i confini e i rapporti. I frame senza Mode non entrano nei confronti.</summary>
    public static PhaseContinuityResult Measure(IReadOnlyList<FrameSample> frames, int sideFrames = SideFrames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        FrameSample[] ordered = frames.OrderBy(f => f.TimeUtc).ToArray();

        var lumaSteps = new List<double>();
        var rgSteps = new List<double>();
        var bgSteps = new List<double>();
        for (int i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].Mode is null || ordered[i].Mode != ordered[i - 1].Mode)
            {
                continue;
            }

            lumaSteps.Add(Math.Abs(ordered[i].MeanLuminance - ordered[i - 1].MeanLuminance));
            rgSteps.Add(Math.Abs(Rg(ordered[i]) - Rg(ordered[i - 1])));
            bgSteps.Add(Math.Abs(Bg(ordered[i]) - Bg(ordered[i - 1])));
        }

        double? tl = Typical(lumaSteps);
        double? tr = Typical(rgSteps);
        double? tb = Typical(bgSteps);

        var boundaries = new List<PhaseBoundary>();
        for (int i = 1; i < ordered.Length; i++)
        {
            string? from = ordered[i - 1].Mode;
            string? to = ordered[i].Mode;
            if (from is null || to is null || from == to)
            {
                continue;
            }

            int b0 = Math.Max(0, i - sideFrames);
            int a1 = Math.Min(ordered.Length - 1, i + sideFrames - 1);
            FrameSample[] before = ordered[b0..i];
            FrameSample[] after = ordered[i..(a1 + 1)];

            double lj = Math.Abs(Robust.Median(after.Select(f => f.MeanLuminance).ToArray())
                                 - Robust.Median(before.Select(f => f.MeanLuminance).ToArray()));
            double rj = Math.Abs(Robust.Median(after.Select(Rg).ToArray()) - Robust.Median(before.Select(Rg).ToArray()));
            double bj = Math.Abs(Robust.Median(after.Select(Bg).ToArray()) - Robust.Median(before.Select(Bg).ToArray()));

            boundaries.Add(new PhaseBoundary(
                from,
                to,
                ordered[i].TimeUtc,
                lj,
                rj,
                bj,
                Ratio(lj, tl),
                Ratio(rj, tr),
                Ratio(bj, tb)));
        }

        return new PhaseContinuityResult(boundaries, tl, tr, tb);
    }

    private static double Rg(FrameSample f) => f.SkyG > 0 ? f.SkyR / f.SkyG : double.NaN;

    private static double Bg(FrameSample f) => f.SkyG > 0 ? f.SkyB / f.SkyG : double.NaN;

    private static double? Typical(List<double> steps)
    {
        var valid = steps.Where(double.IsFinite).ToArray();
        return valid.Length > 0 ? Robust.Median(valid) : null;
    }

    private static double? Ratio(double jump, double? typical) =>
        typical is { } t && t > 0 ? jump / t : null;
}
