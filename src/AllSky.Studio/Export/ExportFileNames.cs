using System.Globalization;
using System.Text;

namespace AllSky.Studio;

/// <summary>
/// Modello del nome file di un'esportazione locale, con i segnaposto <c>{project}</c>, <c>{date}</c> e <c>{n}</c>.
/// <para>
/// Regole [S-scelta]: il testo fisso non può contenere <c>&lt; &gt; : " / \ | ? *</c>, caratteri di controllo o
/// <c>..</c>; il modello non può finire con punto o spazio. I valori dei segnaposto vengono ripuliti con gli stessi
/// criteri, quindi il nome risultante non esce mai dalla cartella scelta.
/// </para>
/// </summary>
public static class ExportFileNames
{
    /// <summary>Lunghezza massima del modello, in caratteri.</summary>
    public const int MaxTemplateLength = 120;

    /// <summary>Lunghezza massima del nome finale, estensione inclusa.</summary>
    public const int MaxFileNameLength = 200;

    /// <summary>Lunghezza massima della parte "progetto" nel nome.</summary>
    public const int MaxProjectPartLength = 60;

    /// <summary>Estensione aggiunta se il modello non la contiene.</summary>
    public const string Extension = ".mp4";

    private const string DateFormat = "yyyy-MM-dd";

    private static readonly char[] ForbiddenChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly string[] ReservedBaseNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    private static readonly string[] Placeholders = ["project", "date", "n"];

    /// <summary>Controlla un modello. Restituisce gli errori in italiano (vuoto se è valido).</summary>
    public static IReadOnlyList<string> ValidateTemplate(string? template)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template))
        {
            errors.Add("il modello del nome file è vuoto.");
            return errors;
        }

        if (template.Length > MaxTemplateLength)
        {
            errors.Add($"il modello del nome file supera {MaxTemplateLength} caratteri.");
            return errors;
        }

        if (!TryParseParts(template, out List<NamePart> parts, out string? parseError))
        {
            errors.Add(parseError!);
            return errors;
        }

        string literal = string.Concat(parts.Where(p => !p.IsPlaceholder).Select(p => p.Text));
        string? literalError = CheckLiteral(literal);
        if (literalError is not null)
        {
            errors.Add(literalError);
        }

        if (template.TrimEnd().Length != template.Length || template.EndsWith('.'))
        {
            errors.Add("il modello del nome file non può finire con un punto o uno spazio.");
        }

        return errors;
    }

    /// <summary>
    /// Compone il nome file finale. Restituisce false con un messaggio in italiano se il modello non è valido.
    /// </summary>
    /// <param name="template">Modello già validato o da validare.</param>
    /// <param name="projectName">Nome del progetto (ripulito prima dell'uso).</param>
    /// <param name="date">Data locale per <c>{date}</c>.</param>
    /// <param name="sequence">Numero del lavoro per <c>{n}</c>, da 1.</param>
    /// <param name="fileName">Solo il nome, senza cartella.</param>
    /// <param name="error">Errore in italiano, o null.</param>
    public static bool TryRender(string? template, string projectName, DateOnly date, int sequence,
        out string fileName, out string? error)
    {
        fileName = string.Empty;
        IReadOnlyList<string> problems = ValidateTemplate(template);
        if (problems.Count > 0)
        {
            error = problems[0];
            return false;
        }

        if (!TryParseParts(template!, out List<NamePart> parts, out string? parseError))
        {
            error = parseError;
            return false;
        }

        var sb = new StringBuilder();
        foreach (NamePart part in parts)
        {
            if (!part.IsPlaceholder)
            {
                sb.Append(part.Text);
                continue;
            }

            sb.Append(part.Text switch
            {
                "project" => SanitizeFragment(projectName, MaxProjectPartLength, "progetto"),
                "date" => date.ToString(DateFormat, CultureInfo.InvariantCulture),
                "n" => sequence.ToString(CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException("Segnaposto non gestito: " + part.Text),
            });
        }

        string name = sb.ToString().Trim();
        if (!name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            name += Extension;
        }

        if (name.Length > MaxFileNameLength)
        {
            error = $"il nome del file supera {MaxFileNameLength} caratteri.";
            return false;
        }

        if (!IsSafeFileName(name))
        {
            error = "il nome del file risultante non è valido.";
            return false;
        }

        string baseName = Path.GetFileNameWithoutExtension(name);
        if (ReservedBaseNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
        {
            error = "il nome del file è riservato dal sistema: scegliere un altro modello.";
            return false;
        }

        fileName = name;
        error = null;
        return true;
    }

    /// <summary>
    /// Vero se il nome è un nome semplice, senza separatori, senza <c>..</c>, senza caratteri vietati e senza
    /// punto o spazio finali.
    /// </summary>
    public static bool IsSafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (Path.GetFileName(name) != name || name.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (name.IndexOfAny(ForbiddenChars) >= 0 || name.Any(char.IsControl))
        {
            return false;
        }

        return !name.EndsWith('.') && !name.EndsWith(' ');
    }

    /// <summary>Nome con un numero prima dell'estensione, es. «notte (2).mp4».</summary>
    public static string WithSuffix(string fileName, int number)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        return $"{stem} ({number.ToString(CultureInfo.InvariantCulture)}){ext}";
    }

    /// <summary>
    /// Ripulisce un testo per usarlo in un nome file: caratteri vietati e di controllo diventano «_», i «..»
    /// sono tolti, punti e spazi ai bordi sono tolti. Il risultato non è mai vuoto.
    /// </summary>
    public static string SanitizeFragment(string? text, int maxLength, string fallback)
    {
        var sb = new StringBuilder(text?.Length ?? 0);
        foreach (char c in text ?? string.Empty)
        {
            sb.Append(c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' || char.IsControl(c) ? '_' : c);
        }

        string clean = sb.ToString();
        while (clean.Contains("..", StringComparison.Ordinal))
        {
            clean = clean.Replace("..", string.Empty, StringComparison.Ordinal);
        }

        clean = clean.Trim(' ', '.');
        if (clean.Length > maxLength)
        {
            clean = clean[..maxLength].TrimEnd(' ', '.');
        }

        return clean.Length == 0 ? fallback : clean;
    }

    private static string? CheckLiteral(string literal)
    {
        if (literal.IndexOfAny(ForbiddenChars) >= 0)
        {
            char bad = literal.First(c => ForbiddenChars.Contains(c));
            return $"caratteri non ammessi nel nome file: «{bad}».";
        }

        if (literal.Any(char.IsControl))
        {
            return "il modello contiene caratteri di controllo.";
        }

        if (literal.Contains("..", StringComparison.Ordinal))
        {
            return "il modello non può contenere «..».";
        }

        return null;
    }

    private static bool TryParseParts(string template, out List<NamePart> parts, out string? error)
    {
        parts = [];
        error = null;
        var literal = new StringBuilder();
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c == '{')
            {
                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    error = "segnaposto non chiuso: manca la «}».";
                    return false;
                }

                string token = template.Substring(i + 1, close - i - 1);
                if (Array.IndexOf(Placeholders, token) < 0)
                {
                    error = $"segnaposto non riconosciuto: «{{{token}}}».";
                    return false;
                }

                if (literal.Length > 0)
                {
                    parts.Add(new NamePart(literal.ToString(), false));
                    literal.Clear();
                }

                parts.Add(new NamePart(token, true));
                i = close + 1;
                continue;
            }

            if (c == '}')
            {
                error = "parentesi graffa chiusa senza apertura.";
                return false;
            }

            literal.Append(c);
            i++;
        }

        if (literal.Length > 0)
        {
            parts.Add(new NamePart(literal.ToString(), false));
        }

        return true;
    }

    private sealed record NamePart(string Text, bool IsPlaceholder);
}
