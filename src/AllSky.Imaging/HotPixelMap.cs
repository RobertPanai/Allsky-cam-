namespace AllSky.Imaging;

/// <summary>
/// Mappa dei pixel caldi di un sensore: indici lineari (y * Width + x) dei pixel da correggere, in ordine crescente.
/// Gli array usano uguaglianza per riferimento.
/// </summary>
/// <param name="Width">Larghezza del sensore in pixel.</param>
/// <param name="Height">Altezza del sensore in pixel.</param>
/// <param name="Indices">Indici lineari dei pixel caldi, ordinati in modo crescente.</param>
public sealed record HotPixelMap(int Width, int Height, int[] Indices);
