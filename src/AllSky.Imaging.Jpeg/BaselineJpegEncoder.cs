using System.Numerics;
using AllSky.Core;

namespace AllSky.Imaging.Jpeg;

/// <summary>
/// Codificatore JPEG baseline sequenziale (ITU-T T.81), scritto da zero con la sola BCL.
/// Scelte (semplici, da documentare se cambiano):
/// <list type="bullet">
/// <item>Colore JFIF (YCbCr BT.601 a piena gamma), APP0 JFIF 1.01, nessun profilo ICC.</item>
/// <item>Sottocampionamento 4:2:0 con quality &lt; 95, 4:4:4 con quality &gt;= 95. Il 4:2:0 è una media dei 2x2 pixel.</item>
/// <item>Bordi: le coordinate fuori immagine sono limitate al bordo (replica), sia per il padding a MCU
/// sia per il sottocampionamento. Il decoder ritaglia alle dimensioni del SOF0.</item>
/// <item>Quantizzazione: tabelle Annex K scalate come libjpeg; DCT separabile in float con matrice precalcolata.</item>
/// <item>Huffman: tabelle standard Annex K (nessuna ottimizzazione); i coefficienti DC/AC sono limitati
/// alle categorie previste dalla baseline (DC in -1024..1023, AC in -1023..1023).</item>
/// </list>
/// La classe non ha stato mutabile: una stessa istanza può essere usata da più thread.
/// </summary>
public sealed class BaselineJpegEncoder : IJpegEncoder
{
    private const int MaxDimension = 65535;
    private const int QualitySubsamplingThreshold = 95;
    private const float LumaBias = 128f;

    /// <summary>Codifica l'immagine RGB 8 bit in un file JPEG baseline.</summary>
    /// <param name="rgb8">Pixel in ordine R,G,B, righe contigue; lunghezza esatta width*height*3.</param>
    /// <param name="width">Larghezza in pixel, 1..65535.</param>
    /// <param name="height">Altezza in pixel, 1..65535.</param>
    /// <param name="quality">Qualità 1..100 (scala libjpeg: 100 = quantizzazione unitaria).</param>
    /// <returns>Byte del file JPEG (SOI ... EOI).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Se quality, width o height sono fuori range.</exception>
    /// <exception cref="ArgumentException">Se la lunghezza di rgb8 non è width*height*3.</exception>
    public byte[] Encode(ReadOnlySpan<byte> rgb8, int width, int height, int quality)
    {
        ValidateArguments(rgb8, width, height, quality);

        bool subsample420 = quality < QualitySubsamplingThreshold;
        int mcuSize = subsample420 ? 16 : 8;
        int blocksPerSide = mcuSize / 8;
        int chromaStep = mcuSize / 8;

        int[] lumaQuant = JpegTables.ScaleQuantTable(JpegTables.LuminanceQuant, quality);
        int[] chromaQuant = JpegTables.ScaleQuantTable(JpegTables.ChrominanceQuant, quality);

        using var writer = new JpegBitWriter(Math.Max(4096, rgb8.Length / 8));

        WriteHeaders(writer, width, height, subsample420, lumaQuant, chromaQuant);
        WriteScan(writer, rgb8, width, height, mcuSize, blocksPerSide, chromaStep, lumaQuant, chromaQuant);

        writer.FlushBits();
        writer.WriteMarker(0xD9); // EOI
        return writer.ToArray();
    }

    private static void ValidateArguments(ReadOnlySpan<byte> rgb8, int width, int height, int quality)
    {
        if (quality < 1 || quality > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(quality), quality, "La qualità deve essere compresa tra 1 e 100.");
        }

        if (width < 1 || width > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "La larghezza deve essere compresa tra 1 e 65535.");
        }

        if (height < 1 || height > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "L'altezza deve essere compresa tra 1 e 65535.");
        }

        long expected = (long)width * height * 3;
        if (rgb8.Length != expected)
        {
            throw new ArgumentException($"La lunghezza di rgb8 deve essere {expected} byte (width*height*3).", nameof(rgb8));
        }
    }

    /// <summary>Scrive SOI, APP0 JFIF, DQT (luminanza e crominanza), SOF0, DHT (4 tabelle) e SOS.</summary>
    private static void WriteHeaders(JpegBitWriter writer, int width, int height, bool subsample420, int[] lumaQuant, int[] chromaQuant)
    {
        writer.WriteMarker(0xD8); // SOI

        // APP0 JFIF 1.01, densità 1:1, nessuna miniatura.
        writer.WriteMarker(0xE0);
        writer.WriteUInt16(16);
        foreach (byte b in "JFIF\0"u8)
        {
            writer.WriteByte(b);
        }

        writer.WriteByte(1);
        writer.WriteByte(1);
        writer.WriteByte(0); // unità: nessuna
        writer.WriteUInt16(1);
        writer.WriteUInt16(1);
        writer.WriteByte(0);
        writer.WriteByte(0);

        WriteQuantTable(writer, 0, lumaQuant);
        WriteQuantTable(writer, 1, chromaQuant);

        // SOF0: precisione 8, dimensioni, 3 componenti (Y, Cb, Cr) con campionamento e tabella Q.
        writer.WriteMarker(0xC0);
        writer.WriteUInt16(8 + 3 * 3);
        writer.WriteByte(8);
        writer.WriteUInt16(height);
        writer.WriteUInt16(width);
        writer.WriteByte(3);
        writer.WriteByte(1);
        writer.WriteByte(subsample420 ? (byte)0x22 : (byte)0x11);
        writer.WriteByte(0);
        writer.WriteByte(2);
        writer.WriteByte(0x11);
        writer.WriteByte(1);
        writer.WriteByte(3);
        writer.WriteByte(0x11);
        writer.WriteByte(1);

        WriteHuffmanTable(writer, 0, 0, JpegTables.DcLuminanceBits, JpegTables.DcLuminanceValues);
        WriteHuffmanTable(writer, 1, 0, JpegTables.AcLuminanceBits, JpegTables.AcLuminanceValues);
        WriteHuffmanTable(writer, 0, 1, JpegTables.DcChrominanceBits, JpegTables.DcChrominanceValues);
        WriteHuffmanTable(writer, 1, 1, JpegTables.AcChrominanceBits, JpegTables.AcChrominanceValues);

        // SOS: scansione unica interleaved, Ss=0, Se=63, Ah=Al=0.
        writer.WriteMarker(0xDA);
        writer.WriteUInt16(6 + 2 * 3);
        writer.WriteByte(3);
        writer.WriteByte(1);
        writer.WriteByte(0x00);
        writer.WriteByte(2);
        writer.WriteByte(0x11);
        writer.WriteByte(3);
        writer.WriteByte(0x11);
        writer.WriteByte(0);
        writer.WriteByte(63);
        writer.WriteByte(0);
    }

    /// <summary>Scrive un segmento DQT con una tabella a 8 bit, valori in ordine zigzag come richiede la specifica.</summary>
    private static void WriteQuantTable(JpegBitWriter writer, int tableId, int[] naturalOrder)
    {
        writer.WriteMarker(0xDB);
        writer.WriteUInt16(2 + 1 + 64);
        writer.WriteByte((byte)tableId);
        for (int k = 0; k < 64; k++)
        {
            writer.WriteByte((byte)naturalOrder[JpegTables.ZigZag[k]]);
        }
    }

    /// <summary>Scrive un segmento DHT con una tabella (classe 0 = DC, 1 = AC).</summary>
    private static void WriteHuffmanTable(JpegBitWriter writer, int tableClass, int tableId, byte[] bits, byte[] values)
    {
        writer.WriteMarker(0xC4);
        writer.WriteUInt16(2 + 1 + 16 + values.Length);
        writer.WriteByte((byte)((tableClass << 4) | tableId));
        foreach (byte count in bits)
        {
            writer.WriteByte(count);
        }

        foreach (byte value in values)
        {
            writer.WriteByte(value);
        }
    }

    /// <summary>Scrive i dati entropici: MCU in ordine raster, blocchi Y (raster nel MCU), poi Cb e Cr.</summary>
    private static void WriteScan(
        JpegBitWriter writer,
        ReadOnlySpan<byte> rgb8,
        int width,
        int height,
        int mcuSize,
        int blocksPerSide,
        int chromaStep,
        int[] lumaQuant,
        int[] chromaQuant)
    {
        int mcusX = (width + mcuSize - 1) / mcuSize;
        int mcusY = (height + mcuSize - 1) / mcuSize;

        Span<float> samples = stackalloc float[64];
        Span<float> coefficients = stackalloc float[64];
        Span<float> temp = stackalloc float[64];
        Span<int> zigzag = stackalloc int[64];

        // Predittori DC per Y, Cb, Cr.
        Span<int> dcPredictor = stackalloc int[3];

        for (int my = 0; my < mcusY; my++)
        {
            for (int mx = 0; mx < mcusX; mx++)
            {
                int originX = mx * mcuSize;
                int originY = my * mcuSize;

                for (int by = 0; by < blocksPerSide; by++)
                {
                    for (int bx = 0; bx < blocksPerSide; bx++)
                    {
                        FillLumaBlock(rgb8, width, height, originX + bx * 8, originY + by * 8, samples);
                        EncodeBlock(writer, samples, coefficients, temp, zigzag, lumaQuant, dcPredictor, 0, true);
                    }
                }

                FillChromaBlock(rgb8, width, height, originX, originY, chromaStep, isCb: true, samples);
                EncodeBlock(writer, samples, coefficients, temp, zigzag, chromaQuant, dcPredictor, 1, false);

                FillChromaBlock(rgb8, width, height, originX, originY, chromaStep, isCb: false, samples);
                EncodeBlock(writer, samples, coefficients, temp, zigzag, chromaQuant, dcPredictor, 2, false);
            }
        }
    }

    /// <summary>Riempie un blocco 8x8 di luminanza (Y - 128). Le coordinate oltre il bordo sono replicate.</summary>
    private static void FillLumaBlock(ReadOnlySpan<byte> rgb8, int width, int height, int x0, int y0, Span<float> dst)
    {
        for (int v = 0; v < 8; v++)
        {
            int sy = Math.Min(y0 + v, height - 1);
            for (int u = 0; u < 8; u++)
            {
                int sx = Math.Min(x0 + u, width - 1);
                int i = (sy * width + sx) * 3;
                float r = rgb8[i];
                float g = rgb8[i + 1];
                float b = rgb8[i + 2];
                dst[v * 8 + u] = 0.299f * r + 0.587f * g + 0.114f * b - LumaBias;
            }
        }
    }

    /// <summary>
    /// Riempie un blocco 8x8 di crominanza. Ogni campione copre un quadrato step x step di pixel
    /// (step=2 per 4:2:0, step=1 per 4:4:4) e ne fa la media. Il risultato è centrato (Cb-128 o Cr-128).
    /// </summary>
    private static void FillChromaBlock(
        ReadOnlySpan<byte> rgb8,
        int width,
        int height,
        int originX,
        int originY,
        int step,
        bool isCb,
        Span<float> dst)
    {
        float inverseCount = 1f / (step * step);
        for (int v = 0; v < 8; v++)
        {
            for (int u = 0; u < 8; u++)
            {
                float sum = 0f;
                for (int dy = 0; dy < step; dy++)
                {
                    int sy = Math.Min(originY + v * step + dy, height - 1);
                    for (int dx = 0; dx < step; dx++)
                    {
                        int sx = Math.Min(originX + u * step + dx, width - 1);
                        int i = (sy * width + sx) * 3;
                        float r = rgb8[i];
                        float g = rgb8[i + 1];
                        float b = rgb8[i + 2];
                        sum += isCb
                            ? -0.168736f * r - 0.331264f * g + 0.5f * b
                            : 0.5f * r - 0.418688f * g - 0.081312f * b;
                    }
                }

                dst[v * 8 + u] = sum * inverseCount;
            }
        }
    }

    /// <summary>DCT, quantizzazione, zigzag e codifica Huffman di un blocco 8x8.</summary>
    private static void EncodeBlock(
        JpegBitWriter writer,
        ReadOnlySpan<float> samples,
        Span<float> coefficients,
        Span<float> temp,
        Span<int> zigzag,
        int[] quant,
        Span<int> dcPredictor,
        int component,
        bool luma)
    {
        ForwardDct(samples, temp, coefficients);

        for (int k = 0; k < 64; k++)
        {
            int natural = JpegTables.ZigZag[k];
            int q = (int)MathF.Round(coefficients[natural] / quant[natural]);
            zigzag[k] = k == 0 ? Math.Clamp(q, -1024, 1023) : Math.Clamp(q, -1023, 1023);
        }

        HuffmanCodeTable dcTable = luma ? JpegTables.DcLuminance : JpegTables.DcChrominance;
        HuffmanCodeTable acTable = luma ? JpegTables.AcLuminance : JpegTables.AcChrominance;

        // DC: differenza rispetto al blocco precedente della stessa componente.
        int diff = zigzag[0] - dcPredictor[component];
        dcPredictor[component] = zigzag[0];
        int dcCategory = Category(diff);
        WriteSymbol(writer, dcTable, dcCategory);
        WriteAmplitude(writer, diff, dcCategory);

        // AC: coppie (run, size) sulle posizioni 1..63 in ordine zigzag.
        int run = 0;
        for (int k = 1; k < 64; k++)
        {
            int value = zigzag[k];
            if (value == 0)
            {
                run++;
                continue;
            }

            while (run > 15)
            {
                WriteSymbol(writer, acTable, 0xF0); // ZRL: 16 zeri
                run -= 16;
            }

            int category = Category(value);
            WriteSymbol(writer, acTable, (run << 4) | category);
            WriteAmplitude(writer, value, category);
            run = 0;
        }

        if (run > 0)
        {
            WriteSymbol(writer, acTable, 0x00); // EOB
        }
    }

    /// <summary>
    /// DCT-II 8x8 separabile in float: F(u,v) = sum_x sum_y C(u,x) C(v,y) f(x,y), con C(u,x) = c(u)/2 cos((2x+1)u*pi/16).
    /// Le righe vanno in <paramref name="temp"/>, poi le colonne in <paramref name="dst"/>.
    /// </summary>
    private static void ForwardDct(ReadOnlySpan<float> src, Span<float> temp, Span<float> dst)
    {
        ReadOnlySpan<float> basis = DctBasis;
        for (int y = 0; y < 8; y++)
        {
            for (int u = 0; u < 8; u++)
            {
                float sum = 0f;
                for (int x = 0; x < 8; x++)
                {
                    sum += src[y * 8 + x] * basis[u * 8 + x];
                }

                temp[y * 8 + u] = sum;
            }
        }

        for (int v = 0; v < 8; v++)
        {
            for (int u = 0; u < 8; u++)
            {
                float sum = 0f;
                for (int y = 0; y < 8; y++)
                {
                    sum += temp[y * 8 + u] * basis[v * 8 + y];
                }

                dst[v * 8 + u] = sum;
            }
        }
    }

    /// <summary>Matrice di base della DCT: DctBasis[u*8+x] = c(u)/2 * cos((2x+1)*u*pi/16), precalcolata una volta.</summary>
    private static readonly float[] DctBasis = BuildDctBasis();

    private static float[] BuildDctBasis()
    {
        var basis = new float[64];
        for (int u = 0; u < 8; u++)
        {
            double cu = u == 0 ? 1.0 / Math.Sqrt(2.0) : 1.0;
            for (int x = 0; x < 8; x++)
            {
                basis[u * 8 + x] = (float)(cu / 2.0 * Math.Cos((2 * x + 1) * u * Math.PI / 16.0));
            }
        }

        return basis;
    }

    /// <summary>Categoria di un valore: numero di bit necessari a |value| (0 se value è 0).</summary>
    private static int Category(int value)
    {
        int magnitude = Math.Abs(value);
        return magnitude == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)magnitude);
    }

    /// <summary>Scrive il codice Huffman di un simbolo.</summary>
    private static void WriteSymbol(JpegBitWriter writer, HuffmanCodeTable table, int symbol)
    {
        writer.PutBits(table.CodeOf(symbol), table.SizeOf(symbol));
    }

    /// <summary>Scrive i bit di ampiezza: il valore se positivo, valore + 2^cat - 1 se negativo (complemento).</summary>
    private static void WriteAmplitude(JpegBitWriter writer, int value, int category)
    {
        if (category == 0)
        {
            return;
        }

        int bits = value >= 0 ? value : value + (1 << category) - 1;
        writer.PutBits((uint)bits, category);
    }
}
