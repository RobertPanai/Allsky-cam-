using System.Reflection;
using AllSky.Products;
using Xunit;

namespace AllSky.Products.Tests;

public class KeogramBuilderTests
{
    private static readonly DateTimeOffset Ts = new(2026, 10, 18, 22, 0, 0, TimeSpan.Zero);

    /// <summary>Frame di sfondo costante con la sola colonna centrale colorata per riga e per indice di frame.</summary>
    private static byte[] MakeFrame(int w, int h, int centerX, int frameIndex)
    {
        var rgb = new byte[w * h * 3];
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = 7;
        }

        for (int y = 0; y < h; y++)
        {
            int p = (y * w + centerX) * 3;
            rgb[p] = (byte)(frameIndex * 10 + y);
            rgb[p + 1] = (byte)(100 + frameIndex);
            rgb[p + 2] = (byte)(200 - y);
        }

        return rgb;
    }

    [Fact]
    public void CenterColumn_BecomesSuccessiveColumns_Exactly()
    {
        const int w = 5;
        const int h = 4;
        const int n = 4;
        var builder = new KeogramBuilder(w, h);

        for (int i = 0; i < n; i++)
        {
            builder.AddFrame(MakeFrame(w, h, centerX: 2, frameIndex: i), Ts.AddSeconds(i));
        }

        byte[] img = builder.Build();

        Assert.Equal(n * h * 3, img.Length);
        Assert.Equal(n, builder.OutputWidth);
        for (int k = 0; k < n; k++)
        {
            for (int y = 0; y < h; y++)
            {
                int p = (y * n + k) * 3;
                Assert.Equal((byte)(k * 10 + y), img[p]);
                Assert.Equal((byte)(100 + k), img[p + 1]);
                Assert.Equal((byte)(200 - y), img[p + 2]);
            }
        }
    }

    [Fact]
    public void EvenWidth_UsesColumnAtHalfWidth()
    {
        // Larghezza pari: la colonna N-S è W / 2 (indice 3 per W = 6).
        const int w = 6;
        const int h = 3;
        var builder = new KeogramBuilder(w, h);
        builder.AddFrame(MakeFrame(w, h, centerX: 3, frameIndex: 2), Ts);

        byte[] img = builder.Build();

        Assert.Equal(1, builder.OutputWidth);
        for (int y = 0; y < h; y++)
        {
            Assert.Equal((byte)(20 + y), img[y * 3]);
            Assert.Equal((byte)102, img[y * 3 + 1]);
            Assert.Equal((byte)(200 - y), img[y * 3 + 2]);
        }
    }

    [Fact]
    public void AngleNinety_SamplesHorizontalDiameter_Exactly()
    {
        // Frame 5x5: il diametro E-O passa per la riga centrale y = 2; output riga j = pixel (x = j, y = 2).
        const int w = 5;
        const int h = 5;
        const int n = 3;
        var builder = new KeogramBuilder(w, h, 0, 90.0);

        for (int i = 0; i < n; i++)
        {
            var frame = new byte[w * h * 3];
            for (int x = 0; x < w; x++)
            {
                int p = (2 * w + x) * 3;
                frame[p] = (byte)(x * 40);
                frame[p + 1] = (byte)(i * 10);
                frame[p + 2] = 200;
            }

            builder.AddFrame(frame, Ts.AddSeconds(i));
        }

        byte[] img = builder.Build();

        for (int j = 0; j < h; j++)
        {
            for (int k = 0; k < n; k++)
            {
                int p = (j * n + k) * 3;
                Assert.Equal((byte)(j * 40), img[p]);
                Assert.Equal((byte)(k * 10), img[p + 1]);
                Assert.Equal((byte)200, img[p + 2]);
            }
        }
    }

    [Fact]
    public void ZeroAngle_MatchesDefaultConstructor()
    {
        const int w = 7;
        const int h = 4;
        var defaults = new KeogramBuilder(w, h);
        var explicitZero = new KeogramBuilder(w, h, 0, 0.0);

        for (int i = 0; i < 3; i++)
        {
            byte[] frame = MakeFrame(w, h, centerX: 3, frameIndex: i);
            defaults.AddFrame(frame, Ts);
            explicitZero.AddFrame(frame, Ts);
        }

        Assert.Equal(defaults.Build(), explicitZero.Build());
    }

    [Fact]
    public void MoreThanMaxColumns_IsDownscaledToMaxColumns_KeepingConstantValues()
    {
        const int w = 3;
        const int h = 2;
        const int n = 4100;
        var builder = new KeogramBuilder(w, h, n);
        for (int i = 0; i < n; i++)
        {
            builder.AddFrame(MakeFrame(w, h, centerX: 1, frameIndex: 0), Ts);
        }

        byte[] img = builder.Build();

        Assert.Equal(KeogramBuilder.MaxColumns, builder.OutputWidth);
        Assert.Equal(KeogramBuilder.MaxColumns * h * 3, img.Length);
        for (int k = 0; k < KeogramBuilder.MaxColumns; k++)
        {
            for (int y = 0; y < h; y++)
            {
                int p = (y * KeogramBuilder.MaxColumns + k) * 3;
                Assert.Equal((byte)y, img[p]);
                Assert.Equal((byte)100, img[p + 1]);
                Assert.Equal((byte)(200 - y), img[p + 2]);
            }
        }
    }

    [Fact]
    public void Downscale_AveragesBlocksOfColumns()
    {
        // 8192 frame in 4096 colonne: ogni colonna è la media di due frame consecutivi.
        // Valori rossi alternati 0 e 100: media (0 + 100 + 1) / 2 = 50 con arrotondamento.
        const int w = 3;
        const int h = 2;
        const int n = 2 * KeogramBuilder.MaxColumns;
        var builder = new KeogramBuilder(w, h, n);
        for (int i = 0; i < n; i++)
        {
            var frame = new byte[w * h * 3];
            int p = (0 * w + 1) * 3;
            frame[p] = (byte)(i % 2 == 0 ? 0 : 100);
            frame[p + 1] = 0;
            frame[p + 2] = 0;
            builder.AddFrame(frame, Ts);
        }

        byte[] img = builder.Build();

        Assert.Equal(KeogramBuilder.MaxColumns, builder.OutputWidth);
        for (int k = 0; k < KeogramBuilder.MaxColumns; k++)
        {
            Assert.Equal((byte)50, img[(0 * KeogramBuilder.MaxColumns + k) * 3]);
        }
    }

    [Fact]
    public void NoFrames_BuildsEmptyImage()
    {
        var builder = new KeogramBuilder(4, 3);

        Assert.Equal(0, builder.OutputWidth);
        Assert.Empty(builder.Build());
    }

    [Fact]
    public void Timestamps_FollowColumnOrder()
    {
        var builder = new KeogramBuilder(2, 2);
        builder.AddFrame(new byte[12], Ts);
        builder.AddFrame(new byte[12], Ts.AddMinutes(1));

        Assert.Equal(2, builder.FrameCount);
        Assert.Equal(Ts, builder.Timestamps[0]);
        Assert.Equal(Ts.AddMinutes(1), builder.Timestamps[1]);
    }

    [Fact]
    public void Build_ReturnsCopy_NotAffectedByLaterInputChanges()
    {
        const int w = 4;
        const int h = 3;
        var builder = new KeogramBuilder(w, h);
        byte[] frame = MakeFrame(w, h, centerX: 2, frameIndex: 1);
        builder.AddFrame(frame, Ts);

        byte[] expected = builder.Build();
        Array.Clear(frame);

        Assert.Equal(expected, builder.Build());
        Assert.Equal((byte)10, builder.Build()[0]);
    }

    [Fact]
    public void WrongFrameLength_Throws()
    {
        var builder = new KeogramBuilder(4, 3);

        Assert.Throws<ArgumentException>(() => { builder.AddFrame(new byte[35], Ts); });
        Assert.Throws<ArgumentException>(() => { builder.AddFrame(new byte[37], Ts); });
        Assert.Equal(0, builder.FrameCount);
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new KeogramBuilder(0, 4); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new KeogramBuilder(4, 0); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new KeogramBuilder(4, 4, -1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new KeogramBuilder(4, 4, 0, double.NaN); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new KeogramBuilder(4, 4, 0, double.PositiveInfinity); });
    }

    [Fact]
    public void AfterManyFrames_BuilderKeepsOnlyColumnsNotFrames()
    {
        // Controllo sui campi: la somma dei buffer byte[] deve restare dell'ordine delle sole colonne
        // (3 * H byte per frame, con al più il raddoppio del buffer), non dei frame completi (3 * W * H per frame).
        const int w = 64;
        const int h = 32;
        const int n = 2000;
        var builder = new KeogramBuilder(w, h);
        byte[] frame = new byte[w * h * 3];
        for (int i = 0; i < n; i++)
        {
            frame[(i % (w * h)) * 3] = (byte)i;
            builder.AddFrame(frame, Ts);
        }

        long byteFieldTotal = 0;
        var fields = typeof(KeogramBuilder).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (FieldInfo field in fields)
        {
            if (field.FieldType == typeof(byte[]))
            {
                byte[]? array = (byte[]?)field.GetValue(builder);
                byteFieldTotal += array?.Length ?? 0;
            }
        }

        long columnsOnly = 3L * h * n;
        long wholeFrames = 3L * w * h * n;
        Assert.Equal(n, builder.FrameCount);
        Assert.True(byteFieldTotal >= columnsOnly, "Il buffer deve contenere almeno le colonne.");
        Assert.True(byteFieldTotal <= 2 * columnsOnly, $"Buffer byte[] troppo grande: {byteFieldTotal}.");
        Assert.True(byteFieldTotal < wholeFrames / 8, "Il keogram non deve conservare i frame completi.");
    }
}
