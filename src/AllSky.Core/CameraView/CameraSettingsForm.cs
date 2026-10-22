using System.Globalization;

namespace AllSky.Core.CameraView;

/// <summary>Problema di un campo del form, da tradurre con <see cref="CameraFieldMessages"/>.</summary>
public enum CameraFieldProblem
{
    /// <summary>Nessun problema: il campo è valido.</summary>
    None,

    /// <summary>Campo vuoto.</summary>
    Empty,

    /// <summary>Il testo non è un numero.</summary>
    NotNumber,

    /// <summary>Il guadagno non è un numero intero.</summary>
    NotInteger,

    /// <summary>Il guadagno è negativo.</summary>
    Negative,

    /// <summary>Il valore non è maggiore di zero (o non è finito).</summary>
    NotPositive,
}

/// <summary>Esito della validazione del form delle impostazioni di cattura.</summary>
/// <param name="Gain">Guadagno letto; null se il campo non è valido.</param>
/// <param name="MaxExposureMs">Esposizione massima in millisecondi; null se il campo non è valido.</param>
/// <param name="GainProblem">Problema del campo guadagno.</param>
/// <param name="MaxExposureProblem">Problema del campo esposizione massima.</param>
public sealed record CameraSettingsCheck(
    int? Gain,
    double? MaxExposureMs,
    CameraFieldProblem GainProblem,
    CameraFieldProblem MaxExposureProblem)
{
    /// <summary>Vero se entrambi i campi sono validi e si possono salvare.</summary>
    public bool IsValid => GainProblem == CameraFieldProblem.None && MaxExposureProblem == CameraFieldProblem.None;
}

/// <summary>
/// Validazione e confronto del form "Impostazioni di cattura" (guadagno ed esposizione massima).
/// Le regole sono quelle dell'Engine (<c>ValidateAll</c>): guadagno intero non negativo, esposizione finita e maggiore di zero.
/// Logica pura, con la cultura dei testi passata dal chiamante.
/// </summary>
public static class CameraSettingsForm
{
    /// <summary>Differenza minima, in millisecondi, sotto cui due esposizioni sono considerate uguali.</summary>
    public const double ExposureTolerance = 1e-9;

    /// <summary>Valida i due campi come testo digitato dall'utente.</summary>
    /// <param name="gainText">Testo del campo guadagno.</param>
    /// <param name="maxExposureText">Testo del campo esposizione massima, in millisecondi.</param>
    /// <param name="culture">Cultura dell'interfaccia: il separatore decimale è quello della lingua; si accetta anche il punto.</param>
    public static CameraSettingsCheck Check(string? gainText, string? maxExposureText, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        (int? gain, CameraFieldProblem gainProblem) = CheckGain(gainText, culture);
        (double? exposure, CameraFieldProblem exposureProblem) = CheckExposure(maxExposureText, culture);
        return new CameraSettingsCheck(gain, exposure, gainProblem, exposureProblem);
    }

    /// <summary>Vero se il form è valido e differisce dalla configurazione attuale.</summary>
    /// <param name="check">Esito di <see cref="Check"/>.</param>
    /// <param name="currentGain">Guadagno della configurazione.</param>
    /// <param name="currentMaxExposureMs">Esposizione massima della configurazione, in millisecondi.</param>
    public static bool HasChanges(CameraSettingsCheck check, int currentGain, double currentMaxExposureMs)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (!check.IsValid)
        {
            return false;
        }

        return check.Gain != currentGain
            || Math.Abs(check.MaxExposureMs!.Value - currentMaxExposureMs) > ExposureTolerance;
    }

    /// <summary>Testo di un valore da mostrare nel campo, senza arrotondare l'esposizione.</summary>
    /// <param name="maxExposureMs">Esposizione in millisecondi.</param>
    /// <param name="culture">Cultura dell'interfaccia.</param>
    public static string FormatExposure(double maxExposureMs, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return maxExposureMs.ToString("R", culture);
    }

    /// <summary>Testo del guadagno da mostrare nel campo.</summary>
    /// <param name="gain">Guadagno.</param>
    public static string FormatGain(int gain) => gain.ToString(CultureInfo.InvariantCulture);

    private static (int? Value, CameraFieldProblem Problem) CheckGain(string? text, CultureInfo culture)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return (null, CameraFieldProblem.Empty);
        }

        if (!TryParseNumber(trimmed, culture, out double value))
        {
            return (null, CameraFieldProblem.NotNumber);
        }

        if (!double.IsFinite(value) || value != Math.Floor(value) || value > int.MaxValue)
        {
            return (null, CameraFieldProblem.NotInteger);
        }

        if (value < 0)
        {
            return (null, CameraFieldProblem.Negative);
        }

        return ((int)value, CameraFieldProblem.None);
    }

    private static (double? Value, CameraFieldProblem Problem) CheckExposure(string? text, CultureInfo culture)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return (null, CameraFieldProblem.Empty);
        }

        if (!TryParseNumber(trimmed, culture, out double value))
        {
            return (null, CameraFieldProblem.NotNumber);
        }

        if (!double.IsFinite(value) || value <= 0)
        {
            return (null, CameraFieldProblem.NotPositive);
        }

        return (value, CameraFieldProblem.None);
    }

    /// <summary>Prova prima la cultura dell'interfaccia, poi il punto decimale invariante.</summary>
    private static bool TryParseNumber(string text, CultureInfo culture, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, culture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
