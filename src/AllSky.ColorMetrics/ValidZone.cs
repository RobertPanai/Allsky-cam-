namespace AllSky.ColorMetrics;

/// <summary>
/// Zona valida per le misure: cerchio centrato (coordinate in pixel, centro del pixel = intero) ridotto al
/// <see cref="RadiusFactor"/> del raggio. Serve a escludere gli angoli neri fuori dal fisheye.
/// Senza una geometria nota si usa il cerchio inscritto nel lato minore.
/// </summary>
public readonly record struct ValidZone(int Width, int Height, double Cx, double Cy, double Radius)
{
    /// <summary>Frazione del raggio del cerchio fisheye (o inscritto) usata come zona valida.</summary>
    public const double RadiusFactor = 0.9;

    /// <summary>Cerchio inscritto nel lato minore, ridotto al 90% del raggio. Centro al centro dell'immagine.</summary>
    public static ValidZone Inscribed(int width, int height) =>
        Fisheye(width, height, (width - 1) / 2.0, (height - 1) / 2.0, Math.Min(width, height) / 2.0);

    /// <summary>Cerchio fisheye noto (centro e raggio del cerchio dell'immagine), ridotto al 90% del raggio.</summary>
    public static ValidZone Fisheye(int width, int height, double cx, double cy, double circleRadius)
    {
        if (width < 1 || height < 1 || !(circleRadius > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(circleRadius), "Dimensioni o raggio non validi.");
        }

        return new ValidZone(width, height, cx, cy, circleRadius * RadiusFactor);
    }

    /// <summary>True se il pixel (x,y) è dentro la zona valida.</summary>
    public bool Contains(int x, int y)
    {
        double dx = x - Cx;
        double dy = y - Cy;
        return dx * dx + dy * dy <= Radius * Radius;
    }

    /// <summary>Indici lineari (y*Width + x) dei pixel della zona, in ordine di riga.</summary>
    public int[] PixelIndices()
    {
        var list = new List<int>();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Contains(x, y))
                {
                    list.Add(y * Width + x);
                }
            }
        }

        return list.ToArray();
    }
}
