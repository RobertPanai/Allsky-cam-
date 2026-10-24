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

    /// <summary>L'esposizione manuale supera l'esposizione massima.</summary>
    AboveMaximum,
}

/// <summary>Esito della validazione del form delle impostazioni di cattura.</summary>
/// <param name="Gain">Guadagno letto; null se il campo non è valido.</param>
/// <param name="MaxExposureMs">Esposizione massima in millisecondi; null se il campo non è valido.</param>
/// <param name="GainProblem">Problema del campo guadagno.</param>
/// <param name="MaxExposureProblem">Problema del campo esposizione massima.</param>
/// <param name="Mode">Modalità di esposizione scelta nel form.</param>
/// <param name="ManualExposureMs">Esposizione manuale in millisecondi; null se non richiesta o non valida.</param>
/// <param name="ManualExposureProblem">Problema del campo esposizione manuale; None se la modalità è automatica.</param>
public sealed record CameraSettingsCheck(
    int? Gain,
    double? MaxExposureMs,
    CameraFieldProblem GainProblem,
    CameraFieldProblem MaxExposureProblem,
    AllSky.Core.CameraExposureMode Mode,
    double? ManualExposureMs,
    CameraFieldProblem ManualExposureProblem)
{
    /// <summary>Vero se tutti i campi richiesti dalla modalità sono validi e si possono salvare.</summary>
    public bool IsValid => GainProblem == CameraFieldProblem.None
        && MaxExposureProblem == CameraFieldProblem.None
        && ManualExposureProblem == CameraFieldProblem.None;
}

/// <summary>
/// Validazione e confronto del form "Impostazioni di cattura": guadagno, esposizione massima e, in modalità manuale,
/// esposizione fissa. Le regole sono quelle dell'Engine (<c>ValidateAll</c>): guadagno intero non negativo, esposizioni
/// finite e maggiori di zero, esposizione manuale non superiore alla massima.
/// Logica pura, con la cultura dei testi passata dal chiamante.
/// </summary>
public static class CameraSettingsForm
{
    /// <summary>Differenza minima, in millisecondi, sotto cui due esposizioni sono considerate uguali.</summary>
    public const double ExposureTolerance = 1e-9;

    /// <summary>Valida i campi come testo digitato dall'utente.</summary>
    /// <param name="gainText">Testo del campo guadagno.</param>
    /// <param name="maxExposureText">Testo del campo esposizione massima, in millisecondi.</param>
    /// <param name="mode">Modalità scelta. Il campo manuale si valida solo in modalità manuale.</param>
    /// <param name="manualExposureText">Testo del campo esposizione manuale, in millisecondi.</param>
    /// <param name="culture">Cultura dell'interfaccia: il separatore decimale è quello della lingua; si accetta anche il punto.</param>
    public static CameraSettingsCheck Check(
        string? gainText,
        string? maxExposureText,
        AllSky.Core.CameraExposureMode mode,
        string? manualExposureText,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        (int? gain, CameraFieldProblem gainProblem) = CheckGain(gainText, culture);
        (double? exposure, CameraFieldProblem exposureProblem) = CheckExposure(maxExposureText, culture);

        if (mode != AllSky.Core.CameraExposureMode.Manual)
        {
            return new CameraSettingsCheck(gain, exposure, gainProblem, exposureProblem, mode, null, CameraFieldProblem.None);
        }

        (double? manual, CameraFieldProblem manualProblem) = CheckExposure(manualExposureText, culture);
        if (manualProblem == CameraFieldProblem.None && exposure is { } max && manual!.Value > max)
        {
            manualProblem = CameraFieldProblem.AboveMaximum;
        }

        return new CameraSettingsCheck(gain, exposure, gainProblem, exposureProblem, mode, manual, manualProblem);
    }

    /// <summary>Vero se il form è valido e differisce dalla configurazione attuale.</summary>
    /// <param name="check">Esito di <see cref="Check"/>.</param>
    /// <param name="current">Sezione camera della configurazione attuale.</param>
    public static bool HasChanges(CameraSettingsCheck check, AllSky.Core.AppConfig.CameraConfig current)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(current);

        if (!check.IsValid)
        {
            return false;
        }

        if (check.Gain != current.Gain
            || Math.Abs(check.MaxExposureMs!.Value - current.MaxExposureMs) > ExposureTolerance
            || check.Mode != current.ExposureMode)
        {
            return true;
        }

        // Il valore manuale conta solo in modalità manuale: in automatico il campo non viene letto.
        return check.Mode == AllSky.Core.CameraExposureMode.Manual
            && Math.Abs(check.ManualExposureMs!.Value - current.ManualExposureMs) > ExposureTolerance;
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
