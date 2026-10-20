using System.Buffers.Binary;
using System.Text;

namespace AllSky.Cameras.Alpaca.Tests;

/// <summary>Costruisce risposte ImageBytes di prova secondo la spec (header di 44 byte little-endian + dati).</summary>
internal static class ImageBytesFixture
{
    /// <summary>Byte di riempimento tra header e dati quando DataStart e' maggiore di 44.</summary>
    internal const byte PaddingByte = 0xAA;

    /// <summary>
    /// Costruisce un'immagine ImageBytes. I dati sono scritti colonna per colonna: x esterno, y interno.
    /// </summary>
    /// <param name="width">NumX.</param>
    /// <param name="height">NumY.</param>
    /// <param name="transmissionType">Codice TransmissionElementType (1 Int16, 2 Int32, 6 Byte, 8 UInt16).</param>
    /// <param name="value">Valore di (x, y) prima della conversione nel tipo di trasmissione.</param>
    /// <param name="dataStart">Offset dei dati; 44 se non c'e' padding.</param>
    /// <param name="metadataVersion">Versione dei metadati.</param>
    internal static byte[] Build(
        int width,
        int height,
        int transmissionType,
        Func<int, int, long> value,
        int dataStart = 44,
        int metadataVersion = 1)
    {
        int elementSize = ElementSize(transmissionType);
        var result = new byte[dataStart + (width * height * elementSize)];
        Array.Fill(result, PaddingByte, 44, dataStart - 44);

        WriteHeader(result, metadataVersion, 0, dataStart, transmissionType, 2, width, height, 0);

        int k = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                WriteElement(result.AsSpan(dataStart + (k * elementSize)), transmissionType, value(x, y));
                k++;
            }
        }

        return result;
    }

    /// <summary>Costruisce una risposta ImageBytes con ErrorNumber diverso da zero e messaggio UTF-8 nei dati.</summary>
    /// <param name="errorNumber">Codice di errore.</param>
    /// <param name="message">Messaggio, codificato in UTF-8 senza terminatore.</param>
    internal static byte[] BuildError(int errorNumber, string message)
    {
        byte[] text = Encoding.UTF8.GetBytes(message);
        var result = new byte[44 + text.Length];
        WriteHeader(result, 1, errorNumber, 44, 2, 2, 0, 0, 0);
        text.CopyTo(result, 44);
        return result;
    }

    private static void WriteHeader(
        byte[] buffer,
        int metadataVersion,
        int errorNumber,
        int dataStart,
        int transmissionType,
        int rank,
        int dim1,
        int dim2,
        int dim3)
    {
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0), metadataVersion);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), errorNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), 7u);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), 9u);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), dataStart);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20), 2);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(24), transmissionType);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(28), rank);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(32), dim1);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(36), dim2);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(40), dim3);
    }

    private static int ElementSize(int transmissionType)
    {
        return transmissionType switch
        {
            1 or 8 => 2,
            2 => 4,
            6 => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(transmissionType)),
        };
    }

    private static void WriteElement(Span<byte> target, int transmissionType, long value)
    {
        switch (transmissionType)
        {
            case 1:
                BinaryPrimitives.WriteInt16LittleEndian(target, (short)value);
                break;
            case 8:
                BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)value);
                break;
            case 2:
                BinaryPrimitives.WriteInt32LittleEndian(target, (int)value);
                break;
            case 6:
                target[0] = (byte)value;
                break;
        }
    }
}
