using AllSky.Products;
using Xunit;

namespace AllSky.Products.Tests;

public class StartrailsBuilderTests
{
    [Fact]
    public void Lighten_TakesMaxPerChannelPerPixel()
    {
        var builder = new StartrailsBuilder(2, 1);
        builder.AddFrame(new byte[] { 10, 200, 30, 0, 0, 0 });
        builder.AddFrame(new byte[] { 50, 100, 60, 5, 5, 5 });

        Assert.Equal(new byte[] { 50, 200, 60, 5, 5, 5 }, builder.Build());
        Assert.Equal(2, builder.FramesAdded);
    }

    [Fact]
    public void DefaultDecay_IsOne_AndNothingFades()
    {
        var builder = new StartrailsBuilder(1, 1);
        builder.AddFrame(new byte[] { 200, 200, 200 });
        builder.AddFrame(new byte[] { 0, 0, 0 });
        builder.AddFrame(new byte[] { 0, 0, 0 });

        Assert.Equal(1.0, builder.Decay);
        Assert.Equal(new byte[] { 200, 200, 200 }, builder.Build());
    }

    [Fact]
    public void LargeRandomFrames_MatchNaiveMax_IncludingVectorTail()
    {
        // 37 x 23 x 3 = 2553 byte: non multiplo della larghezza di Vector<byte>, quindi testa anche la coda scalare.
        const int w = 37;
        const int h = 23;
        var rng = new Random(1234);
        var builder = new StartrailsBuilder(w, h);
        byte[] expected = new byte[w * h * 3];

        for (int f = 0; f < 5; f++)
        {
            byte[] frame = new byte[w * h * 3];
            rng.NextBytes(frame);
            builder.AddFrame(frame);
            for (int i = 0; i < expected.Length; i++)
            {
                expected[i] = Math.Max(expected[i], frame[i]);
            }
        }

        Assert.Equal(expected, builder.Build());
    }

    [Fact]
    public void Decay_HalvesTrailsEachFrame()
    {
        var builder = new StartrailsBuilder(1, 1, 0.5);
        builder.AddFrame(new byte[] { 200, 200, 200 });
        Assert.Equal((byte)200, builder.Build()[0]);

        builder.AddFrame(new byte[] { 0, 0, 0 });
        Assert.Equal((byte)100, builder.Build()[0]);

        builder.AddFrame(new byte[] { 0, 0, 0 });
        Assert.Equal((byte)50, builder.Build()[0]);

        builder.AddFrame(new byte[] { 0, 0, 0 });
        Assert.Equal((byte)25, builder.Build()[0]);
    }

    [Fact]
    public void Decay_NewBrighterPixel_StillWins()
    {
        var builder = new StartrailsBuilder(1, 1, 0.5);
        builder.AddFrame(new byte[] { 100, 100, 100 });
        builder.AddFrame(new byte[] { 150, 150, 150 });

        Assert.Equal(new byte[] { 150, 150, 150 }, builder.Build());
    }

    [Fact]
    public void Decay_NearOne_StillStrictlyFadesFullBrightness()
    {
        // floor(255 * 0.999) = 254: un valore positivo cala sempre di almeno 1 per frame.
        var builder = new StartrailsBuilder(1, 1, 0.999);
        builder.AddFrame(new byte[] { 255, 255, 255 });
        builder.AddFrame(new byte[] { 0, 0, 0 });

        Assert.Equal((byte)254, builder.Build()[0]);
    }

    [Fact]
    public void SkippedFrame_IsIgnored()
    {
        var builder = new StartrailsBuilder(1, 1);
        builder.AddFrame(new byte[] { 200, 200, 200 });
        builder.AddFrame(new byte[] { 255, 255, 255 }, skip: true);

        Assert.Equal(new byte[] { 200, 200, 200 }, builder.Build());
        Assert.Equal(1, builder.FramesAdded);
    }

    [Fact]
    public void Build_ReturnsCopy()
    {
        var builder = new StartrailsBuilder(1, 1);
        builder.AddFrame(new byte[] { 9, 8, 7 });

        byte[] first = builder.Build();
        first[0] = 0;

        Assert.Equal((byte)9, builder.Build()[0]);
    }

    [Fact]
    public void WrongFrameLength_Throws()
    {
        var builder = new StartrailsBuilder(2, 2);

        Assert.Throws<ArgumentException>(() => { builder.AddFrame(new byte[11]); });
        Assert.Throws<ArgumentException>(() => { builder.AddFrame(new byte[13], skip: true); });
    }

    [Fact]
    public void InvalidSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new StartrailsBuilder(0, 1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new StartrailsBuilder(1, 0); });
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void InvalidDecay_Throws(double decay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new StartrailsBuilder(1, 1, decay); });
    }
}
