namespace AllSky.Imaging.Overlay;

/// <summary>
/// Disegno di testo sopra un'immagine RGB a 8 bit (3 byte per pixel, righe contigue), con il <see cref="BitmapFont"/> 8x16.
/// Ogni scrittura e' limitata al rettangolo dell'immagine: pixel fuori dai bordi (anche con coordinate negative) vengono
/// scartati e non scrivono mai fuori dal buffer.
/// </summary>
public static class OverlayRenderer
{
    /// <summary>Scala massima consentita del testo (la minima e' 1).</summary>
    public const int MaxScale = 4;

    /// <summary>Spostamento dell'ombra, in pixel, verso destra e verso il basso. Vale 1 px qualunque sia la scala.</summary>
    public const int ShadowOffsetPx = 1;

    private const int Channels = 3;

    private static readonly (byte R, byte G, byte B) ShadowColor = (0, 0, 0);

    /// <summary>
    /// Dimensioni in pixel occupate da un testo a una certa scala. La larghezza e' 8 * scala per carattere
    /// (la colonna vuota dei glifi e' inclusa); l'ombra, se presente, aggiunge 1 px a larghezza e altezza.
    /// </summary>
    /// <param name="text">Testo da misurare.</param>
    /// <param name="scale">Scala intera, da 1 a <see cref="MaxScale"/>.</param>
    /// <param name="shadow">Se il testo ha l'ombra.</param>
    public static (int Width, int Height) MeasureText(string text, int scale, bool shadow)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateScale(scale);

        long width = (long)text.Length * BitmapFont.GlyphWidth * scale + (shadow ? ShadowOffsetPx : 0);
        long height = (long)BitmapFont.GlyphHeight * scale + (shadow ? ShadowOffsetPx : 0);
        return ((int)Math.Min(width, int.MaxValue), (int)Math.Min(height, int.MaxValue));
    }

    /// <summary>
    /// Disegna il testo con l'angolo in alto a sinistra in (<paramref name="x"/>, <paramref name="y"/>).
    /// Con <paramref name="shadow"/> l'ombra nera di 1 px viene disegnata prima del testo, spostata in basso a destra.
    /// Le coordinate possono essere negative o oltre il bordo: la parte visibile viene disegnata, il resto è scartato.
    /// </summary>
    /// <param name="rgb">Buffer RGB, di almeno w * h * 3 byte. Modificato sul posto.</param>
    /// <param name="w">Larghezza dell'immagine in pixel.</param>
    /// <param name="h">Altezza dell'immagine in pixel.</param>
    /// <param name="text">Testo da disegnare; i caratteri non supportati diventano '?'.</param>
    /// <param name="x">Colonna del bordo sinistro del primo carattere.</param>
    /// <param name="y">Riga del bordo superiore del testo.</param>
    /// <param name="color">Colore del testo.</param>
    /// <param name="scale">Scala intera, da 1 a <see cref="MaxScale"/>.</param>
    /// <param name="shadow">Se disegnare l'ombra scura.</param>
    public static void DrawText(Span<byte> rgb, int w, int h, string text, int x, int y, (byte R, byte G, byte B) color, int scale, bool shadow)
    {
        ValidateImage(rgb.Length, w, h);
        ArgumentNullException.ThrowIfNull(text);
        ValidateScale(scale);

        DrawCore(rgb, w, h, text, x, y, color, scale, shadow);
    }

    /// <summary>
    /// Disegna il testo ancorato a un angolo dell'immagine, a distanza <paramref name="margin"/> dai bordi.
    /// L'ingombro include l'ombra, se presente, quindi il testo non tocca il bordo.
    /// </summary>
    /// <param name="rgb">Buffer RGB, di almeno w * h * 3 byte. Modificato sul posto.</param>
    /// <param name="w">Larghezza dell'immagine in pixel.</param>
    /// <param name="h">Altezza dell'immagine in pixel.</param>
    /// <param name="text">Testo da disegnare.</param>
    /// <param name="anchor">Angolo di riferimento.</param>
    /// <param name="margin">Distanza dai bordi in pixel, maggiore o uguale a zero.</param>
    /// <param name="color">Colore del testo.</param>
    /// <param name="scale">Scala intera, da 1 a <see cref="MaxScale"/>.</param>
    /// <param name="shadow">Se disegnare l'ombra scura.</param>
    public static void DrawTextAnchored(Span<byte> rgb, int w, int h, string text, OverlayAnchor anchor, int margin, (byte R, byte G, byte B) color, int scale, bool shadow)
    {
        ValidateImage(rgb.Length, w, h);
        ArgumentNullException.ThrowIfNull(text);
        ValidateScale(scale);
        if (margin < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(margin), "Il margine deve essere maggiore o uguale a zero.");
        }

        (int textW, int textH) = MeasureText(text, scale, shadow);
        long x;
        long y;
        switch (anchor)
        {
            case OverlayAnchor.TopLeft:
                x = margin;
                y = margin;
                break;
            case OverlayAnchor.TopRight:
                x = (long)w - margin - textW;
                y = margin;
                break;
            case OverlayAnchor.BottomLeft:
                x = margin;
                y = (long)h - margin - textH;
                break;
            case OverlayAnchor.BottomRight:
                x = (long)w - margin - textW;
                y = (long)h - margin - textH;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(anchor), "Angolo di ancoraggio non valido.");
        }

        DrawCore(rgb, w, h, text, x, y, color, scale, shadow);
    }

    /// <summary>Valida una scala del testo: intera tra 1 e <see cref="MaxScale"/>.</summary>
    internal static void ValidateScale(int scale)
    {
        if (scale < 1 || scale > MaxScale)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "La scala del testo deve essere tra 1 e 4.");
        }
    }

    private static void DrawCore(Span<byte> rgb, int w, int h, string text, long x, long y, (byte R, byte G, byte B) color, int scale, bool shadow)
    {
        if (shadow)
        {
            DrawGlyphs(rgb, w, h, text, x + ShadowOffsetPx, y + ShadowOffsetPx, ShadowColor, scale);
        }

        DrawGlyphs(rgb, w, h, text, x, y, color, scale);
    }

    private static void DrawGlyphs(Span<byte> rgb, int w, int h, string text, long x0, long y0, (byte R, byte G, byte B) color, int scale)
    {
        long advance = (long)BitmapFont.GlyphWidth * scale;
        long glyphHeight = (long)BitmapFont.GlyphHeight * scale;
        if (y0 >= h || y0 + glyphHeight <= 0)
        {
            return;
        }

        for (int i = 0; i < text.Length; i++)
        {
            long gx = x0 + i * advance;
            if (gx >= w)
            {
                break;
            }

            if (gx + advance <= 0)
            {
                continue;
            }

            ReadOnlySpan<byte> glyph = BitmapFont.GetGlyph(text[i]);
            for (int row = 0; row < BitmapFont.GlyphHeight; row++)
            {
                byte bits = glyph[row];
                if (bits == 0)
                {
                    continue;
                }

                for (int col = 0; col < BitmapFont.GlyphWidth; col++)
                {
                    if ((bits & (0x80 >> col)) == 0)
                    {
                        continue;
                    }

                    long px = gx + (long)col * scale;
                    long py = y0 + (long)row * scale;
                    for (int sy = 0; sy < scale; sy++)
                    {
                        for (int sx = 0; sx < scale; sx++)
                        {
                            SetPixel(rgb, w, h, px + sx, py + sy, color);
                        }
                    }
                }
            }
        }
    }

    private static void SetPixel(Span<byte> rgb, int w, int h, long x, long y, (byte R, byte G, byte B) color)
    {
        if (x < 0 || y < 0 || x >= w || y >= h)
        {
            return;
        }

        int i = ((int)y * w + (int)x) * Channels;
        rgb[i] = color.R;
        rgb[i + 1] = color.G;
        rgb[i + 2] = color.B;
    }

    private static void ValidateImage(int rgbLength, int w, int h)
    {
        if (w < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(w), "La larghezza non puo' essere negativa.");
        }

        if (h < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(h), "L'altezza non puo' essere negativa.");
        }

        if ((long)w * h * Channels > rgbLength)
        {
            throw new ArgumentException("Il buffer e' troppo piccolo per w * h * 3 byte.", nameof(rgbLength));
        }
    }
}
