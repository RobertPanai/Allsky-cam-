namespace AllSky.Publish;

/// <summary>
/// Immagine pronta per l'invio: JPEG codificato, dimensioni effettive e hash del contenuto.
/// </summary>
/// <param name="Jpeg">File JPEG completo (SOI ... EOI).</param>
/// <param name="Width">Larghezza effettiva in pixel.</param>
/// <param name="Height">Altezza effettiva in pixel.</param>
/// <param name="ContentHash">FNV-1a 64 bit dei byte RGB dell'immagine alla risoluzione di pubblicazione (prima di ogni riduzione di emergenza).</param>
public sealed record PreparedImage(byte[] Jpeg, int Width, int Height, ulong ContentHash);
