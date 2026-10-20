namespace AllSky.Core;

/// <summary>Codifica JPEG di un'immagine RGB a 8 bit (3 byte per pixel, righe contigue, ordine R,G,B).</summary>
public interface IJpegEncoder
{
    /// <summary>Restituisce i byte del file JPEG. quality in 1..100.</summary>
    byte[] Encode(ReadOnlySpan<byte> rgb8, int width, int height, int quality);
}
