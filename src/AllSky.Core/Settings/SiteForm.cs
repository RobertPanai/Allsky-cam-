using System.Globalization;

namespace AllSky.Core.Settings;

/// <summary>Problema di un campo della posizione dell'osservatorio.</summary>
public enum SiteFieldProblem
{
    /// <summary>Nessun problema: il campo è valido.</summary>
    None,

    /// <summary>Campo vuoto.</summary>
    Empty,

    /// <summary>Il testo non è un numero (decimale con punto o con virgola).</summary>
    NotNumber,

    /// <summary>Latitudine fuori da -90..90.</summary>
    LatitudeOutOfRange,

    /// <summary>Longitudine fuori da -180..180.</summary>
    LongitudeOutOfRange,

    /// <summary>Quota fuori dall'intervallo ammesso.</summary>
    ElevationOutOfRange,
}

/// <summary>Esito della validazione della posizione dell'osservatorio.</summary>
/// <param name="Latitude">Latitudine in gradi, null se il campo non è valido.</param>
/// <param name="Longitude">Longitudine in gradi, null se il campo non è valido.</param>
/// <param name="ElevationM">Quota in metri, null se il campo non è valido.</param>
/// <param name="LatitudeProblem">Problema della latitudine.</param>
/// <param name="LongitudeProblem">Problema della longitudine.</param>
/// <param name="ElevationProblem">Problema della quota.</param>
public sealed record SiteCheck(
    double? Latitude,
    double? Longitude,
    double? ElevationM,
    SiteFieldProblem LatitudeProblem,
    SiteFieldProblem LongitudeProblem,
    SiteFieldProblem ElevationProblem)
{
    /// <summary>Vero se tutti i campi sono validi e la posizione si può salvare.</summary>
    public bool IsValid =>
        LatitudeProblem == SiteFieldProblem.None
        && LongitudeProblem == SiteFieldProblem.None
        && ElevationProblem == SiteFieldProblem.None;
}

/// <summary>
/// Validazione del form "Posizione dell'osservatorio": latitudine -90..90, longitudine -180..180 (est positiva),
/// quota -500..9000 m (dal Mar Morto alle quote alpine e andine). Accetta il separatore decimale della lingua
/// e anche il punto. Logica pura.
/// </summary>
public static class SiteForm
{
    /// <summary>Quota minima ammessa, in metri (sotto il livello del mare: Mar Morto circa -430 m).</summary>
    public const double MinElevationM = -500;

    /// <summary>Quota massima ammessa, in metri.</summary>
    public const double MaxElevationM = 9000;

    /// <summary>Valida i tre campi come testo digitato.</summary>
    /// <param name="latitudeText">Testo della latitudine.</param>
    /// <param name="longitudeText">Testo della longitudine.</param>
    /// <param name="elevationText">Testo della quota.</param>
    /// <param name="culture">Cultura dell'interfaccia.</param>
    public static SiteCheck Check(string? latitudeText, string? longitudeText, string? elevationText, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        (double? latitude, SiteFieldProblem latitudeProblem) = CheckRange(latitudeText, culture, -90, 90, SiteFieldProblem.LatitudeOutOfRange);
        (double? longitude, SiteFieldProblem longitudeProblem) = CheckRange(longitudeText, culture, -180, 180, SiteFieldProblem.LongitudeOutOfRange);
        (double? elevation, SiteFieldProblem elevationProblem) = CheckRange(elevationText, culture, MinElevationM, MaxElevationM, SiteFieldProblem.ElevationOutOfRange);
        return new SiteCheck(latitude, longitude, elevation, latitudeProblem, longitudeProblem, elevationProblem);
    }

    /// <summary>Testo di un valore da mostrare nel campo, con la cultura dell'interfaccia e senza arrotondare.</summary>
    /// <param name="value">Valore in gradi o metri.</param>
    /// <param name="culture">Cultura dell'interfaccia.</param>
    public static string Format(double value, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return value.ToString("R", culture);
    }

    /// <summary>Vero se la posizione valida differisce da quella attuale.</summary>
    /// <param name="check">Esito di <see cref="Check"/>.</param>
    /// <param name="latitude">Latitudine attuale.</param>
    /// <param name="longitude">Longitudine attuale.</param>
    /// <param name="elevation">Quota attuale.</param>
    public static bool HasChanges(SiteCheck check, double latitude, double longitude, double elevation)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!check.IsValid)
        {
            return false;
        }

        return Differs(check.Latitude!.Value, latitude)
            || Differs(check.Longitude!.Value, longitude)
            || Differs(check.ElevationM!.Value, elevation);
    }

    private static bool Differs(double a, double b) => Math.Abs(a - b) > 1e-9;

    private static (double? Value, SiteFieldProblem Problem) CheckRange(
        string? text, CultureInfo culture, double min, double max, SiteFieldProblem outOfRange)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return (null, SiteFieldProblem.Empty);
        }

        if (!TryParse(trimmed, culture, out double value) || !double.IsFinite(value))
        {
            return (null, SiteFieldProblem.NotNumber);
        }

        if (value < min || value > max)
        {
            return (null, outOfRange);
        }

        return (value, SiteFieldProblem.None);
    }

    private static bool TryParse(string text, CultureInfo culture, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, culture, out value))
        {
            return true;
        }

        // Si accetta anche il punto, anche dove la lingua usa la virgola.
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
