namespace AllSky.Imaging.Jpeg;

/// <summary>
/// Tabelle JPEG standard (ITU-T T.81, Annex K) e funzioni di supporto.
/// Fonte dei valori: estratti da un JPEG prodotto da libjpeg-turbo (Pillow, optimize=False, quality=50),
/// e confrontati con le Tabelle K.1-K.6 della specifica. Le quantizzazioni sono in ordine naturale (riga*8+colonna).
/// </summary>
internal static class JpegTables
{
    /// <summary>Ordine zigzag: ZigZag[k] è l'indice naturale (riga*8+colonna) del k-esimo coefficiente.</summary>
    public static readonly int[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    /// <summary>Tabella di quantizzazione base per la luminanza (Tabella K.1), ordine naturale.</summary>
    public static readonly byte[] LuminanceQuant =
    [
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77,
        24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103, 99,
    ];

    /// <summary>Tabella di quantizzazione base per la crominanza (Tabella K.2), ordine naturale.</summary>
    public static readonly byte[] ChrominanceQuant =
    [
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
    ];

    /// <summary>Conteggi per lunghezza (1..16) della tabella DC luminanza (Tabella K.3).</summary>
    public static readonly byte[] DcLuminanceBits = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>Simboli della tabella DC luminanza (Tabella K.3).</summary>
    public static readonly byte[] DcLuminanceValues = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];

    /// <summary>Conteggi per lunghezza della tabella DC crominanza (Tabella K.4).</summary>
    public static readonly byte[] DcChrominanceBits = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];

    /// <summary>Simboli della tabella DC crominanza (Tabella K.4).</summary>
    public static readonly byte[] DcChrominanceValues = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];

    /// <summary>Conteggi per lunghezza della tabella AC luminanza (Tabella K.5).</summary>
    public static readonly byte[] AcLuminanceBits = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D];

    /// <summary>Simboli (run/size) della tabella AC luminanza (Tabella K.5), 162 valori.</summary>
    public static readonly byte[] AcLuminanceValues =
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
        0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
        0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    /// <summary>Conteggi per lunghezza della tabella AC crominanza (Tabella K.6).</summary>
    public static readonly byte[] AcChrominanceBits = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];

    /// <summary>Simboli (run/size) della tabella AC crominanza (Tabella K.6), 162 valori.</summary>
    public static readonly byte[] AcChrominanceValues =
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
        0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34, 0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
        0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
        0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
        0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    ];

    /// <summary>Tabella Huffman DC luminanza (tabella 0).</summary>
    public static readonly HuffmanCodeTable DcLuminance = HuffmanCodeTable.Create(DcLuminanceBits, DcLuminanceValues);

    /// <summary>Tabella Huffman AC luminanza (tabella 0).</summary>
    public static readonly HuffmanCodeTable AcLuminance = HuffmanCodeTable.Create(AcLuminanceBits, AcLuminanceValues);

    /// <summary>Tabella Huffman DC crominanza (tabella 1).</summary>
    public static readonly HuffmanCodeTable DcChrominance = HuffmanCodeTable.Create(DcChrominanceBits, DcChrominanceValues);

    /// <summary>Tabella Huffman AC crominanza (tabella 1).</summary>
    public static readonly HuffmanCodeTable AcChrominance = HuffmanCodeTable.Create(AcChrominanceBits, AcChrominanceValues);

    /// <summary>
    /// Scala una tabella base secondo la formula di libjpeg (jpeg_quality_scaling): quality &lt; 50 usa 5000/q,
    /// altrimenti 200-2q; ogni valore è (base*scala+50)/100 limitato a 1..255 (baseline).
    /// quality fuori da 1..100 viene limitato al range.
    /// </summary>
    /// <returns>Tabella di 64 valori in ordine naturale.</returns>
    public static int[] ScaleQuantTable(byte[] baseTable, int quality)
    {
        int q = Math.Clamp(quality, 1, 100);
        int scale = q < 50 ? 5000 / q : 200 - 2 * q;

        var result = new int[64];
        for (int i = 0; i < 64; i++)
        {
            int value = (baseTable[i] * scale + 50) / 100;
            result[i] = Math.Clamp(value, 1, 255);
        }

        return result;
    }
}

/// <summary>
/// Tabella di codici Huffman canonica (ITU-T T.81, Annex C): per ogni simbolo restituisce codice e lunghezza in bit.
/// </summary>
internal sealed class HuffmanCodeTable
{
    private readonly uint[] _codes = new uint[256];
    private readonly byte[] _sizes = new byte[256];

    private HuffmanCodeTable()
    {
    }

    /// <summary>Codice (allineato a destra) del simbolo.</summary>
    public uint CodeOf(int symbol) => _codes[symbol];

    /// <summary>Lunghezza in bit del codice del simbolo (0 se il simbolo non è presente nella tabella).</summary>
    public int SizeOf(int symbol) => _sizes[symbol];

    /// <summary>Costruisce la tabella dai conteggi per lunghezza (bits[0..15] = numero di codici di lunghezza 1..16) e dai simboli.</summary>
    public static HuffmanCodeTable Create(byte[] bits, byte[] values)
    {
        var table = new HuffmanCodeTable();
        uint code = 0;
        int k = 0;
        for (int length = 1; length <= 16; length++)
        {
            for (int i = 0; i < bits[length - 1]; i++)
            {
                int symbol = values[k++];
                table._codes[symbol] = code;
                table._sizes[symbol] = (byte)length;
                code++;
            }

            code <<= 1;
        }

        return table;
    }
}
