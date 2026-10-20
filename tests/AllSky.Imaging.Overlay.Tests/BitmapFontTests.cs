using AllSky.Imaging.Overlay;
using Xunit;

namespace AllSky.Imaging.Overlay.Tests;

public class BitmapFontTests
{
    private static string RequiredCharacters() =>
        new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray()) + "àèéìòùÀÈÉÌÒÙ°±µ";

    [Fact]
    public void AllRequiredCharactersHaveOwnGlyph()
    {
        foreach (char c in RequiredCharacters())
        {
            Assert.True(BitmapFont.IsSupported(c), $"carattere senza glifo: '{c}'");
            Assert.Equal(BitmapFont.GlyphHeight, BitmapFont.GetGlyph(c).Length);
        }
    }

    [Fact]
    public void UnsupportedCharacterIsDrawnAsQuestionMark()
    {
        Assert.False(BitmapFont.IsSupported('€'));
        Assert.Equal(BitmapFont.GetGlyph('?').ToArray(), BitmapFont.GetGlyph('€').ToArray());
    }

    [Fact]
    public void I_HasSerifsTopAndBottom_WhileOneAndLowerLDoNot()
    {
        Assert.True(BitmapFont.GetPixel('I', 1, 2));
        Assert.True(BitmapFont.GetPixel('I', 6, 2));
        Assert.True(BitmapFont.GetPixel('I', 1, 11));

        Assert.False(BitmapFont.GetPixel('1', 6, 2));
        Assert.True(BitmapFont.GetPixel('1', 1, 3));

        Assert.False(BitmapFont.GetPixel('l', 1, 2));
        Assert.True(BitmapFont.GetPixel('l', 3, 2));
        Assert.False(BitmapFont.GetPixel('l', 1, 11));

        Assert.NotEqual(BitmapFont.GetGlyph('I').ToArray(), BitmapFont.GetGlyph('1').ToArray());
        Assert.NotEqual(BitmapFont.GetGlyph('I').ToArray(), BitmapFont.GetGlyph('l').ToArray());
        Assert.NotEqual(BitmapFont.GetGlyph('1').ToArray(), BitmapFont.GetGlyph('l').ToArray());
    }

    [Fact]
    public void AsciiArtOfAg0Il1_MatchesDesignedRows()
    {
        string[] art = Render("Ag0Il1");

        Assert.Equal(BitmapFont.GlyphHeight, art.Length);
        Assert.All(art, row => Assert.Equal(6 * BitmapFont.GlyphWidth, row.Length));

        // Riga 2 (parte alta delle maiuscole): A, g vuota, 0, I (serif), l (asta), 1 (bandierina).
        Assert.Equal("...##..." + "........" + "..####.." + ".######." + "...#...." + "..#.....", art[2]);
        // Riga 11 (linea di base): piede della l a destra, base della 1 e serif della I.
        Assert.Equal(".#....#." + "....#..." + "..####.." + ".######." + "...##..." + ".#####..", art[11]);
    }

    [Fact]
    public void AccentsSitAboveTheBaseLetter()
    {
        Assert.True(BitmapFont.GetPixel('è', 2, 3));
        Assert.False(BitmapFont.GetPixel('e', 2, 3));

        Assert.True(BitmapFont.GetPixel('É', 4, 0));
        Assert.False(BitmapFont.GetPixel('E', 4, 0));

        Assert.True(BitmapFont.GetPixel('À', 2, 0));
        Assert.False(BitmapFont.GetPixel('A', 2, 0));
    }

    [Fact]
    public void DegreeSignIsARing()
    {
        Assert.True(BitmapFont.GetPixel('°', 2, 2));
        Assert.False(BitmapFont.GetPixel('°', 3, 3));
    }

    [Fact]
    public void GetPixel_RejectsCoordinatesOutsideTheCell()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BitmapFont.GetPixel('A', 8, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitmapFont.GetPixel('A', 0, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => BitmapFont.GetPixel('A', -1, 0));
    }

    /// <summary>Rende il testo come ASCII-art: una stringa per riga, '#' acceso e '.' spento.</summary>
    private static string[] Render(string text)
    {
        var rows = new string[BitmapFont.GlyphHeight];
        for (int y = 0; y < rows.Length; y++)
        {
            rows[y] = string.Concat(text.Select(c => RowOf(c, y)));
        }

        return rows;
    }

    private static string RowOf(char c, int y)
    {
        return new string(Enumerable.Range(0, BitmapFont.GlyphWidth)
            .Select(x => BitmapFont.GetPixel(c, x, y) ? '#' : '.')
            .ToArray());
    }
}
