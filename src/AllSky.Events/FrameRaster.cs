namespace AllSky.Events;

/// <summary>Area quadrata di un frame da ritagliare.</summary>
public sealed record CropRect
{
    /// <summary>Colonna del bordo sinistro, in pixel del frame.</summary>
    public int X0 { get; init; }

    /// <summary>Riga del bordo superiore, in pixel del frame.</summary>
    public int Y0 { get; init; }

    /// <summary>Lato del quadrato, in pixel del frame.</summary>
    public int Side { get; init; }
}

/// <summary>
/// Operazioni sui pixel RGB a 8 bit (righe contigue, R,G,B) per ritagli e evidenziazioni.
/// Solo BCL: nessuna libreria grafica. Ogni scrittura fuori immagine viene ignorata.
/// </summary>
public static class FrameRaster
{
    /// <summary>Copia del buffer RGB.</summary>
    public static byte[] Copy(byte[] rgb)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        return (byte[])rgb.Clone();
    }

    /// <summary>
    /// Massimo per pixel e per canale di una sequenza di frame RGB della stessa dimensione (immagine composita di un transito).
    /// Il risultato e' un buffer nuovo; i frame in ingresso non vengono modificati.
    /// </summary>
    /// <param name="frames">Frame RGB 8 bit, riga per riga. Almeno uno.</param>
    /// <param name="width">Larghezza comune dei frame.</param>
    /// <param name="height">Altezza comune dei frame.</param>
    public static byte[] MaxPerPixel(IEnumerable<byte[]> frames, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Il frame deve avere dimensioni positive.");
        }

        int length = checked(width * height * 3);
        byte[]? output = null;
        foreach (byte[] rgb in frames)
        {
            ArgumentNullException.ThrowIfNull(rgb);
            if (rgb.Length != length)
            {
                throw new ArgumentException("Tutti i frame devono avere Width*Height*3 byte.", nameof(frames));
            }

            if (output is null)
            {
                output = (byte[])rgb.Clone();
                continue;
            }

            for (int i = 0; i < length; i++)
            {
                if (rgb[i] > output[i])
                {
                    output[i] = rgb[i];
                }
            }
        }

        return output ?? throw new ArgumentException("Serve almeno un frame.", nameof(frames));
    }

    /// <summary>
    /// Quadrato centrato sulla scia (bounding box in pixel del frame), con margine e lato minimo.
    /// Il lato non supera il lato corto del frame e il quadrato resta dentro il frame.
    /// </summary>
    public static CropRect ComputeCrop(int width, int height, double minX, double minY, double maxX, double maxY, int minSide, int marginPx)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Il frame deve avere dimensioni positive.");
        }

        int limit = Math.Min(width, height);
        double extent = Math.Max(maxX - minX, maxY - minY);
        double wanted = Math.Max(extent + 2.0 * marginPx, Math.Min(minSide, limit));
        int side = Math.Clamp((int)Math.Ceiling(wanted), 1, limit);

        double centerX = (minX + maxX) / 2.0;
        double centerY = (minY + maxY) / 2.0;
        int x0 = (int)Math.Round(Math.Clamp(centerX - side / 2.0, 0.0, width - side));
        int y0 = (int)Math.Round(Math.Clamp(centerY - side / 2.0, 0.0, height - side));
        return new CropRect { X0 = x0, Y0 = y0, Side = side };
    }

    /// <summary>
    /// Ritaglio ingrandito con replica dei pixel (nessuna interpolazione): ogni pixel sorgente diventa un blocco
    /// <paramref name="factor"/> x <paramref name="factor"/>. Restituisce un buffer di (Side*factor)^2 pixel.
    /// </summary>
    public static byte[] ExtractMagnified(byte[] rgb, int width, int height, CropRect crop, int factor)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        if (factor < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "L'ingrandimento deve essere almeno 1.");
        }

        int size = crop.Side * factor;
        var output = new byte[size * size * 3];
        for (int oy = 0; oy < size; oy++)
        {
            int sy = crop.Y0 + oy / factor;
            for (int ox = 0; ox < size; ox++)
            {
                int sx = crop.X0 + ox / factor;
                if (sx >= width || sy >= height)
                {
                    continue;
                }

                int source = (sy * width + sx) * 3;
                int target = (oy * size + ox) * 3;
                output[target] = rgb[source];
                output[target + 1] = rgb[source + 1];
                output[target + 2] = rgb[source + 2];
            }
        }
        return output;
    }

    /// <summary>Segmento disegnato con un pennello quadrato di lato <paramref name="thickness"/>, in place.</summary>
    public static void DrawSegment(byte[] rgb, int width, int height, double x1, double y1, double x2, double y2, int thickness, byte r, byte g, byte b)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        if (thickness < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "Lo spessore deve essere almeno 1.");
        }

        double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        int steps = Math.Max(1, (int)Math.Ceiling(length * 2.0));
        int before = (thickness - 1) / 2;
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            int cx = (int)Math.Round(x1 + (x2 - x1) * t);
            int cy = (int)Math.Round(y1 + (y2 - y1) * t);
            for (int oy = -before; oy < thickness - before; oy++)
            {
                for (int ox = -before; ox < thickness - before; ox++)
                {
                    SetPixel(rgb, width, height, cx + ox, cy + oy, r, g, b);
                }
            }
        }
    }

    /// <summary>Rettangolo di contorno con spessore dato, in place. Le coordinate possono essere fuori immagine.</summary>
    public static void DrawRectangle(byte[] rgb, int width, int height, double left, double top, double right, double bottom, int thickness, byte r, byte g, byte b)
    {
        DrawSegment(rgb, width, height, left, top, right, top, thickness, r, g, b);
        DrawSegment(rgb, width, height, right, top, right, bottom, thickness, r, g, b);
        DrawSegment(rgb, width, height, right, bottom, left, bottom, thickness, r, g, b);
        DrawSegment(rgb, width, height, left, bottom, left, top, thickness, r, g, b);
    }

    private static void SetPixel(byte[] rgb, int width, int height, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        int index = (y * width + x) * 3;
        rgb[index] = r;
        rgb[index + 1] = g;
        rgb[index + 2] = b;
    }
}
