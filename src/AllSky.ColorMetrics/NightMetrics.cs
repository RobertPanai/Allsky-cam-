namespace AllSky.ColorMetrics;

/// <summary>Tutte le metriche di una notte, più i difetti emersi con le soglie dichiarate.</summary>
public sealed record NightMetricsResult(
    string NightId,
    int FramesAnalyzed,
    int FramesSkipped,
    DateTimeOffset? FirstUtc,
    DateTimeOffset? LastUtc,
    FlickerResult Flicker,
    ChromaDrift.ChromaDriftResult Drift,
    PhaseContinuityResult Phase,
    ExposureBandingResult Exposure,
    StarColorSummary Stars,
    EncoderColorInfo? Encoder,
    string EncoderNote,
    IReadOnlyList<string> Findings);

/// <summary>
/// Aggregazione delle metriche di una notte. Le soglie in <see cref="Thresholds"/> sono provvisorie: servono a
/// segnalare dove guardare, non a dichiarare un difetto di qualità. Ogni metrica produce una riga «Difetto»
/// oppure «Non emerge», sempre con i numeri.
/// </summary>
public static class NightMetrics
{
    /// <summary>Soglie di attenzione (provvisorie, non calibrate su frame reali).</summary>
    public static class Thresholds
    {
        /// <summary>Deviazione standard del flicker, in %.</summary>
        public const double FlickerStdPercent = 1.0;

        /// <summary>Deviazione dei residui dei rapporti del fondo, in % del rapporto medio.</summary>
        public const double DriftResidualPercent = 0.5;

        /// <summary>Rapporto salto/tipico oltre il quale un confine di fase è segnalato (una rampa liscia dà circa 5).</summary>
        public const double PhaseRatio = 10.0;

        /// <summary>Rapporto banda ai cambi di esposizione/guadagno oltre il quale è segnalato.</summary>
        public const double ExposureRatio = 2.0;
    }

    /// <summary>Calcola le metriche. I campioni devono venire dai frame elencati in <paramref name="stars"/> e dai sidecar.</summary>
    public static NightMetricsResult Compute(
        string nightId,
        IReadOnlyList<FrameSample> samples,
        IReadOnlyList<StarColorFrame> stars,
        int framesSkipped,
        EncoderColorInfo? encoder,
        string encoderNote)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(stars);
        var flicker = LuminanceFlicker.Measure(samples);
        var drift = ChromaDrift.Measure(samples);
        var phase = PhaseContinuity.Measure(samples);
        var exposure = ExposureBanding.Measure(samples);
        var starSummary = StarColor.Summarize(stars);
        DateTimeOffset? first = samples.Count > 0 ? samples.Min(s => s.TimeUtc) : null;
        DateTimeOffset? last = samples.Count > 0 ? samples.Max(s => s.TimeUtc) : null;

        var result = new NightMetricsResult(
            nightId,
            samples.Count,
            framesSkipped,
            first,
            last,
            flicker,
            drift,
            phase,
            exposure,
            starSummary,
            encoder,
            encoderNote,
            Array.Empty<string>());
        return result with { Findings = Findings(result) };
    }

    /// <summary>Righe «Difetto» o «Non emerge» per flicker, deriva, fasi, bande ed encoder.</summary>
    public static IReadOnlyList<string> Findings(NightMetricsResult r)
    {
        var lines = new List<string>();

        if (r.Flicker.StdPercent is { } fs)
        {
            lines.Add(fs >= Thresholds.FlickerStdPercent
                ? $"Difetto, flicker: deviazione {Fmt(fs)} % (P95 |residuo| {Fmt(r.Flicker.P95AbsPercent)} %), soglia {Fmt(Thresholds.FlickerStdPercent)} %."
                : $"Non emerge, flicker: deviazione {Fmt(fs)} % (P95 |residuo| {Fmt(r.Flicker.P95AbsPercent)} %), sotto la soglia {Fmt(Thresholds.FlickerStdPercent)} %.");
        }
        else
        {
            lines.Add("Flicker: dati insufficienti per un giudizio.");
        }

        bool drifting = false;
        foreach ((string name, ChromaDrift.RatioDrift d) in new[] { ("R/G", r.Drift.RG), ("B/G", r.Drift.BG) })
        {
            if (d.ResidualStdPercent is { } p && p >= Thresholds.DriftResidualPercent)
            {
                drifting = true;
            }
        }

        string drift = $"R/G escursione {Fmt(r.Drift.RG.Excursion, 4)}, pendenza {Fmt(r.Drift.RG.SlopePerHour, 4)} /h, residuo {Fmt(r.Drift.RG.ResidualStdPercent)} %; "
                       + $"B/G escursione {Fmt(r.Drift.BG.Excursion, 4)}, pendenza {Fmt(r.Drift.BG.SlopePerHour, 4)} /h, residuo {Fmt(r.Drift.BG.ResidualStdPercent)} %";
        lines.Add(drifting
            ? $"Difetto, deriva cromatica del fondo (residuo sopra {Fmt(Thresholds.DriftResidualPercent)} %): {drift}."
            : $"Non emerge, deriva cromatica sui residui (sotto {Fmt(Thresholds.DriftResidualPercent)} %): {drift}.");

        var jumps = r.Phase.Boundaries
            .Select(b => (b, max: new[] { b.LumaRatio, b.RgRatio, b.BgRatio }.Where(v => v is not null).Select(v => v!.Value).DefaultIfEmpty(0).Max()))
            .ToList();
        if (jumps.Count == 0)
        {
            lines.Add("Non emerge, continuità di fase: nessun confine di fase nei frame analizzati.");
        }

        foreach (var (b, max) in jumps)
        {
            string detail = $"{b.From}→{b.To} alle {b.Time:u}: rapporto salto/tipico luminanza {Fmt(b.LumaRatio)}, R/G {Fmt(b.RgRatio)}, B/G {Fmt(b.BgRatio)}";
            lines.Add(max >= Thresholds.PhaseRatio
                ? $"Difetto, confine di fase: {detail} (soglia {Fmt(Thresholds.PhaseRatio)})."
                : $"Non emerge, confine di fase: {detail}.");
        }

        if (r.Exposure.Ratio is { } er)
        {
            string detail = $"{r.Exposure.Changes} cambi (di cui {r.Exposure.ChangesAtPhaseChange} coincidenti con un cambio di fase); "
                            + $"salto medio relativo {Fmt(r.Exposure.MeanAbsRelJumpAtChanges * 100, 3)} % ai cambi contro {Fmt(r.Exposure.MeanAbsRelJumpWithoutChanges * 100, 3)} % altrove, rapporto {Fmt(er)}";
            lines.Add(er >= Thresholds.ExposureRatio
                ? $"Difetto, bande ai cambi di esposizione/guadagno: {detail} (soglia {Fmt(Thresholds.ExposureRatio)})."
                : $"Non emerge, bande ai cambi: {detail}.");
        }
        else
        {
            lines.Add($"Non emerge, bande ai cambi: {r.Exposure.Changes} cambi, rapporto non calcolabile (nessun passo senza cambio con luminanza positiva).");
        }

        lines.Add(r.Stars.MedianRg is { } srg
            ? $"Stelle (solo numeri, nessun giudizio): {r.Stars.FramesUsed} frame con almeno {StarColor.MinStarsPerFrame} stelle; mediana R/G {Fmt(srg, 4)} (IQR {Fmt(r.Stars.MedianRgQ25, 4)}–{Fmt(r.Stars.MedianRgQ75, 4)}), B/G {Fmt(r.Stars.MedianBg, 4)} (IQR {Fmt(r.Stars.MedianBgQ25, 4)}–{Fmt(r.Stars.MedianBgQ75, 4)})."
            : "Stelle: nessun frame con stelle sufficienti per la mediana.");

        if (r.Encoder is null)
        {
            lines.Add($"Encoder: non verificabile ({r.EncoderNote}).");
        }
        else
        {
            var missing = r.Encoder.Observations.Where(o => o.Contains("non dichiarato", StringComparison.Ordinal)).ToList();
            lines.Add(missing.Count > 0
                ? $"Difetto, metadati colore del video incompleti: {string.Join("; ", missing)}."
                : "Non emerge, metadati colore del video: matrice, range, curva e primari dichiarati.");
        }

        return lines;
    }

    internal static string Fmt(double? v, int digits = 2) =>
        v is { } x && double.IsFinite(x) ? x.ToString($"F{digits}", System.Globalization.CultureInfo.InvariantCulture) : "n.d.";
}
