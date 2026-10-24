using System;
using System.Collections.Generic;
using System.Globalization;

namespace AllSky.Core.Design;

/// <summary>
/// Palette dei colori dell'interfaccia, un valore per ruolo (specifica in docs/ralph/IMPLEMENTATION_PLAN.md §5.3).
/// Ogni valore è un colore esadecimale "#RRGGBB" in maiuscolo.
/// </summary>
/// <remarks>
/// I bordi del piano sono rgba: qui sono già miscelati sulla superficie 1 (<c>#06080C</c>), perché i ruoli sono opachi.
/// Logica pura, senza dipendenze da WPF: il tema desktop la legge e la traduce in pennelli, i test la verificano ovunque.
/// </remarks>
public sealed record Palette(
    string Background,
    string Surface1,
    string Surface2,
    string Surface3,
    string Border,
    string BorderActive,
    string Text,
    string TextSecondary,
    string TextTertiary,
    string Accent,
    string Accent2,
    string Success,
    string Warning,
    string Error,
    string Sun,
    string Moon,
    string Satellite,
    string Meteor,
    string Fireball,
    string TwilightCivil,
    string TwilightNautical,
    string TwilightAstro,
    string Night)
{
    /// <summary>
    /// Tema scuro (§5.3). Il testo terziario è più chiaro del valore del piano (<c>#5D6778</c>), che misura
    /// 3.1–3.7:1 e non rispetta la regola del piano "≥ 4.5:1 sulle superfici dove compare". <c>#7A8594</c>
    /// supera 4.5:1 su tutte e quattro le superfici.
    /// </summary>
    public static readonly Palette Dark = new(
        Background: "#000000",
        Surface1: "#06080C",
        Surface2: "#0B0F17",
        Surface3: "#111723",
        Border: "#182233",
        BorderActive: "#357292",
        Text: "#E6EDF7",
        TextSecondary: "#8C98AB",
        TextTertiary: "#7A8594",
        Accent: "#5CC8FF",
        Accent2: "#8A7DFF",
        Success: "#3DDC97",
        Warning: "#FFC24B",
        Error: "#FF5C7A",
        Sun: "#FFC24B",
        Moon: "#C9D4E5",
        Satellite: "#5CC8FF",
        Meteor: "#FF8A3D",
        Fireball: "#FF5C7A",
        TwilightCivil: "#1B2A4A",
        TwilightNautical: "#111C33",
        TwilightAstro: "#0A1020",
        Night: "#000000");

    /// <summary>
    /// Modalità notte rossa: nessun colore ha componenti verde o blu, così l'occhio resta adattato al buio.
    /// Il testo primario è rosso pieno; secondario e terziario scendono solo fino al livello che tiene 4.5:1
    /// su tutte le superfici. I ruoli di stato e astronomici condividono la tonalità rossa, quindi si distinguono
    /// per luminosità, icona ed etichetta testuale (vedi docs/DESIGN-DESKTOP.md).
    /// </summary>
    public static readonly Palette NightRed = new(
        Background: "#000000",
        Surface1: "#080000",
        Surface2: "#0E0000",
        Surface3: "#140000",
        Border: "#3C0000",
        BorderActive: "#E60000",
        Text: "#FF0000",
        TextSecondary: "#F20000",
        TextTertiary: "#EE0000",
        Accent: "#F20000",
        Accent2: "#C00000",
        Success: "#F80000",
        Warning: "#FF0000",
        Error: "#F00000",
        Sun: "#FF0000",
        Moon: "#DC0000",
        Satellite: "#C00000",
        Meteor: "#E60000",
        Fireball: "#FF0000",
        TwilightCivil: "#2A0000",
        TwilightNautical: "#1A0000",
        TwilightAstro: "#0E0000",
        Night: "#000000");

    /// <summary>Fattore minimo della luminosità: sotto questo valore l'interfaccia diventa illeggibile.</summary>
    public const double MinimumFactor = 0.2;

    /// <summary>Fattore massimo: la palette di base.</summary>
    public const double MaximumFactor = 1.0;

    /// <summary>
    /// Rapporto di contrasto WCAG 2.x tra due colori esadecimali, da 1 a 21. L'ordine dei due colori non conta.
    /// </summary>
    /// <param name="fgHex">Colore del testo o del segno, "#RRGGBB" o "RRGGBB".</param>
    /// <param name="bgHex">Colore di sfondo, "#RRGGBB" o "RRGGBB".</param>
    /// <exception cref="ArgumentException">Uno dei due valori non è un colore esadecimale valido.</exception>
    public static double ContrastRatio(string fgHex, string bgHex)
    {
        var fg = ParseHex(fgHex);
        var bg = ParseHex(bgHex);
        double l1 = RelativeLuminance(fg.R, fg.G, fg.B);
        double l2 = RelativeLuminance(bg.R, bg.G, bg.B);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Restituisce una copia con tutti i canali RGB moltiplicati per <paramref name="factor"/>, arrotondati e limitati a 0–255.
    /// Il fattore è limitato a 0.2–1.0: un valore non numerico (NaN) equivale a 1.0.
    /// </summary>
    /// <param name="factor">Luminosità relativa, da 0.2 (buio) a 1.0 (palette piena).</param>
    public Palette Scaled(double factor)
    {
        double f = double.IsNaN(factor) ? MaximumFactor : Math.Clamp(factor, MinimumFactor, MaximumFactor);
        return Map(hex => ScaleHex(hex, f));
    }

    /// <summary>
    /// Legge un colore "#RRGGBB" (o "RRGGBB") e restituisce i tre canali. Accetta solo sei cifre esadecimali.
    /// </summary>
    /// <param name="hex">Colore da leggere.</param>
    /// <exception cref="ArgumentException">Il testo è vuoto, ha lunghezza diversa da sei cifre o contiene caratteri non esadecimali.</exception>
    public static (byte R, byte G, byte B) ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            throw new ArgumentException("Colore esadecimale vuoto.", nameof(hex));
        }

        string digits = hex.Trim();
        if (digits.StartsWith('#'))
        {
            digits = digits[1..];
        }

        if (digits.Length != 6)
        {
            throw new ArgumentException($"Colore esadecimale non valido (attese 6 cifre): '{hex}'.", nameof(hex));
        }

        foreach (char c in digits)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                throw new ArgumentException($"Colore esadecimale non valido (carattere non esadecimale): '{hex}'.", nameof(hex));
            }
        }

        byte r = byte.Parse(digits.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(digits.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte b = byte.Parse(digits.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (r, g, b);
    }

    /// <summary>Coppie (nome del ruolo, colore) nell'ordine della dichiarazione. Usato dal tema per creare le risorse.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Entries() =>
    [
        new(nameof(Background), Background),
        new(nameof(Surface1), Surface1),
        new(nameof(Surface2), Surface2),
        new(nameof(Surface3), Surface3),
        new(nameof(Border), Border),
        new(nameof(BorderActive), BorderActive),
        new(nameof(Text), Text),
        new(nameof(TextSecondary), TextSecondary),
        new(nameof(TextTertiary), TextTertiary),
        new(nameof(Accent), Accent),
        new(nameof(Accent2), Accent2),
        new(nameof(Success), Success),
        new(nameof(Warning), Warning),
        new(nameof(Error), Error),
        new(nameof(Sun), Sun),
        new(nameof(Moon), Moon),
        new(nameof(Satellite), Satellite),
        new(nameof(Meteor), Meteor),
        new(nameof(Fireball), Fireball),
        new(nameof(TwilightCivil), TwilightCivil),
        new(nameof(TwilightNautical), TwilightNautical),
        new(nameof(TwilightAstro), TwilightAstro),
        new(nameof(Night), Night),
    ];

    /// <summary>Applica una trasformazione a ogni colore della palette e restituisce la nuova palette.</summary>
    private Palette Map(Func<string, string> transform) => new(
        Background: transform(Background),
        Surface1: transform(Surface1),
        Surface2: transform(Surface2),
        Surface3: transform(Surface3),
        Border: transform(Border),
        BorderActive: transform(BorderActive),
        Text: transform(Text),
        TextSecondary: transform(TextSecondary),
        TextTertiary: transform(TextTertiary),
        Accent: transform(Accent),
        Accent2: transform(Accent2),
        Success: transform(Success),
        Warning: transform(Warning),
        Error: transform(Error),
        Sun: transform(Sun),
        Moon: transform(Moon),
        Satellite: transform(Satellite),
        Meteor: transform(Meteor),
        Fireball: transform(Fireball),
        TwilightCivil: transform(TwilightCivil),
        TwilightNautical: transform(TwilightNautical),
        TwilightAstro: transform(TwilightAstro),
        Night: transform(Night));

    /// <summary>Moltiplica i canali di un colore per il fattore, con arrotondamento e limiti 0–255.</summary>
    private static string ScaleHex(string hex, double factor)
    {
        var (r, g, b) = ParseHex(hex);
        return FormatHex(Scale(r, factor), Scale(g, factor), Scale(b, factor));
    }

    private static byte Scale(byte channel, double factor) =>
        (byte)Math.Clamp((int)Math.Round(channel * factor, MidpointRounding.AwayFromZero), 0, 255);

    private static string FormatHex(byte r, byte g, byte b) =>
        $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>Luminanza relativa sRGB (WCAG 2.x): linearizza ogni canale e somma i pesi 0.2126, 0.7152, 0.0722.</summary>
    private static double RelativeLuminance(byte r, byte g, byte b) =>
        0.2126 * LinearChannel(r) + 0.7152 * LinearChannel(g) + 0.0722 * LinearChannel(b);

    private static double LinearChannel(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
