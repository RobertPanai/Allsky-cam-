using System.Numerics;

namespace AllSky.Products;

/// <summary>
/// Startrails incrementale: accumulatore "lighten" (massimo per canale e per pixel) su un unico buffer
/// di <c>W * H * 3</c> byte. Con <c>decay</c> minore di 1 l'accumulo sfuma prima di ogni massimo, così le
/// code delle stelle si attenuano invece di restare a piena luminosità.
/// </summary>
/// <remarks>
/// Scelta semplice per il decadimento: ogni valore accumulato v diventa <c>floor(v * decay)</c> (tabella di 256 voci),
/// quindi un valore positivo cala sempre di almeno 1 a ogni frame. Con <c>decay = 1</c> non c'è alcun decadimento.
/// </remarks>
public sealed class StartrailsBuilder
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _accumulator;

    // Tabella di decadimento; null quando decay == 1 (nessun decadimento).
    private readonly byte[]? _decayTable;
    private int _framesAdded;

    /// <summary>Crea un accumulatore per frame di <paramref name="width"/> per <paramref name="height"/> pixel.</summary>
    /// <param name="width">Larghezza in pixel (almeno 1).</param>
    /// <param name="height">Altezza in pixel (almeno 1).</param>
    /// <param name="decay">Fattore di decadimento in 0..1. Default 1 = nessun decadimento.</param>
    /// <exception cref="ArgumentOutOfRangeException">Dimensioni o <paramref name="decay"/> fuori intervallo.</exception>
    public StartrailsBuilder(int width, int height, double decay = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (!(decay >= 0.0 && decay <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(decay), "Il decadimento deve essere compreso tra 0 e 1.");
        }

        _width = width;
        _height = height;
        _accumulator = new byte[checked(width * height * 3)];
        Decay = decay;

        if (decay < 1.0)
        {
            _decayTable = new byte[256];
            for (int v = 0; v < 256; v++)
            {
                _decayTable[v] = (byte)Math.Floor(v * decay);
            }
        }
    }

    /// <summary>Fattore di decadimento usato, in 0..1 (1 = nessun decadimento).</summary>
    public double Decay { get; }

    /// <summary>Numero di frame effettivamente accumulati (esclusi quelli saltati con <c>skip</c>).</summary>
    public int FramesAdded => _framesAdded;

    /// <summary>
    /// Accumula un frame: ogni canale di ogni pixel diventa il massimo tra il valore accumulato (dopo il decadimento) e il frame.
    /// </summary>
    /// <param name="rgb8">Frame RGB 8 bit, row-major, lunghezza esattamente <c>W * H * 3</c>.</param>
    /// <param name="skip">Se true il frame viene ignorato (per esempio un frame diurno). La lunghezza è controllata comunque.</param>
    /// <exception cref="ArgumentException">Lunghezza di <paramref name="rgb8"/> diversa da <c>W * H * 3</c>.</exception>
    public void AddFrame(ReadOnlySpan<byte> rgb8, bool skip = false)
    {
        if (rgb8.Length != _accumulator.Length)
        {
            throw new ArgumentException(
                $"Il frame deve avere {_accumulator.Length} byte (RGB 8 bit), ricevuti {rgb8.Length}.", nameof(rgb8));
        }

        if (skip)
        {
            return;
        }

        _framesAdded++;

        if (_decayTable is null)
        {
            MaxVectorized(rgb8);
        }
        else
        {
            MaxWithDecay(rgb8, _decayTable);
        }
    }

    /// <summary>Restituisce una copia dell'immagine accumulata, RGB 8 bit row-major di <c>W * H * 3</c> byte.</summary>
    public byte[] Build()
    {
        return (byte[])_accumulator.Clone();
    }

    /// <summary>Massimo per canale senza decadimento, con Vector&lt;byte&gt; e coda scalare.</summary>
    private void MaxVectorized(ReadOnlySpan<byte> rgb8)
    {
        int count = Vector<byte>.Count;
        int length = _accumulator.Length;
        int i = 0;
        int last = length - count;

        for (; i <= last; i += count)
        {
            var acc = new Vector<byte>(_accumulator, i);
            var frame = new Vector<byte>(rgb8.Slice(i, count));
            Vector.Max(acc, frame).CopyTo(_accumulator, i);
        }

        for (; i < length; i++)
        {
            _accumulator[i] = Math.Max(_accumulator[i], rgb8[i]);
        }
    }

    /// <summary>Decadimento tramite tabella, poi massimo per canale.</summary>
    private void MaxWithDecay(ReadOnlySpan<byte> rgb8, byte[] decayTable)
    {
        byte[] acc = _accumulator;
        for (int i = 0; i < acc.Length; i++)
        {
            byte faded = decayTable[acc[i]];
            byte frame = rgb8[i];
            acc[i] = faded > frame ? faded : frame;
        }
    }
}
