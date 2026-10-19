namespace AllSky.Imaging;

/// <summary>
/// Immagine RGB a tre piani separati, in ordine row-major (indice = y * Width + x).
/// I valori sono float nell'intervallo 0..65535.
/// </summary>
public sealed record RgbImage(int Width, int Height, float[] R, float[] G, float[] B);
