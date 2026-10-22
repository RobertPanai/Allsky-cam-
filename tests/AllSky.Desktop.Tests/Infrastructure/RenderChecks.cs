using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AllSky.Desktop.Tests;

/// <summary>Risultato dell'analisi di un bitmap renderizzato.</summary>
internal sealed record PixelAnalysis(int ContentPixels, int TintViolations, double TintViolationRatio, string Ascii);

/// <summary>Render offscreen e controlli sui pixel. Nessuna finestra viene mostrata.</summary>
internal static class RenderChecks
{
    /// <summary>Griglia dell'anteprima ASCII: colonne e righe.</summary>
    public const int AsciiColumns = 100;
    public const int AsciiRows = 30;

    /// <summary>Dalla più scura alla più chiara: dieci livelli di luminanza.</summary>
    public const string AsciiLevels = " .:-=+*#%@";

    /// <summary>Differenza minima per contare un pixel come contenuto diverso dallo sfondo.</summary>
    public const int ContentDelta = 8;

    /// <summary>Pixel minimi diversi dallo sfondo perché un bitmap non sia considerato vuoto.</summary>
    public const int MinContentPixels = 200;

    /// <summary>Valore massimo tollerato per verde e blu in notte rossa (0 = nessuna tolleranza).</summary>
    public const int NightTolerance = 24;

    /// <summary>Quota massima di pixel fuori dal rosso in notte rossa (0,05%): assorbe l'antialiasing dei bordi.</summary>
    public const double NightViolationBudget = 0.0005;

    /// <summary>
    /// Misura e dispone l'elemento alla dimensione indicata e lo disegna su un <see cref="RenderTargetBitmap"/>.
    /// L'elemento non viene aggiunto a nessuna finestra.
    /// </summary>
    public static RenderTargetBitmap RenderOffscreen(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Elementi con una dimensione reale o prevista non finita (NaN o infinito).</summary>
    public static List<string> InvalidDimensions(DependencyObject root)
    {
        var found = new List<string>();
        Walk(root, found);
        return found;
    }

    /// <summary>Analizza i pixel: contenuto diverso dallo sfondo, violazioni del rosso in notte, anteprima ASCII.</summary>
    public static PixelAnalysis Analyze(BitmapSource bitmap, bool nightRed)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        // Il bordo in alto a sinistra è lo sfondo: il renderer lo dipinge sempre con Brush.Background (opaco).
        byte bgB = pixels[0];
        byte bgG = pixels[1];
        byte bgR = pixels[2];

        int content = 0;
        int violations = 0;
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];

                if (Math.Abs(r - bgR) > ContentDelta || Math.Abs(g - bgG) > ContentDelta || Math.Abs(b - bgB) > ContentDelta)
                {
                    content++;
                }

                if (nightRed && IsOffNightRed(g, b))
                {
                    violations++;
                }
            }
        }

        double ratio = width * height == 0 ? 0 : (double)violations / (width * height);
        return new PixelAnalysis(content, violations, ratio, Ascii(pixels, width, height, stride));
    }

    /// <summary>Regola del rosso in notte: verde e blu devono restare entro la tolleranza.</summary>
    private static bool IsOffNightRed(byte g, byte b) => Math.Max(g, b) > NightTolerance;

    /// <summary>
    /// Diagnostica dei pixel fuori dal rosso, con la stessa regola di <see cref="Analyze"/>: riquadro, colori
    /// quantizzati a 8 livelli per canale e, per quattro punti campione, l'elemento visuale sotto il punto.
    /// Le coordinate sono in pixel del bitmap; a 96 dpi coincidono con i DIP dell'elemento radice.
    /// </summary>
    /// <param name="bitmap">Bitmap renderizzato dell'elemento radice.</param>
    /// <param name="root">Elemento radice su cui si fa <c>VisualTreeHelper.HitTest</c> per i punti campione.</param>
    public static string DescribeTintViolations(BitmapSource bitmap, DependencyObject? root)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = -1;
        int maxY = -1;
        long count = 0;
        long sumX = 0;
        long sumY = 0;
        int firstX = -1;
        int firstY = -1;
        int lastX = -1;
        int lastY = -1;
        var colors = new Dictionary<int, int>();

        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                if (!IsOffNightRed(g, b))
                {
                    continue;
                }

                if (count == 0)
                {
                    firstX = x;
                    firstY = y;
                }

                lastX = x;
                lastY = y;
                count++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                sumX += x;
                sumY += y;

                int key = ((r >> 5) << 6) | ((g >> 5) << 3) | (b >> 5);
                colors.TryGetValue(key, out int seen);
                colors[key] = seen + 1;
            }
        }

        if (count == 0)
        {
            return "Diagnostica: nessun pixel fuori dal rosso.";
        }

        var sb = new StringBuilder();
        sb.Append($"Riquadro pixel fuori dal rosso: x {minX}..{maxX}, y {minY}..{maxY} ")
          .Append($"({maxX - minX + 1} x {maxY - minY + 1} px; 1 px = 1 DIP a 96 dpi).");
        sb.Append("\nColori fuori dal rosso (8 livelli per canale, primi 5 per pixel):");
        foreach (KeyValuePair<int, int> entry in colors.OrderByDescending(e => e.Value).ThenBy(e => e.Key).Take(5))
        {
            sb.Append($" {QuantizedHex(entry.Key)} x{entry.Value}");
        }

        sb.Append($" (colori distinti: {colors.Count}).");

        (int middleX, int middleY) = NthOffending(pixels, width, height, stride, count / 2);
        int centroidX = (int)Math.Round((double)sumX / count);
        int centroidY = (int)Math.Round((double)sumY / count);

        sb.Append("\nCampioni (HitTest sull'elemento radice):");
        AppendSample(sb, "prima", firstX, firstY, root);
        AppendSample(sb, "centrale", middleX, middleY, root);
        AppendSample(sb, "ultima", lastX, lastY, root);
        AppendSample(sb, "baricentro", centroidX, centroidY, root);
        return sb.ToString();
    }

    /// <summary>Chiave quantizzata (3 bit per canale) come #RRGGBB, con il valore centrale del livello.</summary>
    private static string QuantizedHex(int key)
    {
        int r = ((key >> 6) & 7) * 255 / 7;
        int g = ((key >> 3) & 7) * 255 / 7;
        int b = (key & 7) * 255 / 7;
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>Coordinate del pixel fuori dal rosso con indice dato, in ordine di scansione (riga per riga).</summary>
    private static (int X, int Y) NthOffending(byte[] pixels, int width, int height, int stride, long ordinal)
    {
        long seen = 0;
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;
                if (!IsOffNightRed(pixels[i + 1], pixels[i]))
                {
                    continue;
                }

                if (seen == ordinal)
                {
                    return (x, y);
                }

                seen++;
            }
        }

        return (-1, -1);
    }

    private static void AppendSample(StringBuilder sb, string label, int x, int y, DependencyObject? root)
    {
        sb.Append($"\n  {label} ({x},{y}): ");
        if (x < 0 || y < 0)
        {
            sb.Append("punto non disponibile");
            return;
        }

        sb.Append(DescribeHit(root, x, y));
    }

    /// <summary>Elemento più in alto sotto il punto e fino a quattro antenati visuali, dal più vicino.</summary>
    private static string DescribeHit(DependencyObject? root, int x, int y)
    {
        if (root is not Visual visual)
        {
            return "elemento radice assente o non visuale";
        }

        HitTestResult? hit;
        try
        {
            hit = VisualTreeHelper.HitTest(visual, new Point(x + 0.5, y + 0.5));
        }
        catch (Exception ex)
        {
            return "HitTest fallito: " + ex.GetType().Name + " " + ex.Message;
        }

        if (hit?.VisualHit is not DependencyObject node)
        {
            return "nessun elemento sotto il punto";
        }

        string name = node is FrameworkElement element ? element.Name : string.Empty;
        var ancestors = new List<string>();
        DependencyObject? current = node;
        for (int depth = 0; depth < 4 && current is Visual; depth++)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is null)
            {
                break;
            }

            ancestors.Add(current.GetType().Name);
        }

        string chain = ancestors.Count == 0 ? "nessuno" : string.Join(" > ", ancestors);
        return $"{node.GetType().Name} Name='{name}'; antenati: {chain}";
    }

    /// <summary>Anteprima ASCII: griglia fissa, luminanza media per cella, righe separate da <c>\n</c>.</summary>
    public static string Ascii(byte[] pixels, int width, int height, int stride)
    {
        var sb = new StringBuilder(AsciiRows * (AsciiColumns + 1));
        for (int row = 0; row < AsciiRows; row++)
        {
            int y0 = row * height / AsciiRows;
            int y1 = Math.Max(y0 + 1, (row + 1) * height / AsciiRows);
            if (row > 0)
            {
                sb.Append('\n');
            }

            for (int col = 0; col < AsciiColumns; col++)
            {
                int x0 = col * width / AsciiColumns;
                int x1 = Math.Max(x0 + 1, (col + 1) * width / AsciiColumns);
                double sum = 0;
                int count = 0;
                for (int y = y0; y < y1 && y < height; y++)
                {
                    for (int x = x0; x < x1 && x < width; x++)
                    {
                        int i = y * stride + x * 4;
                        sum += 0.0722 * pixels[i] + 0.7152 * pixels[i + 1] + 0.2126 * pixels[i + 2];
                        count++;
                    }
                }

                double luminance = count == 0 ? 0 : sum / count / 255.0;
                int level = Math.Clamp((int)Math.Floor(luminance * AsciiLevels.Length), 0, AsciiLevels.Length - 1);
                sb.Append(AsciiLevels[level]);
            }
        }

        return sb.ToString();
    }

    /// <summary>Salva il bitmap come PNG. Restituisce il nome del file scritto.</summary>
    public static string SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(path))
        {
            encoder.Save(stream);
        }

        return Path.GetFileName(path);
    }

    /// <summary>
    /// Nasconde le immagini (foto del cielo, anteprime) durante il controllo dei colori di notte: non sono UI.
    /// Restituisce gli elementi nascosti, da ripristinare con <see cref="Restore"/>.
    /// </summary>
    public static List<UIElement> HideImages(DependencyObject root)
    {
        var hidden = new List<UIElement>();
        CollectImages(root, hidden);
        foreach (UIElement image in hidden)
        {
            image.Visibility = Visibility.Collapsed;
        }

        return hidden;
    }

    /// <summary>Ripristina la visibilità degli elementi nascosti da <see cref="HideImages"/>.</summary>
    public static void Restore(IEnumerable<UIElement> hidden)
    {
        foreach (UIElement image in hidden)
        {
            image.Visibility = Visibility.Visible;
        }
    }

    private static void CollectImages(DependencyObject node, List<UIElement> found)
    {
        if (node is not Visual)
        {
            return;
        }

        if (node is System.Windows.Controls.Image image && image.Visibility == Visibility.Visible)
        {
            found.Add(image);
            return;
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            CollectImages(VisualTreeHelper.GetChild(node, i), found);
        }
    }

    private static void Walk(DependencyObject node, List<string> found)
    {
        if (node is not Visual)
        {
            return;
        }

        if (node is FrameworkElement element && found.Count < 20)
        {
            if (IsInvalid(element.ActualWidth) || IsInvalid(element.ActualHeight)
                || IsInvalid(element.DesiredSize.Width) || IsInvalid(element.DesiredSize.Height))
            {
                found.Add($"{element.GetType().Name} actual={element.ActualWidth}x{element.ActualHeight} desired={element.DesiredSize}");
            }
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), found);
        }
    }

    private static bool IsInvalid(double value) => double.IsNaN(value) || double.IsInfinity(value);
}
