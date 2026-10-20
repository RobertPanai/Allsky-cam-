using AllSky.Imaging.Overlay;
using Xunit;

namespace AllSky.Imaging.Overlay.Tests;

public class OverlayComposerTests
{
    private const int W = 320;
    private const int H = 120;
    private const int Size = W * H * 3;
    private const byte Background = 40;
    private static readonly DateTimeOffset Time = new(2026, 10, 19, 21, 5, 3, TimeSpan.Zero);

    /// <summary>Verifica che tutti i pixel cambiati stiano in una delle zone attese; restituisce quanti sono cambiati.</summary>
    private static int ChangedPixelsOutside(byte[] before, ReadOnlySpan<byte> after, Func<int, int, bool> allowed)
    {
        int outside = 0;
        for (int p = 0; p < W * H; p++)
        {
            int i = p * 3;
            bool changed = after[i] != before[i] || after[i + 1] != before[i + 1] || after[i + 2] != before[i + 2];
            if (changed && !allowed(p % W, p / W))
            {
                outside++;
            }
        }

        return outside;
    }

    private static int ChangedCount(byte[] before, ReadOnlySpan<byte> after)
    {
        int count = 0;
        for (int p = 0; p < W * H; p++)
        {
            int i = p * 3;
            if (after[i] != before[i] || after[i + 1] != before[i + 1] || after[i + 2] != before[i + 2])
            {
                count++;
            }
        }

        return count;
    }

    private static bool InTopLeft(int x, int y) => x < 100 && y < 30;
    private static bool InBottomLeft(int x, int y) => x < 170 && y >= 90;
    private static bool InBottomRight(int x, int y) => x >= 150 && y >= 60;

    [Fact]
    public void Compose_ChangesOnlyTheExpectedCorners()
    {
        byte[] backing = CanaryBuffer.Allocate(Size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, Size);
        rgb.Fill(Background);
        byte[] before = rgb.ToArray();

        var info = new OverlayInfo(Time, 12.3, "Test", 8000, 250, null);
        OverlayComposer.Compose(rgb, W, H, info, new OverlayOptions(true, true, "en", 1));

        CanaryBuffer.AssertIntact(backing, Size);
        Assert.True(ChangedCount(before, rgb) > 0);
        Assert.Equal(0, ChangedPixelsOutside(before, rgb, (x, y) => InTopLeft(x, y) || InBottomLeft(x, y) || InBottomRight(x, y)));
    }

    [Fact]
    public void Compose_TimeOnly_TouchesOnlyBottomLeft()
    {
        byte[] backing = CanaryBuffer.Allocate(Size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, Size);
        rgb.Fill(Background);
        byte[] before = rgb.ToArray();

        var info = new OverlayInfo(Time, null, null, null, null, null);
        OverlayComposer.Compose(rgb, W, H, info, new OverlayOptions(true, true, "it", 1));

        CanaryBuffer.AssertIntact(backing, Size);
        Assert.True(ChangedCount(before, rgb) > 0);
        Assert.Equal(0, ChangedPixelsOutside(before, rgb, InBottomLeft));
    }

    [Fact]
    public void Compose_NothingToDraw_LeavesBufferUntouched()
    {
        byte[] backing = CanaryBuffer.Allocate(Size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, Size);
        rgb.Fill(Background);
        byte[] before = rgb.ToArray();

        var info = new OverlayInfo(Time, null, null, null, null, null);
        OverlayComposer.Compose(rgb, W, H, info, new OverlayOptions(false, false, "en", 2));

        CanaryBuffer.AssertIntact(backing, Size);
        Assert.Equal(0, ChangedCount(before, rgb));
    }

    [Fact]
    public void Compose_TemperatureSwitch_ControlsTheBottomRightOnly()
    {
        // Senza esposizione ne' etichetta: resta solo la temperatura, in basso a destra.
        var info = new OverlayInfo(Time, 12.3, null, null, null, null);

        byte[] offBacking = CanaryBuffer.Allocate(Size);
        Span<byte> off = CanaryBuffer.Payload(offBacking, Size);
        off.Fill(Background);
        byte[] before = off.ToArray();
        OverlayComposer.Compose(off, W, H, info, new OverlayOptions(false, false, "en", 1));
        Assert.Equal(0, ChangedCount(before, off));

        byte[] onBacking = CanaryBuffer.Allocate(Size);
        Span<byte> on = CanaryBuffer.Payload(onBacking, Size);
        on.Fill(Background);
        OverlayComposer.Compose(on, W, H, info, new OverlayOptions(false, true, "en", 1));
        CanaryBuffer.AssertIntact(onBacking, Size);
        Assert.True(ChangedCount(before, on) > 0);
        Assert.Equal(0, ChangedPixelsOutside(before, on, InBottomRight));
    }

    [Fact]
    public void Compose_TinyBuffer_DoesNotOverflow()
    {
        const int w = 12, h = 6, size = w * h * 3;
        byte[] backing = CanaryBuffer.Allocate(size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, size);

        var info = new OverlayInfo(DateTimeOffset.UnixEpoch, -5.0, "Lunga etichetta", 30000, 500, "notte");
        OverlayComposer.Compose(rgb, w, h, info, new OverlayOptions(true, true, "it", 4));

        CanaryBuffer.AssertIntact(backing, size);
    }

    [Fact]
    public void Compose_Scale2_StaysInsideTheBuffer()
    {
        byte[] backing = CanaryBuffer.Allocate(Size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, Size);
        rgb.Fill(Background);

        var info = new OverlayInfo(Time, 12.3, "Osservatorio", 8000, 250, "giorno");
        OverlayComposer.Compose(rgb, W, H, info, new OverlayOptions(true, true, "it", 2));

        CanaryBuffer.AssertIntact(backing, Size);
    }

    [Fact]
    public void Compose_InvalidLanguage_ThrowsBeforeDrawing()
    {
        byte[] backing = CanaryBuffer.Allocate(Size);
        Span<byte> rgb = CanaryBuffer.Payload(backing, Size);
        rgb.Fill(Background);
        byte[] before = rgb.ToArray();

        var info = new OverlayInfo(Time, 12.3, "Test", 8000, 250, null);
        Assert.Throws<ArgumentException>(() => OverlayComposer.Compose(new byte[Size], W, H, info, new OverlayOptions(true, true, "fr", 1)));
        Assert.Equal(0, ChangedCount(before, rgb));
    }

    [Fact]
    public void FormatLocalTime_UsesDateAndTimeWithoutConversion()
    {
        var local = new DateTimeOffset(2026, 10, 19, 21, 5, 3, TimeSpan.FromHours(2));
        Assert.Equal("2026-10-19 21:05:03", OverlayComposer.FormatLocalTime(local));
    }

    [Fact]
    public void FormatTemperature_OneDecimalAndDegreeSign()
    {
        Assert.Equal("12.3 °C", OverlayComposer.FormatTemperature(12.3));
        Assert.Equal("-5.0 °C", OverlayComposer.FormatTemperature(-5.0));
    }

    [Fact]
    public void FormatExposureLine_ItalianAndEnglishLabels()
    {
        Assert.Equal("exp 8.0 s | gain 250", OverlayComposer.FormatExposureLine(8000, 250, "en"));
        Assert.Equal("esp 8.0 s | gain 250", OverlayComposer.FormatExposureLine(8000, 250, "it"));
    }

    [Fact]
    public void FormatExposureLine_MillisecondsBelowOneSecond_AndPartialValues()
    {
        Assert.Equal("exp 2 ms", OverlayComposer.FormatExposureLine(2, null, "en"));
        Assert.Equal("gain 250.5", OverlayComposer.FormatExposureLine(null, 250.5, "it"));
        Assert.Null(OverlayComposer.FormatExposureLine(null, null, "en"));
    }

    [Fact]
    public void NormalizeLanguage_IsCaseInsensitiveAndRejectsOthers()
    {
        Assert.Equal("it", OverlayComposer.NormalizeLanguage("IT"));
        Assert.Equal("en", OverlayComposer.NormalizeLanguage("En"));
        Assert.Throws<ArgumentException>(() => OverlayComposer.NormalizeLanguage("de"));
    }
}
