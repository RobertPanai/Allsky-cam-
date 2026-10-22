using AllSky.Sky;
using AllSky.Sky.Calibration;

namespace AllSky.Core.Calibration;

/// <summary>Regole pure del flusso di calibrazione: fase, residui e errore medio.</summary>
public static class CalibrationFlow
{
    /// <summary>Fase dal fatto che ci sia una proposta in sospeso e un modello accettato.</summary>
    public static CalibrationPhase PhaseOf(bool hasProposal, bool hasAccepted)
    {
        if (hasProposal)
        {
            return CalibrationPhase.Review;
        }

        return hasAccepted ? CalibrationPhase.Calibrated : CalibrationPhase.Collecting;
    }

    /// <summary>Residuo di ogni punto rispetto al modello. Un punto che il modello non sa proiettare ha errore NaN.</summary>
    public static IReadOnlyList<CalibrationResidual> Residuals(FisheyeModel model, IReadOnlyList<CalibrationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(points);

        var result = new List<CalibrationResidual>(points.Count);
        foreach (CalibrationPoint p in points)
        {
            if (model.TryAltAzToPixel(p.AltDeg, p.AzDeg, out double px, out double py))
            {
                double error = Math.Sqrt((px - p.X) * (px - p.X) + (py - p.Y) * (py - p.Y));
                result.Add(new CalibrationResidual(p, new PixelPoint(px, py), error));
            }
            else
            {
                result.Add(new CalibrationResidual(p, null, double.NaN));
            }
        }

        return result;
    }

    /// <summary>Errore radiale quadratico medio dei residui validi, in pixel. Zero se non ce ne sono.</summary>
    public static double RmsPx(IReadOnlyList<CalibrationResidual> residuals)
    {
        ArgumentNullException.ThrowIfNull(residuals);

        double sum = 0.0;
        int n = 0;
        foreach (CalibrationResidual r in residuals)
        {
            if (double.IsFinite(r.ErrorPx))
            {
                sum += r.ErrorPx * r.ErrorPx;
                n++;
            }
        }

        return n == 0 ? 0.0 : Math.Sqrt(sum / n);
    }

    /// <summary>Vero se l'errore frazionario e' sotto la soglia di accettazione (stessa regola di <see cref="SunCalibrationCollector"/>).</summary>
    public static bool IsAcceptableFraction(double rmsFractionOfRadius) =>
        double.IsFinite(rmsFractionOfRadius) && rmsFractionOfRadius < SunCalibrationCollector.AcceptanceFraction;

    /// <summary>Soglia di accettazione come percentuale del raggio, per i messaggi.</summary>
    public static double AcceptancePercent => SunCalibrationCollector.AcceptanceFraction * 100.0;
}
