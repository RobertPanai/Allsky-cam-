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

                if (nightRed && Math.Max(g, b) > NightTolerance)
                {
                    violations++;
                }
            }
        }

        double ratio = width * height == 0 ? 0 : (double)violations / (width * height);
        return new PixelAnalysis(content, violations, ratio, Ascii(pixels, width, height, stride));
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
