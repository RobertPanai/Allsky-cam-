using Xunit;

namespace AllSky.Timelapse.Tests;

public class FfmpegLocatorTests
{
    [Fact]
    public void ConfiguredExistingPath_WinsOverBundledAndPath()
    {
        using var dir = new TempDirectory();
        string configured = Path.Combine(dir.Path, "mio-ffmpeg");
        File.WriteAllText(configured, "");
        WriteFile(Path.Combine(dir.Path, "tools", "ffmpeg", FfmpegLocator.ExecutableName));
        string pathDir = Path.Combine(dir.Path, "bin");
        WriteFile(Path.Combine(pathDir, FfmpegLocator.ExecutableName));

        string? found = FfmpegLocator.Locate(configured, dir.Path, pathDir);

        Assert.Equal(Path.GetFullPath(configured), found);
    }

    [Fact]
    public void ConfiguredMissingPath_FallsBackToBundled()
    {
        using var dir = new TempDirectory();
        string bundled = Path.Combine(dir.Path, "tools", "ffmpeg", FfmpegLocator.ExecutableName);
        WriteFile(bundled);

        string? found = FfmpegLocator.Locate(Path.Combine(dir.Path, "assente"), dir.Path, pathVariable: "");

        Assert.Equal(Path.GetFullPath(bundled), found);
    }

    [Fact]
    public void BundledBeatsPath()
    {
        using var dir = new TempDirectory();
        string bundled = Path.Combine(dir.Path, "tools", "ffmpeg", FfmpegLocator.ExecutableName);
        WriteFile(bundled);
        string pathDir = Path.Combine(dir.Path, "bin");
        WriteFile(Path.Combine(pathDir, FfmpegLocator.ExecutableName));

        string? found = FfmpegLocator.Locate(null, dir.Path, pathDir);

        Assert.Equal(Path.GetFullPath(bundled), found);
    }

    [Fact]
    public void NothingConfigured_UsesPathEntry()
    {
        using var dir = new TempDirectory();
        string pathDir = Path.Combine(dir.Path, "bin");
        string exe = Path.Combine(pathDir, FfmpegLocator.ExecutableName);
        WriteFile(exe);
        string pathVariable = "/cartella/inesistente" + Path.PathSeparator + "\"" + pathDir + "\"";

        string? found = FfmpegLocator.Locate(null, dir.Path, pathVariable);

        Assert.Equal(Path.GetFullPath(exe), found);
    }

    [Fact]
    public void NothingFound_ReturnsNull()
    {
        using var dir = new TempDirectory();

        string? found = FfmpegLocator.Locate("/non/esiste/ffmpeg", dir.Path, pathVariable: "");

        Assert.Null(found);
    }

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }
}
