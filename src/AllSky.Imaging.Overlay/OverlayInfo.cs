namespace AllSky.Imaging.Overlay;

/// <summary>
/// Dati di un singolo fotogramma da scrivere in sovraimpressione. I campi opzionali a null non vengono disegnati.
/// </summary>
/// <param name="LocalTime">Ora locale dello scatto. Viene formattata come 'yyyy-MM-dd HH:mm:ss' senza conversione di fuso.</param>
/// <param name="SensorTempC">Temperatura del sensore in gradi Celsius, oppure null.</param>
/// <param name="Label">Etichetta o nome, disegnata in alto a sinistra, oppure null o vuota.</param>
/// <param name="ExposureMs">Esposizione in millisecondi, oppure null.</param>
/// <param name="Gain">Gain della camera, oppure null.</param>
/// <param name="PhaseText">Testo di fase (per esempio giorno o notte), disegnato in alto a destra, oppure null.</param>
public sealed record OverlayInfo(
    DateTimeOffset LocalTime,
    double? SensorTempC,
    string? Label,
    double? ExposureMs,
    double? Gain,
    string? PhaseText);
