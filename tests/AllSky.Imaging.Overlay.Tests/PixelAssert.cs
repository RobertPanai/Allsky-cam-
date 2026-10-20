using Xunit;

namespace AllSky.Imaging.Overlay.Tests;

/// <summary>Helper per leggere pixel e bounding box da un buffer RGB nei test.</summary>
internal static class PixelAssert
{
    /// <summary>Verifica il colore del pixel (x, y) in un buffer largo <paramref name="w"/> pixel.</summary>
    public static void Pixel(ReadOnlySpan<byte> rgb, int w, int x, int y, (byte R, byte G, byte B) expected)
    {
        int i = (y * w + x) * 3;
        Assert.Equal(expected.R, rgb[i]);
        Assert.Equal(expected.G, rgb[i + 1]);
        Assert.Equal(expected.B, rgb[i + 2]);
    }

    /// <summary>
    /// Rettangolo minimo (minX, minY, maxX, maxY) che contiene tutti i pixel del colore dato.
    /// Restituisce (-1, -1, -1, -1) se il colore non compare.
    /// </summary>
    public static (int MinX, int MinY, int MaxX, int MaxY) Bounds(ReadOnlySpan<byte> rgb, int w, int h, (byte R, byte G, byte B) color)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                if (rgb[i] == color.R && rgb[i + 1] == color.G && rgb[i + 2] == color.B)
                {
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        return maxX < 0 ? (-1, -1, -1, -1) : (minX, minY, maxX, maxY);
    }
}
