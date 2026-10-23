namespace AllSky.ColorMetrics;

/// <summary>
/// Immagine RGB a 8 bit, pixel interleaved (R,G,B), righe contigue dall'alto in basso.
/// È il formato in ingresso di <see cref="FrameSampler"/>; la decodifica del JPEG sta fuori da questa libreria.
/// </summary>
public sealed class Rgb8Image
{
    /// <summary>Crea l'immagine. Lancia <see cref="ArgumentException"/> se la lunghezza non è width*height*3.</summary>
    public Rgb8Image(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Le dimensioni devono essere positive.");
        }

        if ((long)width * height * 3 != pixels.Length)
        {
            throw new ArgumentException("La lunghezza dei pixel deve essere width*height*3.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Pixel RGB 8 bit, interleaved.</summary>
    public byte[] Pixels { get; }
}

/// <summary>Luminanza e utilità di colore. Luminanza Rec.709 su valori 0..1.</summary>
public static class ColorMath
{
    /// <summary>Luminanza Rec.709 (0.2126 R + 0.7152 G + 0.0722 B) di un pixel con componenti 0..1.</summary>
    public static double Luma(double r, double g, double b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;

    /// <summary>Luminanza Rec.709 di un pixel 8 bit, risultato in 0..1.</summary>
    public static double Luma(byte r, byte g, byte b) => Luma(r / 255.0, g / 255.0, b / 255.0);
}
