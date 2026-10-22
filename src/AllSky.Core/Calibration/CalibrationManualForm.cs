using System.Globalization;
using AllSky.Sky;

namespace AllSky.Core.Calibration;

/// <summary>Problema di un campo della modalita' manuale.</summary>
public enum ManualFieldProblem
{
    /// <summary>Nessun problema.</summary>
    None,

    /// <summary>Campo vuoto.</summary>
    Empty,

    /// <summary>Il testo non e' un numero finito.</summary>
    NotNumber,

    /// <summary>Il raggio non e' maggiore di zero.</summary>
    NotPositive,

    /// <summary>K2 non e' maggiore di -1/3.</summary>
    OutOfRange,
}

/// <summary>
/// Esito della lettura del form manuale. <see cref="Model"/> e' null se almeno un campo non e' valido.
/// </summary>
public sealed record ManualCalibrationCheck(
    FisheyeModel? Model,
    ManualFieldProblem CenterXProblem,
    ManualFieldProblem CenterYProblem,
    ManualFieldProblem RadiusProblem,
    ManualFieldProblem K2Problem,
    ManualFieldProblem RotationProblem)
{
    /// <summary>Vero se tutti i campi sono validi.</summary>
    public bool IsValid => Model is not null;
}

/// <summary>
/// Lettura e validazione dei campi della modalita' manuale: centro x/y, raggio dell'orizzonte in pixel, K2, rotazione del Nord
/// e specchio. Il raggio e' quello dell'orizzonte effettivo (<see cref="FisheyeModel.HorizonRadius"/>), perche' e' l'unico che
/// l'utente vede sull'immagine; il raggio di scala si ricava con ScaleRadiusPx = orizzonte / (1 + K2).
/// </summary>
public static class CalibrationManualForm
{
    /// <summary>Legge i campi come testo digitato. Accetta il separatore della cultura e anche il punto.</summary>
    public static ManualCalibrationCheck Check(
        string? centerXText,
        string? centerYText,
        string? radiusText,
        string? k2Text,
        string? rotationText,
        bool mirrored,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        (double cx, ManualFieldProblem cxP) = Parse(centerXText, culture);
        (double cy, ManualFieldProblem cyP) = Parse(centerYText, culture);
        (double radius, ManualFieldProblem radiusP) = Parse(radiusText, culture);
        (double k2, ManualFieldProblem k2P) = Parse(k2Text, culture);
        (double rot, ManualFieldProblem rotP) = Parse(rotationText, culture);

        if (radiusP == ManualFieldProblem.None && radius <= 0.0)
        {
            radiusP = ManualFieldProblem.NotPositive;
        }

        if (k2P == ManualFieldProblem.None && k2 <= -1.0 / 3.0)
        {
            k2P = ManualFieldProblem.OutOfRange;
        }

        FisheyeModel? model = null;
        bool allValid = cxP == ManualFieldProblem.None && cyP == ManualFieldProblem.None
            && radiusP == ManualFieldProblem.None && k2P == ManualFieldProblem.None && rotP == ManualFieldProblem.None;
        if (allValid)
        {
            model = new FisheyeModel(cx, cy, radius / (1.0 + k2), rot, mirrored, k2);
        }

        return new ManualCalibrationCheck(model, cxP, cyP, radiusP, k2P, rotP);
    }

    /// <summary>Testo di un valore per il campo del form, con la cultura dell'interfaccia.</summary>
    public static string Format(double value, CultureInfo culture, int decimals = 1) =>
        value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), culture);

    private static (double Value, ManualFieldProblem Problem) Parse(string? text, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (0.0, ManualFieldProblem.Empty);
        }

        string trimmed = text.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, culture, out double value)
            && !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return (0.0, ManualFieldProblem.NotNumber);
        }

        return double.IsFinite(value) ? (value, ManualFieldProblem.None) : (0.0, ManualFieldProblem.NotNumber);
    }
}
