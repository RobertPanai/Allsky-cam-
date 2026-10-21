using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class DesktopTextsTests
{
    /// <summary>Tutte le proprietà di testo di <see cref="DesktopTexts"/> (stringhe pubbliche d'istanza).</summary>
    private static IEnumerable<PropertyInfo> TextProperties() =>
        typeof(DesktopTexts).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string));

    /// <summary>Segnaposto {0}, {1}... presenti nel testo, in ordine di comparsa.</summary>
    private static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{\d+\}").Select(m => m.Value));

    [Fact]
    public void EveryText_IsFilledInBothLanguages()
    {
        foreach (PropertyInfo property in TextProperties())
        {
            string? italian = (string?)property.GetValue(DesktopTexts.Italian);
            string? english = (string?)property.GetValue(DesktopTexts.English);

            Assert.False(string.IsNullOrWhiteSpace(italian), $"Testo italiano vuoto: {property.Name}");
            Assert.False(string.IsNullOrWhiteSpace(english), $"Testo inglese vuoto: {property.Name}");
        }
    }

    [Fact]
    public void Placeholders_AreTheSameInBothLanguages()
    {
        // string.Format con un segnaposto mancante lancia FormatException: le due lingue devono avere gli stessi.
        foreach (PropertyInfo property in TextProperties())
        {
            string italian = (string)property.GetValue(DesktopTexts.Italian)!;
            string english = (string)property.GetValue(DesktopTexts.English)!;

            Assert.Equal(Placeholders(italian), Placeholders(english));
        }
    }

    [Fact]
    public void StateName_MapsKnownStates()
    {
        Assert.Equal(DesktopTexts.Italian.StateIdle, DesktopTexts.Italian.StateName("idle"));
        Assert.Equal(DesktopTexts.Italian.StateCapturing, DesktopTexts.Italian.StateName("capturing"));
        Assert.Equal(DesktopTexts.Italian.StateError, DesktopTexts.Italian.StateName("error"));
        Assert.Equal(DesktopTexts.English.StateCapturing, DesktopTexts.English.StateName("capturing"));
    }

    [Fact]
    public void StateName_UnknownState_UsesUnknownText()
    {
        Assert.Equal(DesktopTexts.Italian.StateUnknown, DesktopTexts.Italian.StateName("qualcosa"));
    }

    [Fact]
    public void FormatLastFrame_WithoutFrame_UsesNoFrameText()
    {
        Assert.Equal(DesktopTexts.Italian.NoFrameYet, DesktopTexts.Italian.FormatLastFrame(null, null));
        Assert.Equal(DesktopTexts.Italian.NoFrameYet, DesktopTexts.Italian.FormatLastFrame("", null));
    }

    [Fact]
    public void FormatLastFrame_WithoutTimestamp_ReturnsOnlyId()
    {
        Assert.Equal("20261021T123005Z", DesktopTexts.Italian.FormatLastFrame("20261021T123005Z", null));
    }

    [Fact]
    public void FormatLastFrame_WithTimestamp_ShowsLocalTime()
    {
        var timestamp = new DateTimeOffset(2026, 10, 21, 12, 30, 5, TimeSpan.Zero);
        string local = timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        string text = DesktopTexts.English.FormatLastFrame("20261021T123005Z", timestamp);

        Assert.Equal($"20261021T123005Z ({local})", text);
    }
}
