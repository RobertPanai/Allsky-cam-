namespace AllSky.Astro;

/// <summary>
/// Posizione di un astro sulla volta celeste dell'osservatore.
/// <see cref="AltitudeDeg"/>: altezza sull'orizzonte geometrico, negativa sotto l'orizzonte.
/// <see cref="AzimuthDeg"/>: azimut in gradi, misurato da nord (0) verso est (90), in [0, 360).
/// </summary>
public readonly record struct HorizontalPosition(double AltitudeDeg, double AzimuthDeg);
