using System;
using System.Collections.Generic;
using AllSky.Core;
using AllSky.Imaging.Jpeg;
using AllSky.Publish;
using Xunit;

namespace AllSky.Publish.Tests;

public class WebImagePreparerTests
{
    private static readonly DateTimeOffset Captured = new(2026, 10, 21, 21, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Constants_are_300k_target_and_900k_hard_max()
    {
        Assert.Equal(300_000, WebImagePreparer.TargetBytes);
        Assert.Equal(900_000, WebImagePreparer.HardMaxBytes);
    }

    [Fact]
    public void Small_frame_is_never_enlarged_and_hash_is_fnv1a_of_rgb()
    {
        var encoder = new FakeEncoder((w, h, q) => 1_000);
        var frame = MakeFrame(800, 600);

        PreparedImage result = new WebImagePreparer(encoder).Prepare(frame, Settings(1200, 75));

        Assert.Equal(800, result.Width);
        Assert.Equal(600, result.Height);
        Assert.Equal(1, encoder.Calls.Count);
        Assert.Equal((800, 600, 75), encoder.Calls[0]);
        Assert.Equal(Fnv1a(frame.Rgb8), result.ContentHash);
    }

    [Fact]
    public void Long_edge_is_scaled_to_setting_keeping_aspect_ratio()
    {
        var encoder = new FakeEncoder((w, h, q) => 1_000);

        PreparedImage result = new WebImagePreparer(encoder).Prepare(MakeFrame(1280, 960), Settings(1200, 75));

        Assert.Equal(1200, result.Width);
        Assert.Equal(900, result.Height);
        Assert.Equal((1200, 900, 75), encoder.Calls[0]);
    }

    [Fact]
    public void Portrait_frame_long_edge_is_height()
    {
        var encoder = new FakeEncoder((w, h, q) => 1_000);

        PreparedImage result = new WebImagePreparer(encoder).Prepare(MakeFrame(960, 1280), Settings(1200, 75));

        Assert.Equal(900, result.Width);
        Assert.Equal(1200, result.Height);
    }

    [Fact]
    public void Short_edge_is_at_least_one_pixel()
    {
        var encoder = new FakeEncoder((w, h, q) => 1_000);

        PreparedImage result = new WebImagePreparer(encoder).Prepare(MakeFrame(2000, 1), Settings(200, 75));

        Assert.Equal(200, result.Width);
        Assert.Equal(1, result.Height);
    }

    [Fact]
    public void Over_target_quality_drops_by_five_to_40_then_size_steps_restart_at_initial_quality()
    {
        // Peso = pixel * qualita' / 100: a 1200x900 nemmeno la qualita' 40 entra nel target.
        var encoder = new FakeEncoder((w, h, q) => (int)((long)w * h * q / 100));

        PreparedImage result = new WebImagePreparer(encoder).Prepare(MakeFrame(1280, 960), Settings(1200, 75));

        // Livello 1 (1200x900): qualita' 75..40 a passi di 5, 8 tentativi.
        Assert.Equal((1200, 900, 75), encoder.Calls[0]);
        Assert.Equal((1200, 900, 40), encoder.Calls[7]);
        // Livello 2 (1020x765): riparte dalla qualita' iniziale.
        Assert.Equal((1020, 765, 75), encoder.Calls[8]);
        // Livello 3 (867x650): 75..50 entrano nel target a 50 (281_775 byte).
        Assert.Equal((867, 650, 75), encoder.Calls[16]);
        Assert.Equal((867, 650, 50), encoder.Calls[21]);
        Assert.Equal(22, encoder.Calls.Count);

        Assert.Equal(867, result.Width);
        Assert.Equal(650, result.Height);
        Assert.Equal(281_775, result.Jpeg.Length);
    }

    [Fact]
    public void Returns_best_attempt_when_it_is_between_target_and_hard_max()
    {
        // Peso costante 500_000: sopra il target a ogni livello, sotto il tetto assoluto.
        var encoder = new FakeEncoder((w, h, q) => 500_000);

        PreparedImage result = new WebImagePreparer(encoder).Prepare(MakeFrame(1280, 960), Settings(1200, 75));

        Assert.Equal(500_000, result.Jpeg.Length);
        Assert.Equal(1200, result.Width);
        Assert.Equal(900, result.Height);
        // Livelli 1200, 1020, 867, 737, 626, 532, 452, 384, 326, 320: 10 livelli da 8 qualita'.
        Assert.Equal(80, encoder.Calls.Count);
        Assert.Equal((320, 240, 40), encoder.Calls[79]);
    }

    [Fact]
    public void Throws_when_nothing_fits_under_hard_max()
    {
        var encoder = new FakeEncoder((w, h, q) => 1_000_000);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new WebImagePreparer(encoder).Prepare(MakeFrame(1280, 960), Settings(1200, 75)));

        Assert.Contains("900000", ex.Message);
    }

    [Fact]
    public void Rejects_rgb_buffer_of_wrong_length()
    {
        var encoder = new FakeEncoder((w, h, q) => 1_000);
        var frame = new PublishFrame(new byte[10], 4, 4, Captured, null, null, PublishMode.Night, null);

        Assert.Throws<ArgumentException>(() => new WebImagePreparer(encoder).Prepare(frame, Settings(1200, 75)));
        Assert.Empty(encoder.Calls);
    }

    [Fact]
    public void Content_hash_matches_fnv1a_reference_vector()
    {
        // FNV-1a 64 di "abc" = e71fa2190541574b (vettore di riferimento, verificato con Python).
        var encoder = new FakeEncoder((w, h, q) => 1_000);
        var frame = new PublishFrame(new byte[] { (byte)'a', (byte)'b', (byte)'c' }, 1, 1, Captured, null, null, PublishMode.Night, null);

        PreparedImage result = new WebImagePreparer(encoder).Prepare(frame, Settings(1200, 75));

        Assert.Equal(0xe71fa2190541574bUL, result.ContentHash);
    }

    [Fact]
    public void Real_encoder_on_synthetic_gradient_gives_valid_small_jpeg()
    {
        var encoder = new BaselineJpegEncoder();
        var frame = GradientFrame(1280, 960);

        PreparedImage result = new WebImagePreparer(encoder).Prepare(frame, Settings(1200, 75));

        Assert.Equal(1200, result.Width);
        Assert.Equal(900, result.Height);
        Assert.Equal(0xFF, result.Jpeg[0]);
        Assert.Equal(0xD8, result.Jpeg[1]);
        Assert.Equal(0xFF, result.Jpeg[result.Jpeg.Length - 2]);
        Assert.Equal(0xD9, result.Jpeg[result.Jpeg.Length - 1]);
        Assert.True(result.Jpeg.Length < 300_000, $"Peso {result.Jpeg.Length} byte oltre 300 KB.");
    }

    [Fact]
    public void Real_encoder_preparation_is_deterministic()
    {
        var preparer = new WebImagePreparer(new BaselineJpegEncoder());
        var frame = GradientFrame(1280, 960);

        PreparedImage first = preparer.Prepare(frame, Settings(1200, 75));
        PreparedImage second = preparer.Prepare(frame, Settings(1200, 75));

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(first.Jpeg, second.Jpeg);
    }

    private static PublishSettings Settings(int longEdgePx, int quality) =>
        new() { LongEdgePx = longEdgePx, JpegQuality = quality };

    private static PublishFrame MakeFrame(int width, int height)
    {
        byte[] rgb = new byte[width * height * 3];
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)(i * 7 + 13);
        }

        return new PublishFrame(rgb, width, height, Captured, 175.0, 0.0, PublishMode.Night, null);
    }

    private static PublishFrame GradientFrame(int width, int height)
    {
        byte[] rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3;
                rgb[i] = (byte)(x * 255 / (width - 1));
                rgb[i + 1] = (byte)(y * 255 / (height - 1));
                rgb[i + 2] = (byte)((x + y) / 2 & 0xFF);
            }
        }

        return new PublishFrame(rgb, width, height, Captured, 175.0, 0.0, PublishMode.Night, null);
    }

    /// <summary>Riferimento indipendente di FNV-1a 64 (offset 14695981039346656037, primo 1099511628211).</summary>
    private static ulong Fnv1a(byte[] data)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash;
    }

    /// <summary>Encoder finto: la dimensione del JPEG dipende da larghezza, altezza e qualita'. Registra ogni chiamata.</summary>
    private sealed class FakeEncoder : IJpegEncoder
    {
        private readonly Func<int, int, int, int> _sizeOf;

        public FakeEncoder(Func<int, int, int, int> sizeOf)
        {
            _sizeOf = sizeOf;
        }

        public List<(int Width, int Height, int Quality)> Calls { get; } = new();

        public byte[] Encode(ReadOnlySpan<byte> rgb8, int width, int height, int quality)
        {
            Calls.Add((width, height, quality));
            return new byte[_sizeOf(width, height, quality)];
        }
    }
}
