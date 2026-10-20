namespace AllSky.Capture;

/// <summary>Informazioni sull'ultimo frame catturato con successo.</summary>
/// <param name="Timestamp">Istante del frame, come riportato dalla camera.</param>
/// <param name="ExposureMs">Esposizione del frame in millisecondi.</param>
/// <param name="Gain">Gain del frame.</param>
public sealed record FrameInfo(DateTimeOffset Timestamp, double ExposureMs, double Gain);
