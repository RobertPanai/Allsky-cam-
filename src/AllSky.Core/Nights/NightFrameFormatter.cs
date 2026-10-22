using System.Globalization;
using AllSky.Core;

namespace AllSky.Core.Nights;

/// <summary>Testi della barra informativa del frame selezionato.</summary>
/// <param name="Position">"Frame 12 di 480", numerato da 1.</param>
/// <param name="Time">Ora locale HH:mm:ss.</param>
/// <param name="TimeTooltip">Istante UTC completo, per il tooltip.</param>
/// <param name="Phase">Fase del cielo nella lingua scelta; "—" se non riconosciuta.</param>
public sealed record NightFrameText(string Position, string Time, string TimeTooltip, string Phase);

/// <summary>Formatta le informazioni del frame selezionato. Logica pura.</summary>
public static class NightFrameFormatter
{
    /// <summary>Formatta il frame selezionato.</summary>
    /// <param name="index">Indice del frame, da 0.</param>
    /// <param name="count">Numero di frame della notte.</param>
    /// <param name="timestamp">Istante di cattura in UTC.</param>
    /// <param name="phase">"day", "twilight" o "night".</param>
    /// <param name="texts">Testi della lingua scelta.</param>
    public static NightFrameText Format(int index, int count, DateTimeOffset timestamp, string phase, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(texts);

        string position = string.Format(texts.Culture, texts.NightsFramePosition, index + 1, count);
        string time = timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        string utc = timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string tooltip = string.Format(texts.Culture, texts.NightsFrameTimeTooltip, utc);

        return new NightFrameText(position, time, tooltip, PhaseName(phase, texts));
    }

    private static string PhaseName(string phase, DesktopTexts texts) => phase switch
    {
        "day" => texts.PhaseDay,
        "twilight" => texts.PhaseTwilight,
        "night" => texts.PhaseNight,
        _ => "—",
    };
}
