namespace AllSky.Sky.Calibration;

/// <summary>
/// Sole individuato in un frame in luminanza.
/// </summary>
/// <param name="X">Coordinata x del centroide in pixel, pesato per luminanza.</param>
/// <param name="Y">Coordinata y del centroide in pixel, pesato per luminanza.</param>
/// <param name="AreaPx">Numero di pixel saturi della componente scelta.</param>
/// <param name="PeakLuma">Luminanza massima nella componente.</param>
public readonly record struct SunDetection(double X, double Y, double AreaPx, double PeakLuma);

/// <summary>
/// Individua il Sole in un frame in luminanza a 8 bit.
///
/// <para>
/// Algoritmo:
/// </para>
/// <list type="number">
///   <item><description>Si marcano i pixel saturati (luminanza &gt;= soglia, di default 250).</description></item>
///   <item><description>Se i saturi superano il 5% del frame si rifiuta: cielo bruciato o nuvole luminose.</description></item>
///   <item><description>Si trovano le componenti connesse (8-connessione).</description></item>
///   <item><description>
///     Tra le componenti con area di almeno 20 px e circolarita' di almeno 0.6 si sceglie la piu' grande.
///     La circolarita' e' approssimata dal rapporto bounding-box/area, normalizzato su pi/4 (un disco vale ~1),
///     moltiplicato per il rapporto di aspetto min/max della bounding box. Il fattore di aspetto serve a scartare
///     le barre luminose: senza di esso una barra avrebbe rapporto area/bbox pari a 1.
///   </description></item>
///   <item><description>Il centroide e' la media dei pixel della componente, pesata per luminanza.</description></item>
/// </list>
/// </summary>
public static class SunDetector
{
    /// <summary>Soglia di saturazione di default (luminanza minima per essere considerato saturo).</summary>
    public const byte DefaultSaturationThreshold = 250;

    /// <summary>Area minima, in pixel, di una componente candidata.</summary>
    public const int MinAreaPx = 20;

    /// <summary>Circolarita' minima della componente candidata.</summary>
    public const double MinCircularity = 0.6;

    /// <summary>Frazione massima del frame che puo' essere satura prima di rifiutare l'immagine.</summary>
    public const double MaxSaturatedFraction = 0.05;

    /// <summary>Cerca il Sole usando la soglia di saturazione di default.</summary>
    public static bool TryFindSun(byte[] luma, int width, int height, out SunDetection d)
        => TryFindSun(luma, width, height, DefaultSaturationThreshold, out d);

    /// <summary>
    /// Cerca il Sole nel frame. Restituisce false se non c'e' un candidato compatto o se il frame e' troppo saturo.
    /// In caso di false <paramref name="d"/> e' il valore di default.
    /// </summary>
    /// <exception cref="ArgumentNullException">Il buffer e' nullo.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Dimensioni non positive o troppo grandi.</exception>
    /// <exception cref="ArgumentException">Il buffer e' piu' corto di width * height.</exception>
    public static bool TryFindSun(byte[] luma, int width, int height, byte saturationThreshold, out SunDetection d)
    {
        d = default;
        ArgumentNullException.ThrowIfNull(luma);
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "La larghezza deve essere positiva.");
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "L'altezza deve essere positiva.");
        }

        long total = (long)width * height;
        if (total > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Il frame supera il numero massimo di pixel supportato.");
        }
        if (luma.Length < total)
        {
            throw new ArgumentException("Il buffer di luminanza e' piu' corto di width * height.", nameof(luma));
        }

        int n = (int)total;

        int saturated = 0;
        for (int i = 0; i < n; i++)
        {
            if (luma[i] >= saturationThreshold)
            {
                saturated++;
            }
        }

        // Cielo bruciato o nuvole luminose: un Sole isolato non occupa una frazione cosi' grande del frame.
        if (saturated == 0 || saturated > MaxSaturatedFraction * n)
        {
            return false;
        }

        bool[] visited = new bool[n];
        var queue = new Queue<int>();
        bool found = false;
        int bestArea = 0;

        for (int start = 0; start < n; start++)
        {
            if (visited[start] || luma[start] < saturationThreshold)
            {
                continue;
            }

            int area = 0;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            double sumW = 0.0, sumWx = 0.0, sumWy = 0.0;
            byte peak = 0;

            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int px = idx % width;
                int py = idx / width;
                byte w = luma[idx];

                area++;
                sumW += w;
                sumWx += (double)w * px;
                sumWy += (double)w * py;
                if (w > peak)
                {
                    peak = w;
                }
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;

                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = py + dy;
                    if (ny < 0 || ny >= height)
                    {
                        continue;
                    }
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = px + dx;
                        if ((dx == 0 && dy == 0) || nx < 0 || nx >= width)
                        {
                            continue;
                        }
                        int nidx = ny * width + nx;
                        if (!visited[nidx] && luma[nidx] >= saturationThreshold)
                        {
                            visited[nidx] = true;
                            queue.Enqueue(nidx);
                        }
                    }
                }
            }

            if (area < MinAreaPx)
            {
                continue;
            }

            int bw = maxX - minX + 1;
            int bh = maxY - minY + 1;
            if (Circularity(area, bw, bh) < MinCircularity)
            {
                continue;
            }

            if (!found || area > bestArea)
            {
                found = true;
                bestArea = area;
                d = new SunDetection(sumWx / sumW, sumWy / sumW, area, peak);
            }
        }

        return found;
    }

    /// <summary>
    /// Circolarita' approssimata: (area / bbox) normalizzato su pi/4, per il rapporto di aspetto min/max della bounding box.
    /// Un disco vale circa 1, un quadrato circa 1.27 (accettato), una barra 60x4 circa 0.08 (scartata).
    /// </summary>
    private static double Circularity(int area, int bw, int bh)
    {
        double fill = area / (double)((long)bw * bh);
        double normalizedFill = fill * 4.0 / Math.PI;
        double aspect = Math.Min(bw, bh) / (double)Math.Max(bw, bh);
        return normalizedFill * aspect;
    }
}
