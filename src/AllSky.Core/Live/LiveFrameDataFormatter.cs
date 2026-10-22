using System.Globalization;

namespace AllSky.Core.Live;

/// <summary>Campi del pannello dati del frame, nell'ordine in cui compaiono.</summary>
public enum FrameDataField
{
    /// <summary>Ora locale di cattura, con l'identificativo UTC nel tooltip.</summary>
    Time,

    /// <summary>Esposizione.</summary>
    Exposure,

    /// <summary>Gain della camera.</summary>
    Gain,

    /// <summary>Mediana ADU del frame grezzo.</summary>
    MedianAdu,

    /// <summary>Temperatura del sensore.</summary>
    SensorTemp,
}

/// <summary>
/// Dati grezzi del frame per il pannello della vista Live. Campi nulli quando il servizio non li fornisce.
/// Non dipende dai DTO dell'Engine: la vista li copia da lì.
/// </summary>
/// <param name="FrameId">Identificativo del frame, formato yyyyMMddTHHmmssZ.</param>
/// <param name="Timestamp">Istante di cattura in UTC.</param>
/// <param name="ExposureMs">Esposizione in millisecondi.</param>
/// <param name="Gain">Gain della camera.</param>
/// <param name="MedianAdu">Mediana ADU del frame grezzo.</param>
/// <param name="SensorTempC">Temperatura del sensore in gradi Celsius.</param>
public sealed record LiveFrameData(
    string? FrameId,
    DateTimeOffset? Timestamp,
    double? ExposureMs,
    double? Gain,
    int? MedianAdu,
    double? SensorTempC)
{
    /// <summary>Nessun frame: tutti i campi mancanti.</summary>
    public static LiveFrameData Empty { get; } = new(null, null, null, null, null, null);
}

/// <summary>Una riga del pannello dati, già formattata nella lingua scelta.</summary>
/// <param name="Field">Campo a cui si riferisce la riga.</param>
/// <param name="Label">Etichetta.</param>
/// <param name="Value">Valore formattato, o "n/d" se manca.</param>
/// <param name="Available">Falso se il valore manca.</param>
/// <param name="Tooltip">Spiegazione al passaggio del mouse; null se non serve.</param>
public sealed record FrameDataItem(FrameDataField Field, string Label, string Value, bool Available, string? Tooltip);

/// <summary>
/// Formatta il pannello dati del frame della vista Live. Logica pura: un dato mancante o non valido diventa "n/d"
/// con la spiegazione nel tooltip, mai uno zero.
/// </summary>
public static class LiveFrameDataFormatter
{
    /// <summary>Limite di buon senso per la temperatura del sensore: fuori da questo intervallo il valore è sospetto.</summary>
    private const double MaxSensorTempAbsC = 100;

    /// <summary>Formatta tutte le righe del pannello, sempre nello stesso ordine di <see cref="FrameDataField"/>.</summary>
    /// <param name="data">Dati del frame, o <see cref="LiveFrameData.Empty"/>.</param>
    /// <param name="texts">Testi e cultura della lingua scelta.</param>
    public static IReadOnlyList<FrameDataItem> Format(LiveFrameData data, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(texts);

        CultureInfo culture = texts.Culture;
        return
        [
            FormatTime(data, texts),
            Known(FrameDataField.Exposure, texts.LiveDataExposure, FormatExposure(data.ExposureMs, culture), texts),
            Known(FrameDataField.Gain, texts.LiveDataGain, FormatGain(data.Gain, culture), texts),
            Known(FrameDataField.MedianAdu, texts.LiveDataMedian, FormatMedian(data.MedianAdu, culture), texts),
            Known(FrameDataField.SensorTemp, texts.LiveDataTemperature, FormatTemperature(data.SensorTempC, culture), texts),
        ];
    }

    /// <summary>
    /// Esposizione: millisecondi sotto il secondo ("0.5 ms", "120 ms"), secondi sopra ("30.0 s").
    /// Null se il valore non è finito o è negativo.
    /// </summary>
    /// <param name="milliseconds">Esposizione in millisecondi.</param>
    /// <param name="culture">Cultura per il separatore decimale.</param>
    public static string? FormatExposure(double? milliseconds, CultureInfo culture)
    {
        if (!IsNonNegative(milliseconds))
        {
            return null;
        }

        double ms = milliseconds!.Value;
        return ms >= 1000
            ? (ms / 1000).ToString("0.0", culture) + " s"
            : ms.ToString("0.##", culture) + " ms";
    }

    /// <summary>Gain senza unità, con al massimo due decimali. Null se non valido.</summary>
    /// <param name="gain">Gain della camera.</param>
    /// <param name="culture">Cultura per il separatore decimale.</param>
    public static string? FormatGain(double? gain, CultureInfo culture)
    {
        return IsNonNegative(gain) ? gain!.Value.ToString("0.##", culture) : null;
    }

    /// <summary>Mediana ADU intera con l'unità "ADU". Null se negativa.</summary>
    /// <param name="median">Mediana ADU.</param>
    /// <param name="culture">Cultura.</param>
    public static string? FormatMedian(int? median, CultureInfo culture)
    {
        return median is { } value and >= 0 ? value.ToString("0", culture) + " ADU" : null;
    }

    /// <summary>Temperatura con un decimale e unità, per esempio "-10,5 °C". Null se fuori dall'intervallo ammesso.</summary>
    /// <param name="celsius">Temperatura in gradi Celsius.</param>
    /// <param name="culture">Cultura per il separatore decimale.</param>
    public static string? FormatTemperature(double? celsius, CultureInfo culture)
    {
        if (celsius is not { } value || !double.IsFinite(value) || Math.Abs(value) > MaxSensorTempAbsC)
        {
            return null;
        }

        return value.ToString("0.0", culture) + " °C";
    }

    private static FrameDataItem FormatTime(LiveFrameData data, DesktopTexts texts)
    {
        const FrameDataField field = FrameDataField.Time;
        if (data.Timestamp is not { } timestamp)
        {
            return Missing(field, texts.LiveDataTime, texts);
        }

        string value = timestamp.ToLocalTime().ToString("HH:mm:ss", texts.Culture);
        string? tooltip = string.IsNullOrWhiteSpace(data.FrameId)
            ? null
            : string.Format(texts.Culture, texts.LiveDataTimeTooltip, data.FrameId);
        return new FrameDataItem(field, texts.LiveDataTime, value, true, tooltip);
    }

    private static FrameDataItem Known(FrameDataField field, string label, string? value, DesktopTexts texts)
    {
        return value is null ? Missing(field, label, texts) : new FrameDataItem(field, label, value, true, null);
    }

    private static FrameDataItem Missing(FrameDataField field, string label, DesktopTexts texts)
    {
        return new FrameDataItem(field, label, texts.NotAvailable, false, texts.NotAvailableTooltip);
    }

    private static bool IsNonNegative(double? value) => value is { } v && double.IsFinite(v) && v >= 0;
}
