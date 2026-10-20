namespace AllSky.Imaging;

/// <summary>
/// Demosaicing Bayer edge-aware, variante semplificata di Hamilton-Adams (1997).
/// Passo 1, verde: nei siti R e B si confrontano due stime, una orizzontale e una verticale.
/// Ciascuna e' la media dei due verdi vicini piu' la correzione laplaciana (2C - C(-2) - C(+2)) / 4 del
/// canale nativo C. Si sceglie la direzione con gradiente minore, dH = |G(-1) - G(+1)| + |2C - C(-2) - C(+2)|
/// contro dV analogo; con gradienti uguali si usa la media. Cosi' un bordo netto non viene mediato attraverso.
/// Passo 2, rosso e blu: nei siti mancanti si somma al verde stimato la media delle differenze (C - G)
/// dei vicini a 3x3 che hanno il colore C nativo. Nei siti verde sono 2 vicini, nei siti R o B sono 4 diagonali.
/// Semplificazioni rispetto alla letteratura: un solo passo, nessuna iterazione di correzione.
/// Bordi: riflessione che conserva la parita' del pattern, come in <see cref="Debayer.Bilinear"/>.
/// </summary>
public static class DebayerHamiltonAdams
{
    // Colore per posizione, indice ((y & 1) << 1) | (x & 1). 0 = R, 1 = G, 2 = B.
    // Stessa tabella di Debayer: copiata perche' privata in quella classe.
    private static readonly int[][] Layouts =
    {
        new[] { 0, 1, 1, 2 }, // RGGB
        new[] { 2, 1, 1, 0 }, // BGGR
        new[] { 1, 0, 2, 1 }, // GRBG
        new[] { 1, 2, 0, 1 }, // GBRG
    };

    /// <summary>
    /// Restituisce un'immagine RGB da un mosaico Bayer. Stessa firma e stessa semantica di
    /// <see cref="Debayer.Bilinear"/>: il canale nativo di ogni pixel e' il valore grezzo, gli altri sono stimati.
    /// Lancia ArgumentException se raw non ha width * height elementi, o se width o height sono minori di 2.
    /// </summary>
    public static RgbImage Demosaic(ushort[] raw, int width, int height, BayerPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Servono almeno 2 colonne.");
        }

        if (height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Servono almeno 2 righe.");
        }

        if (!Enum.IsDefined(pattern))
        {
            throw new ArgumentOutOfRangeException(nameof(pattern));
        }

        if ((long)width * height != raw.Length)
        {
            throw new ArgumentException("raw.Length diverso da width * height.", nameof(raw));
        }

        int n = width * height;
        int[] layout = Layouts[(int)pattern];

        // Passo 1: verde completo.
        var g = new float[n];
        for (int y = 0; y < height; y++)
        {
            int yu = Mirror(y - 1, height);
            int yd = Mirror(y + 1, height);
            int yu2 = Mirror(y - 2, height);
            int yd2 = Mirror(y + 2, height);
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                if (Color(layout, x, y) == 1)
                {
                    g[idx] = raw[idx];
                    continue;
                }

                int xl = Mirror(x - 1, width);
                int xr = Mirror(x + 1, width);
                int xl2 = Mirror(x - 2, width);
                int xr2 = Mirror(x + 2, width);

                double c = raw[idx];
                double hPlus = 2.0 * c - raw[y * width + xl2] - raw[y * width + xr2];
                double vPlus = 2.0 * c - raw[yu2 * width + x] - raw[yd2 * width + x];
                double gh = 0.5 * (raw[y * width + xl] + raw[y * width + xr]) + hPlus / 4.0;
                double gv = 0.5 * (raw[yu * width + x] + raw[yd * width + x]) + vPlus / 4.0;
                double dH = Math.Abs(raw[y * width + xl] - raw[y * width + xr]) + Math.Abs(hPlus);
                double dV = Math.Abs(raw[yu * width + x] - raw[yd * width + x]) + Math.Abs(vPlus);

                double est = dH < dV ? gh : dV < dH ? gv : 0.5 * (gh + gv);
                g[idx] = (float)est;
            }
        }

        // Passo 2: rosso e blu come verde piu' media delle differenze dei vicini nativi di quel colore.
        var r = new float[n];
        var b = new float[n];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                int native = Color(layout, x, y);
                for (int m = 0; m <= 2; m += 2)
                {
                    float value;
                    if (native == m)
                    {
                        value = raw[idx];
                    }
                    else
                    {
                        double sum = 0.0;
                        int cnt = 0;
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int ny = Mirror(y + dy, height);
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = Mirror(x + dx, width);
                                if (Color(layout, nx, ny) != m)
                                {
                                    continue;
                                }

                                int nIdx = ny * width + nx;
                                sum += raw[nIdx] - g[nIdx];
                                cnt++;
                            }
                        }

                        value = cnt > 0 ? (float)(g[idx] + sum / cnt) : g[idx];
                    }

                    if (m == 0)
                    {
                        r[idx] = value;
                    }
                    else
                    {
                        b[idx] = value;
                    }
                }
            }
        }

        return new RgbImage(width, height, r, g, b);
    }

    private static int Color(int[] layout, int x, int y) => layout[((y & 1) << 1) | (x & 1)];

    // Riflessione con periodo 2(n-1), valida per qualunque indice; conserva la parita'.
    private static int Mirror(int i, int n)
    {
        int period = 2 * (n - 1);
        int m = ((i % period) + period) % period;
        return m < n ? m : period - m;
    }
}
