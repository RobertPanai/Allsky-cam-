using Xunit;

namespace AllSky.Imaging.Tests;

public class HotPixelFilterTests
{
    private const int W = 16;
    private const int H = 16;

    private static ushort[] Background(ushort value)
    {
        var frame = new ushort[W * H];
        Array.Fill(frame, value);
        return frame;
    }

    /// <summary>Fondo con piccolo rumore deterministico (valori 998..1002).</summary>
    private static ushort[] NoisyBackground()
    {
        var frame = new ushort[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                frame[y * W + x] = (ushort)(1000 + ((x * 7 + y * 13) % 5) - 2);
            }
        }

        return frame;
    }

    [Fact]
    public void IsolatedHotPixel_IsDetectedAndReplacedByNeighbourMedian()
    {
        ushort[] frame = Background(1000);
        int hot = 8 * W + 8;
        frame[hot] = 60000;

        HotPixelMap map = HotPixelFilter.Build(frame, W, H, 5.0);

        Assert.Equal(new[] { hot }, map.Indices);

        HotPixelFilter.Apply(frame, W, H, map);

        Assert.Equal(1000, frame[hot]);
        for (int i = 0; i < frame.Length; i++)
        {
            Assert.Equal(1000, frame[i]);
        }
    }

    [Fact]
    public void IsolatedHotPixel_WithNoise_IsDetected()
    {
        ushort[] frame = NoisyBackground();
        int hot = 9 * W + 6;
        frame[hot] = 60000;
        ushort[] before = (ushort[])frame.Clone();

        HotPixelMap map = HotPixelFilter.Build(frame, W, H, 5.0);

        Assert.Equal(new[] { hot }, map.Indices);

        HotPixelFilter.Apply(frame, W, H, map);

        // Il valore di sostituzione e' la mediana dei vicini dello stesso colore, dentro il fondo rumoroso.
        Assert.InRange(frame[hot], 998, 1002);
        for (int i = 0; i < frame.Length; i++)
        {
            if (i != hot)
            {
                Assert.Equal(before[i], frame[i]);
            }
        }
    }

    [Fact]
    public void Star3x3_IsNotCorrected()
    {
        // Stella 3x3 di 30000 su fondo 1000. Il centro ha i vicini a passo 2 tutti scuri,
        // ma i vicini a distanza 1 sono luminosi: non e' un pixel caldo.
        ushort[] frame = Background(1000);
        for (int y = 7; y <= 9; y++)
        {
            for (int x = 7; x <= 9; x++)
            {
                frame[y * W + x] = 30000;
            }
        }

        ushort[] before = (ushort[])frame.Clone();

        HotPixelMap map = HotPixelFilter.Build(frame, W, H, 5.0);

        Assert.Empty(map.Indices);

        HotPixelFilter.Apply(frame, W, H, map);
        Assert.Equal(before, frame);
    }

    [Fact]
    public void UniformFrame_HasNoHotPixels()
    {
        ushort[] frame = Background(1000);

        HotPixelMap map = HotPixelFilter.Build(frame, W, H, 3.0);

        Assert.Empty(map.Indices);
    }

    [Fact]
    public void MultiFrame_MedianRemovesTransientEvent_KeepsPersistentHotPixel()
    {
        // Pixel caldo persistente (8,8) in tutti i frame; evento transitorio (10,10) solo nel primo.
        int persistent = 8 * W + 8;
        int transient = 10 * W + 10;
        var frames = new List<ushort[]>();
        for (int k = 0; k < 5; k++)
        {
            ushort[] f = Background(1000);
            f[persistent] = 60000;
            frames.Add(f);
        }

        frames[0][transient] = 60000;

        HotPixelMap map = HotPixelFilter.Build(frames, W, H, 5.0);

        Assert.Equal(new[] { persistent }, map.Indices);
    }

    [Fact]
    public void BorderPixels_AreNotCorrected()
    {
        // Il margine di 2 pixel non viene analizzato.
        ushort[] frame = Background(1000);
        frame[0] = 60000;

        HotPixelMap map = HotPixelFilter.Build(frame, W, H, 5.0);

        Assert.Empty(map.Indices);
    }

    [Fact]
    public void NonPositiveSigma_Throws()
    {
        ushort[] frame = Background(1000);

        Assert.Throws<ArgumentOutOfRangeException>(() => HotPixelFilter.Build(frame, W, H, 0.0));
    }

    [Fact]
    public void Apply_MapWithDifferentSize_Throws()
    {
        ushort[] frame = Background(1000);
        var map = new HotPixelMap(8, 8, new[] { 0 });

        Assert.Throws<ArgumentException>(() => HotPixelFilter.Apply(frame, W, H, map));
    }
}
