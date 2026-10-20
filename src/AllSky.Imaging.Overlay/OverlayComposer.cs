using System.Globalization;

namespace AllSky.Imaging.Overlay;

/// <summary>
/// Composizione della sovraimpressione di un fotogramma. Disposizione:
/// <list type="bullet">
/// <item>Alto a sinistra: l'etichetta (<see cref="OverlayInfo.Label"/>).</item>
/// <item>Alto a destra: il testo di fase (<see cref="OverlayInfo.PhaseText"/>). Non era specificato: scelta di layout.</item>
/// <item>Basso a sinistra: l'ora locale, se <see cref="OverlayOptions.ShowTime"/>.</item>
/// <item>Basso a destra: su due righe, esposizione e gain sopra, temperatura del sensore sotto.
/// Non era specificato se stessero sulla stessa riga: scelta di layout per tenere i numeri in colonna.</item>
/// </list>
/// Il testo usa solo caratteri del <see cref="BitmapFont"/>: niente frecce e niente punto medio, al loro posto
/// si usa il separatore ASCII " | ".
/// </summary>
public static class OverlayComposer
{
    private const int MarginPerScale = 4;
    private const int LineGapPerScale = 2;

    private static readonly (byte R, byte G, byte B) TextColor = (255, 255, 255);

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// Scrive la sovraimpressione sul buffer RGB. Disegna solo gli elementi presenti; il resto del buffer non viene toccato.
    /// Margini e spaziature scalano con <see cref="OverlayOptions.Scale"/>.
    /// </summary>
    /// <param name="rgb">Buffer RGB a 8 bit, di almeno w * h * 3 byte. Modificato sul posto.</param>
    /// <param name="w">Larghezza in pixel.</param>
    /// <param name="h">Altezza in pixel.</param>
    /// <param name="info">Dati del fotogramma.</param>
    /// <param name="opt">Opzioni di composizione. La lingua deve essere "it" o "en"; la scala tra 1 e 4.</param>
    public static void Compose(Span<byte> rgb, int w, int h, OverlayInfo info, OverlayOptions opt)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(opt);

        string language = NormalizeLanguage(opt.Language);
        OverlayRenderer.ValidateScale(opt.Scale);
        int scale = opt.Scale;
        int margin = MarginPerScale * scale;

        if (!string.IsNullOrEmpty(info.Label))
        {
            OverlayRenderer.DrawTextAnchored(rgb, w, h, info.Label, OverlayAnchor.TopLeft, margin, TextColor, scale, true);
        }

        if (!string.IsNullOrEmpty(info.PhaseText))
        {
            OverlayRenderer.DrawTextAnchored(rgb, w, h, info.PhaseText, OverlayAnchor.TopRight, margin, TextColor, scale, true);
        }

        if (opt.ShowTime)
        {
            OverlayRenderer.DrawTextAnchored(rgb, w, h, FormatLocalTime(info.LocalTime), OverlayAnchor.BottomLeft, margin, TextColor, scale, true);
        }

        // Blocco in basso a destra: la riga piu' in basso e' la temperatura, sopra di essa l'esposizione.
        var bottomRight = new List<string>(2);
        if (opt.ShowSensorTemp && IsFinite(info.SensorTempC))
        {
            bottomRight.Add(FormatTemperature(info.SensorTempC!.Value));
        }

        string? exposure = FormatExposureLine(info.ExposureMs, info.Gain, language);
        if (exposure is not null)
        {
            bottomRight.Add(exposure);
        }

        int step = (BitmapFont.GlyphHeight + LineGapPerScale) * scale;
        for (int i = 0; i < bottomRight.Count; i++)
        {
            (int textW, int textH) = OverlayRenderer.MeasureText(bottomRight[i], scale, true);
            int x = w - margin - textW;
            int y = h - margin - textH - i * step;
            OverlayRenderer.DrawText(rgb, w, h, bottomRight[i], x, y, TextColor, scale, true);
        }
    }

    /// <summary>Ora locale nel formato 'yyyy-MM-dd HH:mm:ss', senza conversione di fuso.</summary>
    /// <param name="localTime">Ora locale dello scatto.</param>
    public static string FormatLocalTime(DateTimeOffset localTime)
    {
        return localTime.ToString("yyyy-MM-dd HH:mm:ss", Invariant);
    }

    /// <summary>
    /// Temperatura del sensore, per esempio "12.3 °C" (un decimale, punto come separatore).
    /// </summary>
    /// <param name="celsius">Temperatura in gradi Celsius, finita.</param>
    public static string FormatTemperature(double celsius)
    {
        return celsius.ToString("0.0", Invariant) + " °C";
    }

    /// <summary>
    /// Riga di esposizione e gain, per esempio "exp 8.0 s | gain 250" (IT: "esp 8.0 s | gain 250").
    /// Esposizioni sotto il secondo si scrivono in millisecondi ("exp 250 ms"). Il gain usa al massimo due decimali.
    /// Restituisce null se non c'e' nessuno dei due valori.
    /// </summary>
    /// <param name="exposureMs">Esposizione in millisecondi, oppure null. Se non e' finita o non e' positiva, viene omessa.</param>
    /// <param name="gain">Gain, oppure null. Se non e' finito, viene omesso.</param>
    /// <param name="language">"it" o "en".</param>
    public static string? FormatExposureLine(double? exposureMs, double? gain, string language)
    {
        string lang = NormalizeLanguage(language);
        var parts = new List<string>(2);

        if (IsFinite(exposureMs) && exposureMs!.Value > 0.0)
        {
            double ms = exposureMs.Value;
            string prefix = lang == "it" ? "esp" : "exp";
            string value = ms >= 1000.0
                ? (ms / 1000.0).ToString("0.0", Invariant) + " s"
                : ms.ToString("0.#", Invariant) + " ms";
            parts.Add(prefix + " " + value);
        }

        if (IsFinite(gain))
        {
            parts.Add("gain " + gain!.Value.ToString("0.##", Invariant));
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    /// <summary>Normalizza la lingua a "it" o "en"; qualunque altro valore e' un errore.</summary>
    /// <param name="language">Codice lingua.</param>
    public static string NormalizeLanguage(string language)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (string.Equals(language, "it", StringComparison.OrdinalIgnoreCase))
        {
            return "it";
        }

        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        throw new ArgumentException("La lingua deve essere 'it' o 'en'.", nameof(language));
    }

    private static bool IsFinite(double? value) => value is { } d && double.IsFinite(d);
}
