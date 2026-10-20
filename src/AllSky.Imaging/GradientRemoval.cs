namespace AllSky.Imaging;

/// <summary>
/// Rimozione del gradiente di fondo (vignettatura, inquinamento luminoso) su un'immagine RGB lineare.
/// <see cref="Fit"/> stima per canale un polinomio di grado 2 sui pixel di cielo sottocampionati:
/// la griglia di 64 x 48 nodi (o meno, su immagini piccole) usa la mediana di una finestra 8 x 8 di pixel di cielo.
/// Un nodo di canale viene scartato se supera mediana + 3 * MAD dei nodi (stelle residue).
/// Il fit e' ai minimi quadrati, risolto con eliminazione di Gauss in double.
/// <see cref="Apply"/> sottrae strength * (modello - media del modello sul cielo): il livello medio del
/// fondo non cambia, cambia solo la sua variazione spaziale.
/// </summary>
public static class GradientRemoval
{
    private const int GridCols = 64;
    private const int GridRows = 48;
    private const int WindowSize = 8;
    private const int Terms = 6;
    private const double MadLimit = 3.0;
    private const double SingularTolerance = 1e-12;
    private const double MaxValue = 65535.0;

    /// <summary>
    /// Stima il gradiente di fondo di ogni canale sui pixel di cielo della maschera.
    /// Lancia InvalidOperationException se non ci sono abbastanza pixel di cielo per 6 coefficienti.
    /// </summary>
    public static GradientModel Fit(RgbImage img, SkyMask mask)
    {
        ArgumentNullException.ThrowIfNull(img);
        ArgumentNullException.ThrowIfNull(mask);
        CheckSameSize(img, mask.Width, mask.Height, nameof(mask));

        int w = img.Width;
        int h = img.Height;
        bool[] sky = mask.Values;
        float[][] planes = { img.R, img.G, img.B };

        // Nodi della griglia e mediane locali per canale (finestre 8x8 dei soli pixel di cielo).
        int cols = Math.Min(GridCols, w);
        int rows = Math.Min(GridRows, h);
        int nodeCount = cols * rows;
        int winW = Math.Min(WindowSize, w);
        int winH = Math.Min(WindowSize, h);
        int minSky = Math.Max(1, winW * winH / 8);
        var nodeX = new double[nodeCount];
        var nodeY = new double[nodeCount];
        var nodeOk = new bool[nodeCount];
        var nodeVal = new double[3][];
        for (int c = 0; c < 3; c++)
        {
            nodeVal[c] = new double[nodeCount];
        }

        var buf = new double[winW * winH];
        for (int gy = 0; gy < rows; gy++)
        {
            for (int gx = 0; gx < cols; gx++)
            {
                int node = gy * cols + gx;
                double cx = (gx + 0.5) * w / cols;
                double cy = (gy + 0.5) * h / rows;
                int x0 = Math.Clamp((int)cx - winW / 2, 0, w - winW);
                int y0 = Math.Clamp((int)cy - winH / 2, 0, h - winH);

                // Il nodo sta nel baricentro della finestra effettiva: cosi' il fit non ha sfasamenti.
                nodeX[node] = x0 + (winW - 1) * 0.5;
                nodeY[node] = y0 + (winH - 1) * 0.5;

                int nSky = 0;
                for (int y = y0; y < y0 + winH; y++)
                {
                    for (int x = x0; x < x0 + winW; x++)
                    {
                        if (sky[y * w + x])
                        {
                            nSky++;
                        }
                    }
                }

                if (nSky < minSky)
                {
                    continue;
                }

                for (int c = 0; c < 3; c++)
                {
                    int k = 0;
                    float[] plane = planes[c];
                    for (int y = y0; y < y0 + winH; y++)
                    {
                        for (int x = x0; x < x0 + winW; x++)
                        {
                            if (sky[y * w + x])
                            {
                                buf[k++] = plane[y * w + x];
                            }
                        }
                    }

                    Array.Sort(buf, 0, k);
                    nodeVal[c][node] = MedianSorted(buf, k);
                }

                nodeOk[node] = true;
            }
        }

        // Media della base del polinomio sui pixel di cielo: serve per centrare il modello.
        var meanBasis = new double[Terms];
        long skyCount = 0;
        for (int y = 0; y < h; y++)
        {
            double v = GradientModel.NormV(y, h);
            for (int x = 0; x < w; x++)
            {
                if (!sky[y * w + x])
                {
                    continue;
                }

                double u = GradientModel.NormU(x, w);
                meanBasis[0] += 1.0;
                meanBasis[1] += u;
                meanBasis[2] += v;
                meanBasis[3] += u * u;
                meanBasis[4] += u * v;
                meanBasis[5] += v * v;
                skyCount++;
            }
        }

        if (skyCount == 0)
        {
            throw new InvalidOperationException("Nessun pixel di cielo nella maschera.");
        }

        for (int k = 0; k < Terms; k++)
        {
            meanBasis[k] /= skyCount;
        }

        var coefficients = new double[3][];
        var skyMean = new double[3];
        var vals = new double[nodeCount];
        var dev = new double[nodeCount];
        var basis = new double[Terms];
        for (int c = 0; c < 3; c++)
        {
            int m = 0;
            for (int node = 0; node < nodeCount; node++)
            {
                if (nodeOk[node])
                {
                    vals[m++] = nodeVal[c][node];
                }
            }

            if (m < Terms)
            {
                throw new InvalidOperationException("Troppi pochi nodi di cielo per stimare il gradiente.");
            }

            Array.Sort(vals, 0, m);
            double med = MedianSorted(vals, m);
            for (int i = 0; i < m; i++)
            {
                dev[i] = Math.Abs(vals[i] - med);
            }

            Array.Sort(dev, 0, m);
            double limit = med + MadLimit * MedianSorted(dev, m);

            // Equazioni normali: (A^T A) c = A^T y, sui nodi non scartati.
            var ata = new double[Terms, Terms];
            var aty = new double[Terms];
            int used = 0;
            for (int node = 0; node < nodeCount; node++)
            {
                if (!nodeOk[node] || nodeVal[c][node] > limit)
                {
                    continue;
                }

                double u = GradientModel.NormU(nodeX[node], w);
                double v = GradientModel.NormV(nodeY[node], h);
                basis[0] = 1.0;
                basis[1] = u;
                basis[2] = v;
                basis[3] = u * u;
                basis[4] = u * v;
                basis[5] = v * v;
                double y = nodeVal[c][node];
                for (int i = 0; i < Terms; i++)
                {
                    aty[i] += basis[i] * y;
                    for (int j = 0; j < Terms; j++)
                    {
                        ata[i, j] += basis[i] * basis[j];
                    }
                }

                used++;
            }

            if (used < Terms)
            {
                throw new InvalidOperationException("Troppi pochi nodi di cielo dopo lo scarto delle stelle.");
            }

            coefficients[c] = SolveGauss(ata, aty);
            double mean = 0.0;
            for (int k = 0; k < Terms; k++)
            {
                mean += coefficients[c][k] * meanBasis[k];
            }

            skyMean[c] = mean;
        }

        return new GradientModel(w, h, coefficients, skyMean);
    }

    /// <summary>
    /// Sottrae in place strength * (modello - media del modello sul cielo) da ogni canale.
    /// strength deve essere in [0, 1]; con 0 l'immagine non cambia. Il risultato e' limitato a 0..65535.
    /// </summary>
    public static void Apply(RgbImage img, GradientModel model, float strength)
    {
        ArgumentNullException.ThrowIfNull(img);
        ArgumentNullException.ThrowIfNull(model);
        if (!(strength >= 0f && strength <= 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(strength), "strength deve essere in [0, 1].");
        }

        CheckSameSize(img, model.Width, model.Height, nameof(model));
        if (strength == 0f)
        {
            return;
        }

        int w = img.Width;
        int h = img.Height;
        float[][] planes = { img.R, img.G, img.B };
        for (int c = 0; c < 3; c++)
        {
            double[] coef = model.CoefficientsRef(c);
            double mean = model.SkyMeanRef(c);
            float[] plane = planes[c];
            for (int y = 0; y < h; y++)
            {
                double v = GradientModel.NormV(y, h);
                for (int x = 0; x < w; x++)
                {
                    double u = GradientModel.NormU(x, w);
                    double offset = strength * (GradientModel.Evaluate(coef, u, v) - mean);
                    int i = y * w + x;
                    plane[i] = (float)Math.Clamp(plane[i] - offset, 0.0, MaxValue);
                }
            }
        }
    }

    private static void CheckSameSize(RgbImage img, int width, int height, string paramName)
    {
        if (img.Width != width || img.Height != height)
        {
            throw new ArgumentException("Dimensioni diverse tra immagine e modello/maschera.", paramName);
        }
    }

    // Mediana di a[0..n) gia' ordinato; n > 0.
    private static double MedianSorted(double[] a, int n)
    {
        if (n % 2 == 1)
        {
            return a[n / 2];
        }

        return 0.5 * (a[n / 2 - 1] + a[n / 2]);
    }

    // Eliminazione di Gauss con pivot parziale. Lancia InvalidOperationException se singolare.
    private static double[] SolveGauss(double[,] a, double[] rhs)
    {
        int n = rhs.Length;
        var m = (double[,])a.Clone();
        var b = (double[])rhs.Clone();

        double scale = 1.0;
        for (int i = 0; i < n; i++)
        {
            scale = Math.Max(scale, Math.Abs(m[i, i]));
        }

        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
            {
                if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col]))
                {
                    piv = r;
                }
            }

            if (Math.Abs(m[piv, col]) < SingularTolerance * scale)
            {
                throw new InvalidOperationException("Sistema del fit del gradiente singolare.");
            }

            if (piv != col)
            {
                for (int c = 0; c < n; c++)
                {
                    (m[col, c], m[piv, c]) = (m[piv, c], m[col, c]);
                }

                (b[col], b[piv]) = (b[piv], b[col]);
            }

            for (int r = col + 1; r < n; r++)
            {
                double f = m[r, col] / m[col, col];
                for (int c = col; c < n; c++)
                {
                    m[r, c] -= f * m[col, c];
                }

                b[r] -= f * b[col];
            }
        }

        var x = new double[n];
        for (int r = n - 1; r >= 0; r--)
        {
            double s = b[r];
            for (int c = r + 1; c < n; c++)
            {
                s -= m[r, c] * x[c];
            }

            x[r] = s / m[r, r];
        }

        return x;
    }
}
