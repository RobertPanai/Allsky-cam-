namespace AllSky.Studio;

/// <summary>
/// Anteprima su un singolo frame: applica al buffer RGB8 la lista di operazioni sui pixel del piano.
/// Non tocca i file originali; lavora solo in memoria.
/// </summary>
public static class MontagePreview
{
    /// <summary>Applica le operazioni sui pixel nell'ordine dato.</summary>
    /// <param name="rgb">Buffer RGB8 del frame di origine. Non viene modificato.</param>
    /// <param name="width">Larghezza del frame.</param>
    /// <param name="height">Altezza del frame.</param>
    /// <param name="pixelOps">Operazioni, nell'ordine del piano.</param>
    /// <returns>Nuovo buffer con le sue dimensioni.</returns>
    /// <exception cref="StudioException">Buffer incoerente o ritaglio fuori dall'immagine.</exception>
    public static RgbImage Apply(byte[] rgb, int width, int height, IReadOnlyList<PixelOp> pixelOps)
    {
        ArgumentNullException.ThrowIfNull(pixelOps);
        RgbImage image = RgbImage.Of(width, height, rgb ?? throw new ArgumentNullException(nameof(rgb)));

        foreach (PixelOp op in pixelOps)
        {
            image = op switch
            {
                PixelCrop crop => PixelOps.Crop(image, crop.X, crop.Y, crop.Width, crop.Height),
                PixelRotate rotate => Rotate(image, rotate.Degrees),
                PixelMirror mirror => Mirror(image, mirror.Horizontal, mirror.Vertical),
                _ => throw new InvalidOperationException("operazione sui pixel sconosciuta."),
            };
        }

        return image;
    }

    private static RgbImage Rotate(RgbImage image, int degrees)
    {
        return degrees switch
        {
            0 => image,
            90 => PixelOps.Rotate90(image),
            180 => PixelOps.Rotate180(image),
            270 => PixelOps.Rotate270(image),
            _ => throw new StudioException("rotazione non ammessa: usare 0, 90, 180 o 270 gradi."),
        };
    }

    private static RgbImage Mirror(RgbImage image, bool horizontal, bool vertical)
    {
        if (horizontal)
        {
            image = PixelOps.MirrorH(image);
        }

        return vertical ? PixelOps.MirrorV(image) : image;
    }
}
