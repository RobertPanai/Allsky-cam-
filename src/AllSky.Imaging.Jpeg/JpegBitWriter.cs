using System.Buffers;

namespace AllSky.Imaging.Jpeg;

/// <summary>
/// Scrittore di byte e di bit per il flusso JPEG. Il buffer viene noleggiato da <see cref="ArrayPool{T}"/>.
/// I bit vengono scritti MSB-first e ogni 0xFF prodotto nei dati entropici è seguito da 0x00 (bit stuffing).
/// </summary>
internal sealed class JpegBitWriter : IDisposable
{
    private byte[] _buffer;
    private int _length;
    private uint _bits;
    private int _bitCount;
    private bool _disposed;

    /// <summary>Crea lo scrittore con una capacità iniziale indicativa.</summary>
    public JpegBitWriter(int initialCapacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(256, initialCapacity));
    }

    /// <summary>Scrive un byte grezzo (senza bit stuffing), usato per segmenti e marker.</summary>
    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    /// <summary>Scrive un valore a 16 bit in big-endian.</summary>
    public void WriteUInt16(int value)
    {
        WriteByte((byte)(value >> 8));
        WriteByte((byte)value);
    }

    /// <summary>Scrive il marker FF xx.</summary>
    public void WriteMarker(byte marker)
    {
        WriteByte(0xFF);
        WriteByte(marker);
    }

    /// <summary>Accoda <paramref name="count"/> bit (1..16) di <paramref name="value"/>, MSB-first, con bit stuffing.</summary>
    public void PutBits(uint value, int count)
    {
        _bits = (_bits << count) | (value & ((1u << count) - 1));
        _bitCount += count;

        while (_bitCount >= 8)
        {
            _bitCount -= 8;
            byte b = (byte)(_bits >> _bitCount);
            WriteByte(b);
            if (b == 0xFF)
            {
                WriteByte(0x00);
            }
        }

        _bits &= (1u << _bitCount) - 1;
    }

    /// <summary>Completa l'ultimo byte con bit a 1 (come richiesto dalla specifica) e lo scrive.</summary>
    public void FlushBits()
    {
        if (_bitCount > 0)
        {
            int pad = 8 - _bitCount;
            PutBits((1u << pad) - 1, pad);
        }
    }

    /// <summary>Copia il contenuto scritto in un array nuovo.</summary>
    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

    /// <summary>Restituisce il buffer ad ArrayPool.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _disposed = true;
        }
    }

    private void EnsureCapacity(int extra)
    {
        if (_length + extra <= _buffer.Length)
        {
            return;
        }

        int newSize = (int)Math.Min((long)Math.Max(_buffer.Length * 2, _length + extra), Array.MaxLength);
        byte[] bigger = ArrayPool<byte>.Shared.Rent(newSize);
        _buffer.AsSpan(0, _length).CopyTo(bigger);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = bigger;
    }
}
