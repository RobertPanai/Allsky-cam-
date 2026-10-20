using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace AllSky.Cameras.Alpaca;

/// <summary>
/// Lettore del formato ImageBytes ASCOM Alpaca (solo BCL): header di 44 byte little-endian, poi i dati.
/// Layout e ordine dei dati da ASCOM Alpaca API Reference, sezioni 8.7 e 8.8: l'array e' Array[NumX, NumY]
/// e l'indice Y (altezza) varia piu' in fretta, quindi l'elemento (x, y) sta al posto (x * NumY + y).
/// </summary>
internal static class ImageBytesReader
{
    /// <summary>Tipo MIME di ImageBytes, usato sia nell'header Accept sia nella risposta.</summary>
    internal const string MediaType = "application/imagebytes";

    /// <summary>Lunghezza fissa dell'header dei metadati.</summary>
    internal const int HeaderLength = 44;

    /// <summary>Unica versione dei metadati definita dalla spec.</summary>
    private const int SupportedMetadataVersion = 1;

    /// <summary>Limite al padding tra header e dati, per evitare allocazioni enormi da un header corrotto.</summary>
    private const int MaxPaddingBytes = 1 << 20;

    /// <summary>Numero di elementi letti per volta dallo stream.</summary>
    private const int ChunkElements = 4096;

    /// <summary>Codici ImageArrayElementTypes della spec (sez. 8.7.2).</summary>
    internal enum ElementType
    {
        Unknown = 0,
        Int16 = 1,
        Int32 = 2,
        Double = 3,
        Single = 4,
        UInt64 = 5,
        Byte = 6,
        Int64 = 7,
        UInt16 = 8,
        UInt32 = 9,
    }

    /// <summary>Campi dell'header di 44 byte (sez. 8.8.2).</summary>
    internal readonly record struct Header(
        int MetadataVersion,
        int ErrorNumber,
        int DataStart,
        int ImageElementType,
        int TransmissionElementType,
        int Rank,
        int Dimension1,
        int Dimension2,
        int Dimension3);

    /// <summary>
    /// Legge da <paramref name="body"/> un'immagine ImageBytes 2D di <paramref name="width"/> x <paramref name="height"/>
    /// e la scrive in <paramref name="pixels"/> in ordine riga-maggiore (indice = y * width + x).
    /// I valori sono limitati a 0..65535. Se ErrorNumber e' diverso da zero lancia <see cref="AlpacaException"/>
    /// con il messaggio UTF-8 contenuto nei dati.
    /// </summary>
    /// <param name="body">Corpo della risposta, posizionato all'inizio dell'header.</param>
    /// <param name="width">Larghezza attesa (NumX).</param>
    /// <param name="height">Altezza attesa (NumY).</param>
    /// <param name="pixels">Buffer di destinazione, di almeno width * height elementi.</param>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    /// <exception cref="InvalidDataException">Header non valido o dimensioni diverse da quelle attese.</exception>
    /// <exception cref="NotSupportedException">Tipo di elemento o rango non supportati.</exception>
    internal static async Task ReadAsync(
        Stream body,
        int width,
        int height,
        ushort[] pixels,
        CancellationToken cancellationToken)
    {
        byte[] headerBytes = new byte[HeaderLength];
        await body.ReadExactlyAsync(headerBytes.AsMemory(), cancellationToken);
        Header header = ParseHeader(headerBytes);

        if (header.MetadataVersion != SupportedMetadataVersion)
        {
            throw new InvalidDataException($"Versione dei metadati ImageBytes {header.MetadataVersion} non supportata.");
        }

        if (header.DataStart < HeaderLength || header.DataStart - HeaderLength > MaxPaddingBytes)
        {
            throw new InvalidDataException($"Offset dei dati ImageBytes non valido ({header.DataStart}).");
        }

        int padding = header.DataStart - HeaderLength;
        if (padding > 0)
        {
            await body.ReadExactlyAsync(new byte[padding].AsMemory(), cancellationToken);
        }

        if (header.ErrorNumber != 0)
        {
            // Con ErrorNumber != 0 i dati dal DataStart sono il messaggio d'errore UTF-8, senza terminatore.
            using var message = new MemoryStream();
            await body.CopyToAsync(message, cancellationToken);
            throw new AlpacaException(header.ErrorNumber, Encoding.UTF8.GetString(message.ToArray()));
        }

        if (header.Rank != 2)
        {
            throw new NotSupportedException($"Immagine con {header.Rank} dimensioni non supportata: serve un array 2D (sensore Bayer).");
        }

        if (header.Dimension1 != width || header.Dimension2 != height)
        {
            throw new InvalidDataException(
                $"Dimensioni immagine {header.Dimension1}x{header.Dimension2} diverse da quelle della camera ({width}x{height}).");
        }

        var type = (ElementType)header.TransmissionElementType;
        int elementSize = ElementSize(type);
        long count = (long)width * height;
        byte[] chunk = new byte[ChunkElements * elementSize];

        // Ordine dei dati: x esterno, y interno (colonna per colonna). Il contatore (x, y) evita divisioni per pixel.
        int x = 0;
        int y = 0;
        long done = 0;
        while (done < count)
        {
            int n = (int)Math.Min(ChunkElements, count - done);
            await body.ReadExactlyAsync(chunk.AsMemory(0, n * elementSize), cancellationToken);

            for (int i = 0; i < n; i++)
            {
                int value = ReadElement(chunk.AsSpan(i * elementSize, elementSize), type);
                pixels[(y * width) + x] = Clamp16(value);

                y++;
                if (y == height)
                {
                    y = 0;
                    x++;
                }
            }

            done += n;
        }
    }

    /// <summary>
    /// Legge l'immagine dal campo Value di una risposta JSON (array di colonne: Value[x][y]), usata quando il
    /// dispositivo non risponde con ImageBytes.
    /// </summary>
    /// <param name="root">Radice del JSON, con campo Value.</param>
    /// <param name="width">Larghezza attesa.</param>
    /// <param name="height">Altezza attesa.</param>
    /// <param name="pixels">Buffer di destinazione in ordine riga-maggiore.</param>
    /// <exception cref="InvalidDataException">Struttura o dimensioni diverse da quelle attese.</exception>
    internal static void ReadJsonColumns(JsonElement root, int width, int height, ushort[] pixels)
    {
        if (!root.TryGetProperty("Value", out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("La risposta imagearray JSON non contiene un array in Value.");
        }

        if (value.GetArrayLength() != width)
        {
            throw new InvalidDataException($"Numero di colonne JSON {value.GetArrayLength()} diverso dalla larghezza {width}.");
        }

        int x = 0;
        foreach (JsonElement column in value.EnumerateArray())
        {
            if (column.ValueKind != JsonValueKind.Array || column.GetArrayLength() != height)
            {
                throw new InvalidDataException($"La colonna {x} del JSON non ha {height} elementi.");
            }

            int y = 0;
            foreach (JsonElement element in column.EnumerateArray())
            {
                pixels[(y * width) + x] = Clamp16(element.GetInt64());
                y++;
            }

            x++;
        }
    }

    /// <summary>Decodifica i 44 byte dell'header in campi little-endian.</summary>
    /// <param name="bytes">Almeno 44 byte.</param>
    internal static Header ParseHeader(ReadOnlySpan<byte> bytes)
    {
        return new Header(
            MetadataVersion: BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(0, 4)),
            ErrorNumber: BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(4, 4)),
            DataStart: BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(16, 4)),
            ImageElementType: BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20, 4)),
            TransmissionElementType: BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(24, 4)),
            Rank: BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(28, 4)),
            Dimension1: BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(32, 4)),
            Dimension2: BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(36, 4)),
            Dimension3: BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(40, 4)));
    }

    /// <summary>Dimensione in byte di un elemento trasmesso. Solo i tipi che una camera Bayer usa davvero.</summary>
    /// <param name="type">Tipo di elemento trasmesso.</param>
    private static int ElementSize(ElementType type)
    {
        return type switch
        {
            ElementType.Byte => 1,
            ElementType.Int16 or ElementType.UInt16 => 2,
            ElementType.Int32 => 4,
            _ => throw new NotSupportedException($"Tipo di elemento ImageBytes {(int)type} non supportato."),
        };
    }

    private static int ReadElement(ReadOnlySpan<byte> element, ElementType type)
    {
        return type switch
        {
            ElementType.Byte => element[0],
            ElementType.Int16 => BinaryPrimitives.ReadInt16LittleEndian(element),
            ElementType.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(element),
            ElementType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(element),
            _ => throw new NotSupportedException($"Tipo di elemento ImageBytes {(int)type} non supportato."),
        };
    }

    /// <summary>Limita un valore a 0..65535 (un valore negativo diventa 0, uno oltre 65535 diventa 65535).</summary>
    private static ushort Clamp16(long value)
    {
        return (ushort)Math.Clamp(value, 0L, 65535L);
    }
}
