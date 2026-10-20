using Xunit;
using AllSky.Imaging.Jpeg;

namespace AllSky.Imaging.Jpeg.Tests;

public class BaselineJpegEncoderTests
{
    private const byte Soi = 0xD8;
    private const byte Eoi = 0xD9;
    private const byte Sof0 = 0xC0;
    private const byte Dht = 0xC4;
    private const byte Dqt = 0xDB;
    private const byte Sos = 0xDA;
    private const byte App0 = 0xE0;

    private static byte[] Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3;
                pixels[i] = (byte)(width == 1 ? 0 : x * 255 / (width - 1));
                pixels[i + 1] = (byte)(height == 1 ? 0 : y * 255 / (height - 1));
                pixels[i + 2] = 128;
            }
        }

        return pixels;
    }

    private static byte[] Noise(int width, int height, int seed)
    {
        // Seme fisso: i test restano deterministici.
        var pixels = new byte[width * height * 3];
        new Random(seed).NextBytes(pixels);
        return pixels;
    }

    /// <summary>Restituisce i marker dei segmenti tra SOI e SOS (SOS incluso), nell'ordine in cui compaiono.</summary>
    private static List<byte> HeaderMarkers(byte[] jpeg)
    {
        var markers = new List<byte>();
        int pos = 2;
        while (true)
        {
            Assert.Equal(0xFF, jpeg[pos]);
            byte marker = jpeg[pos + 1];
            markers.Add(marker);
            if (marker == Sos)
            {
                return markers;
            }

            int length = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
            pos += 2 + length;
        }
    }

    /// <summary>Restituisce la posizione del payload (dopo la lunghezza) del primo segmento con il marker indicato.</summary>
    private static int PayloadOf(byte[] jpeg, byte marker)
    {
        int pos = 2;
        while (jpeg[pos + 1] != marker)
        {
            Assert.Equal(0xFF, jpeg[pos]);
            int length = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
            pos += 2 + length;
        }

        return pos + 4;
    }

    [Fact]
    public void Encode_StartsWithSoi_AndEndsWithEoi()
    {
        var encoder = new BaselineJpegEncoder();

        byte[] jpeg = encoder.Encode(Gradient(40, 24), 40, 24, 90);

        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(Soi, jpeg[1]);
        Assert.Equal(0xFF, jpeg[^2]);
        Assert.Equal(Eoi, jpeg[^1]);
    }

    [Fact]
    public void Encode_WritesHeaderMarkersInStandardOrder()
    {
        var encoder = new BaselineJpegEncoder();

        byte[] jpeg = encoder.Encode(Gradient(16, 16), 16, 16, 90);

        // APP0 JFIF, due DQT (luminanza, crominanza), SOF0, quattro DHT (DC/AC luminanza e crominanza), SOS.
        Assert.Equal(
            new byte[] { App0, Dqt, Dqt, Sof0, Dht, Dht, Dht, Dht, Sos },
            HeaderMarkers(jpeg));
    }

    [Fact]
    public void Encode_WritesJfifIdentifier()
    {
        var encoder = new BaselineJpegEncoder();

        byte[] jpeg = encoder.Encode(Gradient(8, 8), 8, 8, 75);

        int payload = PayloadOf(jpeg, App0);
        Assert.Equal(new byte[] { (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0 }, jpeg[payload..(payload + 5)]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(17, 9)]
    [InlineData(16, 16)]
    [InlineData(33, 47)]
    [InlineData(64, 1)]
    public void Sof0_ReportsInputDimensions(int width, int height)
    {
        var encoder = new BaselineJpegEncoder();

        byte[] jpeg = encoder.Encode(Gradient(width, height), width, height, 90);

        // Payload SOF0: precisione (1), altezza (2), larghezza (2), numero di componenti (1).
        int payload = PayloadOf(jpeg, Sof0);
        Assert.Equal(8, jpeg[payload]);
        Assert.Equal(height, (jpeg[payload + 1] << 8) | jpeg[payload + 2]);
        Assert.Equal(width, (jpeg[payload + 3] << 8) | jpeg[payload + 4]);
        Assert.Equal(3, jpeg[payload + 5]);
    }

    [Theory]
    [InlineData(90, 0x22)]
    [InlineData(94, 0x22)]
    [InlineData(95, 0x11)]
    [InlineData(100, 0x11)]
    public void Sof0_UsesSubsampling420BelowQuality95(int quality, int expectedLumaSampling)
    {
        var encoder = new BaselineJpegEncoder();

        byte[] jpeg = encoder.Encode(Gradient(32, 32), 32, 32, quality);

        // Componenti a partire dal byte 6 del payload: id, campionamento H/V, tabella Q.
        int payload = PayloadOf(jpeg, Sof0);
        Assert.Equal(expectedLumaSampling, jpeg[payload + 7]);
        Assert.Equal(0x11, jpeg[payload + 10]);
        Assert.Equal(0x11, jpeg[payload + 13]);
    }

    [Fact]
    public void Encode_IsDeterministic()
    {
        var encoder = new BaselineJpegEncoder();
        byte[] pixels = Noise(37, 21, 7);

        byte[] first = encoder.Encode(pixels, 37, 21, 85);
        byte[] second = encoder.Encode(pixels, 37, 21, 85);

        Assert.Equal(first, second);
    }

    [Fact]
    public void LowerQuality_ProducesSmallerFile()
    {
        var encoder = new BaselineJpegEncoder();
        byte[] pixels = Noise(64, 48, 3);

        int q10 = encoder.Encode(pixels, 64, 48, 10).Length;
        int q50 = encoder.Encode(pixels, 64, 48, 50).Length;
        int q90 = encoder.Encode(pixels, 64, 48, 90).Length;

        Assert.True(q10 < q50, $"q10={q10} deve essere minore di q50={q50}");
        Assert.True(q50 < q90, $"q50={q50} deve essere minore di q90={q90}");
    }

    [Fact]
    public void Quality50_UsesAnnexKLuminanceTable()
    {
        var encoder = new BaselineJpegEncoder();

        byte[] jpeg = encoder.Encode(Gradient(8, 8), 8, 8, 50);

        // DQT tabella 0: primo byte = Pq/Tq (0 = 8 bit, tabella 0), poi 64 valori in ordine zigzag.
        // Annex K.1 in zigzag: 16, 11, 12, 14, 12, 10, ... ; ultimo valore (posizione 63) = 99.
        int payload = PayloadOf(jpeg, Dqt);
        Assert.Equal(0x00, jpeg[payload]);
        Assert.Equal(new byte[] { 16, 11, 12, 14, 12, 10 }, jpeg[(payload + 1)..(payload + 7)]);
        Assert.Equal(99, jpeg[payload + 64]);
    }

    [Fact]
    public void Quality100_UsesUnitQuantizationTables()
    {
        var encoder = new BaselineJpegEncoder();

        byte[] jpeg = encoder.Encode(Gradient(16, 16), 16, 16, 100);

        int payload = PayloadOf(jpeg, Dqt);
        for (int i = 1; i <= 64; i++)
        {
            Assert.Equal(1, jpeg[payload + i]);
        }
    }

    [Fact]
    public void EntropyData_StuffsEveryFfByte()
    {
        var encoder = new BaselineJpegEncoder();

        // Rumore a qualità alta: produce molti byte 0xFF nei dati entropici.
        byte[] jpeg = encoder.Encode(Noise(48, 48, 11), 48, 48, 100);

        // Ogni 0xFF nei dati entropici deve essere seguito da 0x00; l'ultimo FF D9 (EOI) è escluso.
        int ffCount = 0;
        for (int i = FindScanDataStart(jpeg); i < jpeg.Length - 2; i++)
        {
            if (jpeg[i] == 0xFF)
            {
                ffCount++;
                Assert.Equal(0x00, jpeg[i + 1]);
                i++;
            }
        }

        Assert.True(ffCount > 0, "il rumore a q100 deve produrre byte 0xFF nei dati entropici");
    }

    /// <summary>Primo byte dei dati entropici: subito dopo il segmento SOS.</summary>
    private static int FindScanDataStart(byte[] jpeg)
    {
        int payload = PayloadOf(jpeg, Sos);
        int length = (jpeg[payload - 2] << 8) | jpeg[payload - 1];
        return payload - 2 + length;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Encode_RejectsQualityOutOfRange(int quality)
    {
        var encoder = new BaselineJpegEncoder();

        Assert.Throws<ArgumentOutOfRangeException>(() => { encoder.Encode(Gradient(4, 4), 4, 4, quality); });
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-3, 2)]
    [InlineData(65536, 1)]
    public void Encode_RejectsDimensionsOutOfRange(int width, int height)
    {
        var encoder = new BaselineJpegEncoder();

        Assert.Throws<ArgumentOutOfRangeException>(() => { encoder.Encode(new byte[3], width, height, 90); });
    }

    [Fact]
    public void Encode_RejectsBufferWithWrongLength()
    {
        var encoder = new BaselineJpegEncoder();

        Assert.Throws<ArgumentException>(() => { encoder.Encode(new byte[47], 4, 4, 90); });
    }
}
