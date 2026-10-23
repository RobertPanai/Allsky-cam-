using System.Globalization;

namespace AllSky.Studio.Tests;

/// <summary>
/// Decoder finto: genera immagini sintetiche dal nome "frame_NNNN.jpg" (l'indice è NNNN).
/// Registra gli indici decodificati, nell'ordine delle chiamate, e può dichiarare alcuni frame illeggibili.
/// </summary>
internal sealed class SyntheticFrameDecoder : IFrameDecoder
{
    private readonly int _width;
    private readonly int _height;
    private readonly Func<int, int, int, (byte R, byte G, byte B)> _pixel;
    private readonly Func<int, bool>? _unreadable;
    private readonly Action<int>? _onDecode;

    /// <param name="width">Larghezza dei frame sintetici.</param>
    /// <param name="height">Altezza dei frame sintetici.</param>
    /// <param name="pixel">Colore del pixel (indice frame, x, y). Null = <see cref="DefaultPixel"/>.</param>
    /// <param name="unreadable">Frame da dichiarare illeggibili (per indice). Null = nessuno.</param>
    /// <param name="onDecode">Chiamata dopo ogni decodifica, con il numero della chiamata (dal 1).</param>
    public SyntheticFrameDecoder(
        int width = 64,
        int height = 64,
        Func<int, int, int, (byte R, byte G, byte B)>? pixel = null,
        Func<int, bool>? unreadable = null,
        Action<int>? onDecode = null)
    {
        _width = width;
        _height = height;
        _pixel = pixel ?? ((f, x, y) => DefaultPixel(f, x, y));
        _unreadable = unreadable;
        _onDecode = onDecode;
    }

    /// <summary>Indici dei frame decodificati, nell'ordine delle chiamate.</summary>
    public List<int> DecodedIndices { get; } = new();

    /// <summary>
    /// Colore di riferimento: R = 8·f (uniforme sul frame, cambia da frame a frame), G = 4·y, B = 4·x.
    /// Con f ≤ 31, x e y ≤ 63 i valori stanno in un byte senza overflow.
    /// </summary>
    public static (byte R, byte G, byte B) DefaultPixel(int frameIndex, int x, int y)
    {
        return ((byte)(8 * frameIndex), (byte)(4 * y), (byte)(4 * x));
    }

    public RgbImage? Decode(StudioFrame frame)
    {
        int index = IndexOf(frame);
        DecodedIndices.Add(index);
        _onDecode?.Invoke(DecodedIndices.Count);
        if (_unreadable?.Invoke(index) == true)
        {
            return null;
        }

        var pixels = new byte[_width * _height * 3];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                (byte r, byte g, byte b) = _pixel(index, x, y);
                int offset = (y * _width + x) * 3;
                pixels[offset] = r;
                pixels[offset + 1] = g;
                pixels[offset + 2] = b;
            }
        }

        return RgbImage.Of(_width, _height, pixels);
    }

    /// <summary>Indice dal nome "frame_NNNN.jpg".</summary>
    public static int IndexOf(StudioFrame frame)
    {
        return int.Parse(frame.FileName.Substring(6, 4), CultureInfo.InvariantCulture);
    }
}

/// <summary>Raccoglie i valori di avanzamento in modo sincrono (nessun contesto di sincronizzazione).</summary>
internal sealed class ProgressRecorder : IProgress<double>
{
    public List<double> Values { get; } = new();

    public void Report(double value) => Values.Add(value);
}
