using Xunit;

namespace AllSky.Timelapse.Tests;

public class JpegSizeTests
{
    [Fact]
    public void ReadsDimensionsFromSofMarker()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "f.jpg");
        File.WriteAllBytes(path, TestFiles.Jpeg64x48);

        bool ok = JpegSize.TryRead(path, out int width, out int height);

        Assert.True(ok);
        Assert.Equal(64, width);
        Assert.Equal(48, height);
    }

    [Fact]
    public void NotAJpeg_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "f.jpg");
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        bool ok = JpegSize.TryRead(path, out int width, out int height);

        Assert.False(ok);
        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    [Fact]
    public void TruncatedJpeg_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "f.jpg");
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 });

        Assert.False(JpegSize.TryRead(path, out _, out _));
    }
}
