namespace AllSky.Studio;

/// <summary>Operazione sui pixel, applicata a ogni frame nell'ordine del piano.</summary>
public abstract record PixelOp
{
    /// <summary>Forma testuale stabile, usata per l'impronta del piano.</summary>
    public abstract string Canonical { get; }
}

/// <summary>Ritaglio.</summary>
public sealed record PixelCrop(int X, int Y, int Width, int Height) : PixelOp
{
    /// <inheritdoc />
    public override string Canonical => FormattableString.Invariant($"crop:{X},{Y},{Width},{Height}");
}

/// <summary>Rotazione oraria di 0, 90, 180 o 270 gradi.</summary>
public sealed record PixelRotate(int Degrees) : PixelOp
{
    /// <inheritdoc />
    public override string Canonical => FormattableString.Invariant($"rotate:{Degrees}");
}

/// <summary>Specchio su uno o entrambi gli assi.</summary>
public sealed record PixelMirror(bool Horizontal, bool Vertical) : PixelOp
{
    /// <inheritdoc />
    public override string Canonical => FormattableString.Invariant($"mirror:{Horizontal},{Vertical}");
}

/// <summary>
/// Immagine RGB8 in memoria: byte per canale, riga per riga, da sinistra a destra e dall'alto in basso.
/// La lunghezza del buffer deve essere <c>Width * Height * 3</c>.
/// </summary>
/// <param name="Width">Larghezza in pixel.</param>
/// <param name="Height">Altezza in pixel.</param>
/// <param name="Pixels">Buffer RGB8.</param>
public sealed record RgbImage(int Width, int Height, byte[] Pixels)
{
    /// <summary>Crea un'immagine controllando dimensioni e lunghezza del buffer.</summary>
    /// <exception cref="StudioException">Dimensioni non positive o buffer di lunghezza diversa.</exception>
    public static RgbImage Of(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0)
        {
            throw new StudioException("le dimensioni dell'immagine devono essere positive.");
        }

        if ((long)width * height * 3 != pixels.LongLength)
        {
            throw new StudioException("le dimensioni dell'immagine non corrispondono al buffer RGB.");
        }

        return new RgbImage(width, height, pixels);
    }
}

/// <summary>Operazioni geometriche su buffer RGB8. Ogni metodo restituisce un nuovo buffer.</summary>
public static class PixelOps
{
    /// <summary>Ritaglia un rettangolo che deve stare tutto dentro l'immagine.</summary>
    /// <exception cref="StudioException">Rettangolo vuoto o fuori dall'immagine.</exception>
    public static RgbImage Crop(RgbImage source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0 || height <= 0 || x < 0 || y < 0
            || (long)x + width > source.Width || (long)y + height > source.Height)
        {
            throw new StudioException(
                $"il ritaglio {x},{y} {width}×{height} esce dall'immagine {source.Width}×{source.Height}.");
        }

        var target = new byte[width * height * 3];
        int rowBytes = width * 3;
        for (int row = 0; row < height; row++)
        {
            int sourceOffset = ((y + row) * source.Width + x) * 3;
            Buffer.BlockCopy(source.Pixels, sourceOffset, target, row * rowBytes, rowBytes);
        }

        return new RgbImage(width, height, target);
    }

    /// <summary>Ruota di 90 gradi in senso orario. L'immagine diventa alta quanto era larga.</summary>
    public static RgbImage Rotate90(RgbImage source)
    {
        // (x, y) -> (altezza - 1 - y, x): l'angolo alto-sinistra va in alto-destra.
        return Remap(source, source.Height, source.Width, (x, y) => (source.Height - 1 - y, x));
    }

    /// <summary>Ruota di 180 gradi.</summary>
    public static RgbImage Rotate180(RgbImage source)
    {
        return Remap(source, source.Width, source.Height, (x, y) => (source.Width - 1 - x, source.Height - 1 - y));
    }

    /// <summary>Ruota di 270 gradi in senso orario (90 in senso antiorario).</summary>
    public static RgbImage Rotate270(RgbImage source)
    {
        // (x, y) -> (y, larghezza - 1 - x): l'angolo alto-sinistra va in basso-sinistra.
        return Remap(source, source.Height, source.Width, (x, y) => (y, source.Width - 1 - x));
    }

    /// <summary>Specchio orizzontale (sinistra e destra scambiate).</summary>
    public static RgbImage MirrorH(RgbImage source)
    {
        return Remap(source, source.Width, source.Height, (x, y) => (source.Width - 1 - x, y));
    }

    /// <summary>Specchio verticale (alto e basso scambiati).</summary>
    public static RgbImage MirrorV(RgbImage source)
    {
        return Remap(source, source.Width, source.Height, (x, y) => (x, source.Height - 1 - y));
    }

    private static RgbImage Remap(RgbImage source, int targetWidth, int targetHeight, Func<int, int, (int X, int Y)> map)
    {
        ArgumentNullException.ThrowIfNull(source);
        var target = new byte[targetWidth * targetHeight * 3];
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                (int tx, int ty) = map(x, y);
                int from = (y * source.Width + x) * 3;
                int to = (ty * targetWidth + tx) * 3;
                target[to] = source.Pixels[from];
                target[to + 1] = source.Pixels[from + 1];
                target[to + 2] = source.Pixels[from + 2];
            }
        }

        return new RgbImage(targetWidth, targetHeight, target);
    }
}
