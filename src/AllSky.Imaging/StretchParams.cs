namespace AllSky.Imaging;

/// <summary>
/// Parametri di stretch stimati da un'immagine: punto nero per canale, punto bianco comune e midtone.
/// Sono i campi che l'Host puo' levigare fra un frame e l'altro (vedi <see cref="StretchParamsSmoother"/>).
/// </summary>
public readonly record struct StretchParams(float BlackR, float BlackG, float BlackB, float White, float Midtone);
