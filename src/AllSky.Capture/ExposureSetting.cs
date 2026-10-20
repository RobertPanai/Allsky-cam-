namespace AllSky.Capture;

/// <summary>
/// Impostazione di cattura: tempo di esposizione in millisecondi e gain della camera.
/// </summary>
/// <param name="ExposureMs">Tempo di esposizione in millisecondi.</param>
/// <param name="Gain">Gain della camera, nelle unità del driver (200 unità corrispondono a un fattore 10 sul segnale).</param>
public readonly record struct ExposureSetting(double ExposureMs, double Gain);
