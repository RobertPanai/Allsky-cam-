using Xunit;

namespace AllSky.Cameras.Alpaca.Tests;

/// <summary>Test del lettore ImageBytes e del ripiego JSON, su buffer costruiti a mano secondo la spec.</summary>
public sealed class ImageBytesReaderTests
{
    private const int W = 8;
    private const int H = 6;

    private static async Task<ushort[]> ReadAsync(byte[] bytes, int width = W, int height = H)
    {
        var pixels = new ushort[width * height];
        await ImageBytesReader.ReadAsync(new MemoryStream(bytes), width, height, pixels, CancellationToken.None);
        return pixels;
    }

    [Fact]
    public async Task ReadAsync_Int32OutOfRange_IsClamped()
    {
        byte[] bytes = ImageBytesFixture.Build(W, H, 2, (x, y) => x == 0 ? -5 : x == 1 ? 70000 : 1234);

        ushort[] pixels = await ReadAsync(bytes);

        Assert.Equal(0, pixels[0 * W + 0]);
        Assert.Equal(65535, pixels[0 * W + 1]);
        Assert.Equal(1234, pixels[0 * W + 2]);
    }

    [Fact]
    public async Task ReadAsync_Int16Negative_IsClampedToZero()
    {
        byte[] bytes = ImageBytesFixture.Build(W, H, 1, (x, y) => x == 0 ? -1 : 32767);

        ushort[] pixels = await ReadAsync(bytes);

        Assert.Equal(0, pixels[0]);
        Assert.Equal(32767, pixels[1]);
    }

    [Fact]
    public async Task ReadAsync_UInt16MaxValue_IsKept()
    {
        byte[] bytes = ImageBytesFixture.Build(W, H, 8, (x, y) => 65535);

        ushort[] pixels = await ReadAsync(bytes);

        Assert.All(pixels, p => Assert.Equal(65535, p));
    }

    [Fact]
    public async Task ReadAsync_ByteElements_AreReadAsUnsigned()
    {
        byte[] bytes = ImageBytesFixture.Build(W, H, 6, (x, y) => 255);

        ushort[] pixels = await ReadAsync(bytes);

        Assert.All(pixels, p => Assert.Equal(255, p));
    }

    [Fact]
    public async Task ReadAsync_HonoursDataStartPadding()
    {
        // DataStart = 48: quattro byte di riempimento tra header e dati.
        byte[] bytes = ImageBytesFixture.Build(W, H, 2, (x, y) => (x * 100) + y + 1, dataStart: 48);

        ushort[] pixels = await ReadAsync(bytes);

        Assert.Equal((ushort)(3 * 100 + 2 + 1), pixels[(2 * W) + 3]);
    }

    [Fact]
    public async Task ReadAsync_ErrorNumber_ThrowsAlpacaExceptionWithUtf8Text()
    {
        byte[] bytes = ImageBytesFixture.BuildError(1234, "Pixel ° non valido");

        AlpacaException ex = await Assert.ThrowsAsync<AlpacaException>(() => ReadAsync(bytes));

        Assert.Equal(1234, ex.ErrorNumber);
        Assert.Equal("Errore Alpaca 1234: Pixel ° non valido", ex.Message);
    }

    [Fact]
    public async Task ReadAsync_DimensionMismatch_Throws()
    {
        byte[] bytes = ImageBytesFixture.Build(4, 6, 2, (x, y) => 1);

        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(bytes, W, H));
    }

    [Fact]
    public async Task ReadAsync_UnknownMetadataVersion_Throws()
    {
        byte[] bytes = ImageBytesFixture.Build(W, H, 2, (x, y) => 1, metadataVersion: 2);

        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAsync(bytes));
    }

    [Fact]
    public async Task ReadAsync_UnsupportedElementType_Throws()
    {
        byte[] bytes = ImageBytesFixture.Build(W, H, 2, (x, y) => 1);
        bytes[24] = 9; // TransmissionElementType = UInt32, non supportato

        await Assert.ThrowsAsync<NotSupportedException>(() => ReadAsync(bytes));
    }

    [Fact]
    public async Task ReadAsync_ThreeDimensionalRank_Throws()
    {
        byte[] bytes = ImageBytesFixture.Build(W, H, 2, (x, y) => 1);
        bytes[28] = 3; // Rank = 3

        await Assert.ThrowsAsync<NotSupportedException>(() => ReadAsync(bytes));
    }

    [Fact]
    public void ReadJsonColumns_WrongColumnCount_Throws()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("{\"Value\":[[1,2],[3,4]]}");
        var pixels = new ushort[3 * 2];

        Assert.Throws<InvalidDataException>(() => ImageBytesReader.ReadJsonColumns(doc.RootElement, 3, 2, pixels));
    }
}
