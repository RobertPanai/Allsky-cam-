using Xunit;

namespace AllSky.Imaging.Tests;

public class DarkBuilderTests
{
    private static ushort[] Filled(int width, int height, ushort value)
    {
        var frame = new ushort[width * height];
        Array.Fill(frame, value);
        return frame;
    }

    [Fact]
    public void Combine_OddCount_MedianRemovesOutlierFrame()
    {
        // 5 frame uguali (100 ovunque) tranne uno con un raggio cosmico (60000) nel pixel 3.
        // La mediana su 5 valori ignora l'unico outlier.
        const int w = 4;
        const int h = 2;
        var frames = new List<ushort[]>();
        for (int k = 0; k < 5; k++)
        {
            frames.Add(Filled(w, h, 100));
        }

        frames[0][3] = 60000;

        MasterDark master = DarkBuilder.Combine(frames, w, h, 300.0, 120.0, 10.0);

        Assert.Equal(100, master.Data[3]);
        for (int i = 0; i < master.Data.Length; i++)
        {
            Assert.Equal(100, master.Data[i]);
        }
    }

    [Fact]
    public void Combine_EvenCount_MedianIsFlooredMeanOfMiddleValues()
    {
        // Pixel 0 nei 4 frame: 10, 20, 30, 1000. Centrali 20 e 30, media 25.
        // Pixel 1: 7, 7, 8, 8 -> (7 + 8) / 2 = 7 per difetto.
        var frames = new List<ushort[]>
        {
            new ushort[] { 10, 7 },
            new ushort[] { 20, 7 },
            new ushort[] { 30, 8 },
            new ushort[] { 1000, 8 },
        };

        MasterDark master = DarkBuilder.Combine(frames, 2, 1, 300.0, 120.0);

        Assert.Equal(25, master.Data[0]);
        Assert.Equal(7, master.Data[1]);
    }

    [Fact]
    public void Combine_SetsMetadataAndDimensions()
    {
        var frames = new List<ushort[]> { Filled(3, 2, 500) };

        MasterDark master = DarkBuilder.Combine(frames, 3, 2, 600.0, 80.0, 15.5);

        Assert.Equal(3, master.Width);
        Assert.Equal(2, master.Height);
        Assert.Equal(600.0, master.ExposureMs);
        Assert.Equal(80.0, master.Gain);
        Assert.Equal(15.5, master.TemperatureC!.Value);
        Assert.Equal(500, master.Data[5]);
    }

    [Fact]
    public void Combine_NullTemperature_IsAccepted()
    {
        var frames = new List<ushort[]> { Filled(2, 2, 1) };

        MasterDark master = DarkBuilder.Combine(frames, 2, 2, 100.0, 0.0);

        Assert.Null(master.TemperatureC);
    }

    [Fact]
    public void Combine_EmptyFrameList_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DarkBuilder.Combine(new List<ushort[]>(), 2, 2, 100.0, 0.0));
    }

    [Fact]
    public void Combine_FrameWithWrongLength_Throws()
    {
        var frames = new List<ushort[]> { Filled(2, 2, 1), Filled(3, 2, 1) };

        Assert.Throws<ArgumentException>(() =>
            DarkBuilder.Combine(frames, 2, 2, 100.0, 0.0));
    }

    [Fact]
    public void Combine_NonPositiveExposure_Throws()
    {
        var frames = new List<ushort[]> { Filled(2, 2, 1) };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DarkBuilder.Combine(frames, 2, 2, 0.0, 0.0));
    }
}
