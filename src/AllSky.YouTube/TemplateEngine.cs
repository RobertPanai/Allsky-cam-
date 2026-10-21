using System.Globalization;
using System.Text;

namespace AllSky.YouTube;

/// <summary>
/// Parser, renderer e normalizzazione comuni ai modelli con segnaposto (titolo e descrizione).
/// Il parser raccoglie tutti gli errori senza lanciare eccezioni.
/// </summary>
internal static class TemplateEngine
{
    /// <summary>Tipi di segnaposto riconosciuti.</summary>
    internal enum Kind { Data, Night, Time, Location, Frames, Duration, Meteors, Transits }

    /// <summary>Elemento del modello: testo letterale oppure segnaposto.</summary>
    internal abstract record Token;

    internal sealed record LiteralToken(string Text) : Token;

    /// <summary>Segnaposto; <see cref="Format"/> e null quando si usa il formato di default.</summary>
    internal sealed record PlaceholderToken(Kind Kind, string? Format) : Token;

    internal const string Ellipsis = "…";

    private const int MaxFormatLength = 64;
    private const string DefaultDateFormat = "yyyy-MM-dd";
    private const string DefaultTimeFormat = "HH:mm";

    // Date di prova per validare i formati: un formato che fallisce qui fallirebbe anche in fase di rendering.
    private static readonly DateTimeOffset SampleOffset = new(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(1));
    private static readonly DateTime SampleDateTime = new(2026, 1, 2, 3, 4, 5);

    private static readonly Dictionary<string, Kind> Names = new(StringComparer.Ordinal)
    {
        ["data"] = Kind.Data,
        ["notte"] = Kind.Night,
        ["ora"] = Kind.Time,
        ["luogo"] = Kind.Location,
        ["fotogrammi"] = Kind.Frames,
        ["durata"] = Kind.Duration,
        ["meteore"] = Kind.Meteors,
        ["transiti"] = Kind.Transits,
    };

    /// <summary>Converte il testo in token. Gli errori finiscono in <paramref name="errors"/>.</summary>
    internal static List<Token> Tokenize(string template, List<TemplateError> errors)
    {
        var tokens = new List<Token>();
        var literal = new StringBuilder();
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c == '{')
            {
                // "{{" e una graffa letterale.
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    literal.Append('{');
                    i += 2;
                    continue;
                }
                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    AddError(errors, i, "segnaposto non chiuso: manca '}'.");
                    break;
                }
                FlushLiteral(literal, tokens);
                ParsePlaceholder(template.Substring(i + 1, close - i - 1), i, tokens, errors);
                i = close + 1;
            }
            else if (c == '}')
            {
                // "}}" e una graffa letterale; una '}' singola non ha un'apertura.
                if (i + 1 < template.Length && template[i + 1] == '}')
                {
                    literal.Append('}');
                    i += 2;
                    continue;
                }
                AddError(errors, i, "'}' senza '{' corrispondente (per una graffa letterale usare '}}').");
                i++;
            }
            else
            {
                literal.Append(c);
                i++;
            }
        }
        FlushLiteral(literal, tokens);
        return tokens;
    }

    /// <summary>Renderizza i token senza normalizzare il testo: la normalizzazione è compito del modello.</summary>
    internal static string Render(IReadOnlyList<Token> tokens, TitleContext ctx)
    {
        var sb = new StringBuilder();
        foreach (Token token in tokens)
        {
            if (token is LiteralToken literal)
                sb.Append(literal.Text);
            else if (token is PlaceholderToken placeholder)
                sb.Append(Value(placeholder, ctx));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Normalizzazione di una riga (titolo): caratteri di controllo e separatori di riga diventano spazi,
    /// '&lt;' e '&gt;' vengono rimossi, gli spazi multipli si riducono a uno e il testo si ritaglia.
    /// </summary>
    internal static string NormalizeLine(string raw) => CollapseLine(ReplaceLoneSurrogates(raw));

    /// <summary>
    /// Normalizzazione multilinea (descrizione): mantiene le righe, normalizza fine riga in '\n',
    /// applica <see cref="NormalizeLine"/> a ogni riga e lascia al massimo una riga vuota consecutiva.
    /// </summary>
    internal static string NormalizeMultiline(string raw)
    {
        string text = ReplaceLoneSurrogates(raw.Replace("\r\n", "\n").Replace('\r', '\n'));
        var kept = new List<string>();
        foreach (string line in text.Split('\n'))
        {
            string clean = CollapseLine(line);
            if (clean.Length == 0 && kept.Count > 0 && kept[^1].Length == 0)
                continue;
            kept.Add(clean);
        }
        return string.Join("\n", kept).Trim();
    }

    /// <summary>Ritaglia a <paramref name="maxLength"/> unità UTF-16 aggiungendo '…', senza spezzare una coppia surrogata.</summary>
    internal static string TruncateUtf16(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;
        int keep = maxLength - Ellipsis.Length;
        if (keep > 0 && char.IsHighSurrogate(text[keep - 1]))
            keep--;
        return text.Substring(0, keep).TrimEnd() + Ellipsis;
    }

    /// <summary>Ritaglia a <paramref name="maxBytes"/> byte UTF-8 aggiungendo '…', per code point interi.</summary>
    internal static string TruncateUtf8(string text, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
            return text;
        int budget = maxBytes - Encoding.UTF8.GetByteCount(Ellipsis);
        var sb = new StringBuilder();
        int used = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > budget)
                break;
            sb.Append(rune.ToString());
            used += rune.Utf8SequenceLength;
        }
        return sb.ToString().TrimEnd() + Ellipsis;
    }

    private static void FlushLiteral(StringBuilder literal, List<Token> tokens)
    {
        if (literal.Length == 0)
            return;
        tokens.Add(new LiteralToken(literal.ToString()));
        literal.Clear();
    }

    private static void ParsePlaceholder(string inner, int position, List<Token> tokens, List<TemplateError> errors)
    {
        if (inner.Length == 0)
        {
            AddError(errors, position, "segnaposto vuoto '{}'.");
            return;
        }
        if (inner.IndexOf('{') >= 0)
        {
            AddError(errors, position, "'{' non ammesso dentro un segnaposto.");
            return;
        }

        int colon = inner.IndexOf(':');
        string name = colon < 0 ? inner : inner.Substring(0, colon);
        string? format = colon < 0 ? null : inner.Substring(colon + 1);

        if (!Names.TryGetValue(name, out Kind kind))
        {
            AddError(errors, position, $"segnaposto sconosciuto '{{{name}}}'.");
            return;
        }
        if (format is null)
        {
            tokens.Add(new PlaceholderToken(kind, null));
            return;
        }
        if (!AcceptsFormat(kind))
        {
            AddError(errors, position, $"il segnaposto '{{{name}}}' non accetta un formato.");
            return;
        }
        if (format.Length == 0)
        {
            AddError(errors, position, $"formato vuoto in '{{{name}:}}'.");
            return;
        }
        if (format.Length > MaxFormatLength)
        {
            AddError(errors, position, $"formato troppo lungo (massimo {MaxFormatLength} caratteri).");
            return;
        }
        if (!IsValidFormat(kind, format, out string? reason))
        {
            AddError(errors, position, reason!);
            return;
        }
        tokens.Add(new PlaceholderToken(kind, format));
    }

    private static bool AcceptsFormat(Kind kind) => kind is Kind.Data or Kind.Night or Kind.Time;

    /// <summary>Valida un formato .NET con cultura invariante: deve essere accettato e non produrre caratteri di controllo.</summary>
    private static bool IsValidFormat(Kind kind, string format, out string? reason)
    {
        try
        {
            string sample = kind == Kind.Night
                ? SampleDateTime.ToString(format, CultureInfo.InvariantCulture)
                : SampleOffset.ToString(format, CultureInfo.InvariantCulture);
            if (sample.Any(char.IsControl))
            {
                reason = $"il formato '{format}' produce caratteri di controllo.";
                return false;
            }
            reason = null;
            return true;
        }
        catch (FormatException)
        {
            reason = $"formato non valido '{format}'.";
            return false;
        }
    }

    private static string Value(PlaceholderToken placeholder, TitleContext ctx) => placeholder.Kind switch
    {
        Kind.Data => ctx.NowLocal.ToString(placeholder.Format ?? DefaultDateFormat, CultureInfo.InvariantCulture),
        Kind.Night => ctx.NightStartLocal.ToString(placeholder.Format ?? DefaultDateFormat, CultureInfo.InvariantCulture),
        Kind.Time => ctx.NowLocal.ToString(placeholder.Format ?? DefaultTimeFormat, CultureInfo.InvariantCulture),
        Kind.Location => ctx.Location,
        Kind.Frames => ctx.Frames.ToString(CultureInfo.InvariantCulture),
        Kind.Duration => FormatDuration(ctx.VideoDuration),
        Kind.Meteors => ctx.Meteors.ToString(CultureInfo.InvariantCulture),
        Kind.Transits => ctx.Transits.ToString(CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException("Segnaposto non gestito: " + placeholder.Kind),
    };

    /// <summary>Durata in mm:ss; i minuti oltre 59 restano nei minuti (es. 75:05).</summary>
    private static string FormatDuration(TimeSpan duration) =>
        $"{((long)duration.TotalMinutes).ToString("00", CultureInfo.InvariantCulture)}:{duration.Seconds.ToString("00", CultureInfo.InvariantCulture)}";

    /// <summary>Sostituisce le unità surrogate isolate con U+FFFD, così ogni coppia valida resta intatta.</summary>
    private static string ReplaceLoneSurrogates(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                sb.Append('\uFFFD');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Una riga: rimuove '&lt;' e '&gt;', tratta i controlli come spazi e collassa gli spazi.</summary>
    private static string CollapseLine(string line)
    {
        var sb = new StringBuilder(line.Length);
        bool pendingSpace = false;
        foreach (char original in line)
        {
            if (original is '<' or '>')
                continue;
            char c = (char.IsControl(original) || original is '\u2028' or '\u2029') ? ' ' : original;
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }
            if (pendingSpace && sb.Length > 0)
                sb.Append(' ');
            pendingSpace = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Controlla il contesto: valori nulli o negativi sono errori del chiamante, quindi lanciano.</summary>
    internal static void Validate(TitleContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(ctx.Location);
        if (ctx.Frames < 0) throw new ArgumentOutOfRangeException(nameof(ctx), ctx.Frames, "Il numero di fotogrammi non può essere negativo.");
        if (ctx.VideoDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ctx), ctx.VideoDuration, "La durata non può essere negativa.");
        if (ctx.Meteors < 0) throw new ArgumentOutOfRangeException(nameof(ctx), ctx.Meteors, "Il numero di meteore non può essere negativo.");
        if (ctx.Transits < 0) throw new ArgumentOutOfRangeException(nameof(ctx), ctx.Transits, "Il numero di transiti non può essere negativo.");
    }

    private static void AddError(List<TemplateError> errors, int position, string message) =>
        errors.Add(new TemplateError(position, $"posizione {position}: {message}"));
}
