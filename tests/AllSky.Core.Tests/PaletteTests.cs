using System;
using System.Collections.Generic;
using System.Linq;
using AllSky.Core.Design;
using Xunit;

namespace AllSky.Core.Tests;

/// <summary>Verifica dei colori della palette: contrasti WCAG, modalità notte rossa, scala di luminosità e lettura esadecimale.</summary>
public sealed class PaletteTests
{
    /// <summary>Soglia AA per il testo normale (WCAG 2.x, criterio 1.4.3).</summary>
    private const double AaText = 4.5;

    /// <summary>Soglia per grafica e segni non testuali (WCAG 2.x, criterio 1.4.11).</summary>
    private const double NonTextGraphic = 3.0;

    /// <summary>Le due palette da verificare: il tema scuro e la modalità notte rossa.</summary>
    private static IEnumerable<(string Name, Palette Palette)> AllPalettes() =>
    [
        ("Dark", Palette.Dark),
        ("NightRed", Palette.NightRed),
    ];

    /// <summary>Tutte le superfici su cui può comparire il testo.</summary>
    private static IEnumerable<string> Surfaces(Palette p) =>
        [p.Background, p.Surface1, p.Surface2, p.Surface3];

    [Fact]
    public void TextAndSecondaryText_MeetAaOnEverySurface()
    {
        foreach (var (name, p) in AllPalettes())
        {
            foreach (string surface in Surfaces(p))
            {
                double text = Palette.ContrastRatio(p.Text, surface);
                double secondary = Palette.ContrastRatio(p.TextSecondary, surface);
                Assert.True(text >= AaText, $"{name}: Text su {surface} = {text:F2}:1 (minimo 4.5)");
                Assert.True(secondary >= AaText, $"{name}: TextSecondary su {surface} = {secondary:F2}:1 (minimo 4.5)");
            }
        }
    }

    [Fact]
    public void TertiaryText_MeetsAaOnEverySurface()
    {
        // Il piano chiede 4.5:1 sulle superfici dove il testo terziario compare: il tema scuro (#7A8594) e la notte rossa
        // (#EE0000) lo rispettano su tutte e quattro le superfici, Background e Surface1 comprese.
        foreach (var (name, p) in AllPalettes())
        {
            foreach (string surface in Surfaces(p))
            {
                double ratio = Palette.ContrastRatio(p.TextTertiary, surface);
                Assert.True(ratio >= AaText, $"{name}: TextTertiary su {surface} = {ratio:F2}:1 (minimo 4.5)");
            }
        }
    }

    [Fact]
    public void Accent_MeetsNonTextContrastOnBackground()
    {
        foreach (var (name, p) in AllPalettes())
        {
            double ratio = Palette.ContrastRatio(p.Accent, p.Background);
            Assert.True(ratio >= NonTextGraphic, $"{name}: Accent su Background = {ratio:F2}:1 (minimo 3)");
        }
    }

    [Fact]
    public void StatusColors_MeetAaOnBackground()
    {
        foreach (var (name, p) in AllPalettes())
        {
            foreach (string status in new[] { p.Success, p.Warning, p.Error })
            {
                double ratio = Palette.ContrastRatio(status, p.Background);
                Assert.True(ratio >= AaText, $"{name}: stato {status} su Background = {ratio:F2}:1 (minimo 4.5)");
            }
        }
    }

    [Fact]
    public void NightRed_HasNoGreenOrBlueComponent()
    {
        foreach (var (role, hex) in Palette.NightRed.Entries())
        {
            var (_, g, b) = Palette.ParseHex(hex);
            Assert.True(g == 0 && b == 0, $"NightRed.{role} = {hex} contiene verde o blu");
        }
    }

    [Fact]
    public void Scaled_OneReturnsTheSamePalette()
    {
        Assert.Equal(Palette.Dark, Palette.Dark.Scaled(1.0));
        Assert.Equal(Palette.NightRed, Palette.NightRed.Scaled(1.0));
    }

    [Fact]
    public void Scaled_HalfHalvesEveryChannel()
    {
        // Arrotondamento a 1 unità: la metà di un canale dispari non è esatta.
        Palette half = Palette.Dark.Scaled(0.5);
        foreach (var ((role, original), (_, scaled)) in Palette.Dark.Entries().Zip(half.Entries()))
        {
            var (r, g, b) = Palette.ParseHex(original);
            var (rs, gs, bs) = Palette.ParseHex(scaled);
            Assert.True(Math.Abs(rs - r / 2.0) <= 1, $"{role}: canale R {r} -> {rs}");
            Assert.True(Math.Abs(gs - g / 2.0) <= 1, $"{role}: canale G {g} -> {gs}");
            Assert.True(Math.Abs(bs - b / 2.0) <= 1, $"{role}: canale B {b} -> {bs}");
        }
    }

    [Fact]
    public void Scaled_ClampsFactorToTheSupportedRange()
    {
        Assert.Equal(Palette.Dark.Scaled(0.2), Palette.Dark.Scaled(0.0));
        Assert.Equal(Palette.Dark.Scaled(1.0), Palette.Dark.Scaled(5.0));
        Assert.Equal(Palette.Dark.Scaled(1.0), Palette.Dark.Scaled(double.NaN));
    }

    [Fact]
    public void Scaled_KeepsNightRedFreeOfGreenAndBlue()
    {
        Palette dim = Palette.NightRed.Scaled(0.2);
        foreach (var (role, hex) in dim.Entries())
        {
            var (_, g, b) = Palette.ParseHex(hex);
            Assert.True(g == 0 && b == 0, $"NightRed scalata .{role} = {hex} contiene verde o blu");
        }
    }

    [Fact]
    public void ParseHex_AcceptsWithAndWithoutHash()
    {
        Assert.Equal<(byte, byte, byte)>((92, 200, 255), Palette.ParseHex("#5CC8FF"));
        Assert.Equal<(byte, byte, byte)>((92, 200, 255), Palette.ParseHex("5CC8FF"));
        Assert.Equal<(byte, byte, byte)>((0, 0, 0), Palette.ParseHex("#000000"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12")]
    [InlineData("#1234567")]
    [InlineData("#GG0000")]
    [InlineData("#FFFFFFFF")]
    public void ParseHex_RejectsInvalidInput(string hex)
    {
        Assert.Throws<ArgumentException>(() => { Palette.ParseHex(hex); });
    }

    [Fact]
    public void ContrastRatio_BlackOnWhiteIsTwentyOne()
    {
        Assert.Equal(21.0, Palette.ContrastRatio("#FFFFFF", "#000000"), 6);
        Assert.Equal(21.0, Palette.ContrastRatio("#000000", "#FFFFFF"), 6);
    }

    [Fact]
    public void ContrastRatio_OfAColorWithItselfIsOne()
    {
        Assert.Equal(1.0, Palette.ContrastRatio("#5CC8FF", "#5CC8FF"), 6);
    }

    [Fact]
    public void Entries_ListEveryRoleOnceWithAValidColor()
    {
        var entries = Palette.Dark.Entries();
        Assert.Equal(23, entries.Count);
        Assert.Equal(entries.Count, entries.Select(e => e.Key).Distinct().Count());
        foreach (var entry in entries)
        {
            Palette.ParseHex(entry.Value);
        }
    }

    [Fact]
    public void Dark_UsesTheValuesOfSection53()
    {
        Assert.Equal("#000000", Palette.Dark.Background);
        Assert.Equal("#06080C", Palette.Dark.Surface1);
        Assert.Equal("#5CC8FF", Palette.Dark.Accent);
        Assert.Equal("#8A7DFF", Palette.Dark.Accent2);
        Assert.Equal("#3DDC97", Palette.Dark.Success);
        Assert.Equal("#FFC24B", Palette.Dark.Warning);
        Assert.Equal("#FF5C7A", Palette.Dark.Error);
    }
}
