using AllSky.Core;
using AllSky.Imaging.Overlay;

namespace AllSky.Publish;

/// <summary>
/// Prepara il frame per l'invio: ridimensiona (mai ingrandendo) il lato lungo a <see cref="PublishSettings.LongEdgePx"/>,
/// codifica in JPEG e tiene il peso sotto <see cref="TargetBytes"/>.
/// <para>
/// Algoritmo (in ordine):
/// </para>
/// <list type="number">
/// <item>Qualita' iniziale <see cref="PublishSettings.JpegQuality"/>; se il JPEG supera il target, la qualita' scende di 5 alla volta fino a 40 (mai sotto).</item>
/// <item>Se ancora troppo grande, il lato lungo scende a passi di 0.85 (minimo 320) e si ricomincia dalla qualita' iniziale.</item>
/// <item>Se nessun tentativo rientra nel target ma il migliore sta sotto <see cref="HardMaxBytes"/>, viene restituito il migliore:
/// scelta, un'immagine un po' sopra il target e' preferibile a nessuna immagine. Altrimenti si lancia <see cref="InvalidOperationException"/>.</item>
/// </list>
/// <para>
/// <see cref="PreparedImage.ContentHash"/> e' calcolato sui byte RGB alla risoluzione di pubblicazione, prima di ogni riduzione
/// di emergenza: dipende dal frame e da <see cref="PublishSettings.LongEdgePx"/>, non dalla qualita' JPEG.
/// </para>
/// </summary>
public sealed class WebImagePreparer
{
    /// <summary>Peso obiettivo del JPEG in byte.</summary>
    public const int TargetBytes = 300_000;

    /// <summary>Tetto assoluto del JPEG in byte: oltre questo valore la preparazione fallisce.</summary>
    public const int HardMaxBytes = 900_000;

    private const int MinQuality = 40;
    private const int QualityStep = 5;
    private const double SizeStep = 0.85;
    private const int MinLongEdgePx = 320;
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private readonly IJpegEncoder _encoder;

    /// <summary>Crea il preparatore con l'encoder JPEG da usare.</summary>
    /// <param name="encoder">Encoder JPEG, non nullo.</param>
    public WebImagePreparer(IJpegEncoder encoder)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
    }

    /// <summary>Prepara il frame secondo le impostazioni.</summary>
    /// <param name="frame">Frame RGB 8 bit, con <c>Rgb8.Length == Width * Height * 3</c>.</param>
    /// <param name="settings">Impostazioni di pubblicazione (lato lungo e qualita' iniziale).</param>
    /// <returns>Immagine JPEG con dimensioni effettive e hash del contenuto.</returns>
    /// <exception cref="ArgumentNullException">Se frame o settings sono nulli.</exception>
    /// <exception cref="ArgumentException">Se le dimensioni non sono positive o il buffer RGB non ha la lunghezza attesa.</exception>
    /// <exception cref="InvalidOperationException">Se nessun JPEG rientra nel tetto <see cref="HardMaxBytes"/>.</exception>
    public PreparedImage Prepare(PublishFrame frame, PublishSettings settings)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(settings);
        ValidateFrame(frame);
        if (settings.LongEdgePx < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.LongEdgePx, "Il lato lungo deve essere almeno 1 pixel.");
        }

        (int baseWidth, int baseHeight) = FitLongEdge(frame.Width, frame.Height, settings.LongEdgePx);
        byte[] baseRgb = ResizeTo(frame, baseWidth, baseHeight);
        ulong contentHash = Fnv1a64(baseRgb);

        Candidate? best = null;
        int longEdge = Math.Max(baseWidth, baseHeight);

        while (true)
        {
            (int width, int height) = FitLongEdge(frame.Width, frame.Height, longEdge);
            byte[] rgb = width == baseWidth && height == baseHeight ? baseRgb : ResizeTo(frame, width, height);

            foreach (int quality in QualitySequence(settings.JpegQuality))
            {
                byte[] jpeg = _encoder.Encode(rgb, width, height, quality);
                if (jpeg.Length <= TargetBytes)
                {
                    return new PreparedImage(jpeg, width, height, contentHash);
                }

                if (best is null || jpeg.Length < best.Jpeg.Length)
                {
                    best = new Candidate(jpeg, width, height);
                }
            }

            int next = Math.Max(MinLongEdgePx, (int)Math.Round(longEdge * SizeStep, MidpointRounding.AwayFromZero));
            if (next >= longEdge)
            {
                break;
            }

            longEdge = next;
        }

        if (best is not null && best.Jpeg.Length <= HardMaxBytes)
        {
            return new PreparedImage(best.Jpeg, best.Width, best.Height, contentHash);
        }

        throw new InvalidOperationException(
            $"Impossibile ottenere un JPEG entro {HardMaxBytes} byte: il miglior tentativo pesa {best?.Jpeg.Length ?? 0} byte.");
    }

    private static void ValidateFrame(PublishFrame frame)
    {
        if (frame.Width < 1 || frame.Height < 1)
        {
            throw new ArgumentException("Larghezza e altezza del frame devono essere positive.", nameof(frame));
        }

        ArgumentNullException.ThrowIfNull(frame.Rgb8, nameof(frame));
        long expected = (long)frame.Width * frame.Height * 3;
        if (frame.Rgb8.Length != expected)
        {
            throw new ArgumentException($"Il buffer RGB deve essere {expected} byte (larghezza * altezza * 3).", nameof(frame));
        }
    }

    /// <summary>Dimensioni con lato lungo al massimo <paramref name="maxLongEdge"/>, rapporto mantenuto, mai ingrandite.</summary>
    private static (int Width, int Height) FitLongEdge(int width, int height, int maxLongEdge)
    {
        int longSide = Math.Max(width, height);
        if (longSide <= maxLongEdge)
        {
            return (width, height);
        }

        double scale = (double)maxLongEdge / longSide;
        int w = Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero));
        int h = Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero));
        return (w, h);
    }

    /// <summary>Restituisce i byte RGB alla dimensione richiesta. Se la dimensione coincide, restituisce il buffer originale senza copia.</summary>
    private static byte[] ResizeTo(PublishFrame frame, int width, int height)
    {
        if (width == frame.Width && height == frame.Height)
        {
            return frame.Rgb8;
        }

        // Le dimensioni non superano mai quelle del frame (nessun ingrandimento), quindi width*height*3 sta in un int.
        byte[] resized = new byte[width * height * 3];
        ImageResize.Box(frame.Rgb8, frame.Width, frame.Height, resized, width, height);
        return resized;
    }

    /// <summary>Qualita' da provare: quella iniziale, poi a passi di 5 fino a 40 (o fino alla qualita' iniziale, se e' gia' sotto 40).</summary>
    private static IEnumerable<int> QualitySequence(int startQuality)
    {
        int quality = startQuality;
        yield return quality;
        while (quality > MinQuality)
        {
            quality = Math.Max(MinQuality, quality - QualityStep);
            yield return quality;
        }
    }

    /// <summary>FNV-1a 64 bit: offset basis 14695981039346656037, primo 1099511628211, XOR poi moltiplicazione per byte.</summary>
    private static ulong Fnv1a64(byte[] data)
    {
        ulong hash = FnvOffsetBasis;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return hash;
    }

    /// <summary>Miglior tentativo finora, usato solo se nessuno rientra nel target.</summary>
    private sealed record Candidate(byte[] Jpeg, int Width, int Height);
}
