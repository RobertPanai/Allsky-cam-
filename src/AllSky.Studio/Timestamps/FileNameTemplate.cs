using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AllSky.Studio;

/// <summary>
/// Modello del nome file con segnaposto <c>{yyyy}</c> <c>{MM}</c> <c>{dd}</c> <c>{HH}</c> <c>{mm}</c> <c>{ss}</c>
/// e jolly <c>*</c>, es. <c>allsky_{yyyy}{MM}{dd}_{HH}{mm}{ss}.jpg</c>.
/// <para>
/// Il modello viene tradotto in una espressione regolare: il testo fisso è sempre escapato, i segnaposto
/// accettano solo cifre ASCII, il jolly è un gruppo non avido. Il motore è non-backtracking e ha un
/// timeout, quindi un modello o un nome non possono far esplodere il tempo di calcolo.
/// </para>
/// <para>
/// Obbligatori: <c>{yyyy}</c>, <c>{MM}</c> e <c>{dd}</c>. Se mancano <c>{HH}</c>, <c>{mm}</c> o <c>{ss}</c>
/// l'ora è 00:00:00. Il modello si applica al solo nome del file, senza cartelle.
/// </para>
/// </summary>
internal sealed class FileNameTemplate
{
    private const int MaxLength = 200;
    private const int MaxWildcards = 8;
    private static readonly string[] Placeholders = ["yyyy", "MM", "dd", "HH", "mm", "ss"];
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(500);

    private readonly Regex _regex;
    private readonly Dictionary<string, string> _groupNames;

    private FileNameTemplate(Regex regex, Dictionary<string, string> groupNames)
    {
        _regex = regex;
        _groupNames = groupNames;
    }

    /// <summary>Compila il modello. In caso di errore restituisce false e un messaggio in italiano.</summary>
    internal static bool TryCompile(string? template, out FileNameTemplate? result, out string? error)
    {
        result = null;
        error = null;
        if (string.IsNullOrWhiteSpace(template))
        {
            error = "il modello del nome file è vuoto.";
            return false;
        }

        if (template.Length > MaxLength)
        {
            error = $"il modello del nome file supera {MaxLength} caratteri.";
            return false;
        }

        var body = new StringBuilder();
        var groupNames = new Dictionary<string, string>(StringComparer.Ordinal);
        int wildcards = 0;

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            switch (c)
            {
                case '{':
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

                    if (groupNames.ContainsKey(token))
                    {
                        error = $"segnaposto ripetuto: «{{{token}}}».";
                        return false;
                    }

                    string groupName = "p" + groupNames.Count.ToString(CultureInfo.InvariantCulture);
                    groupNames[token] = groupName;
                    int digits = token == "yyyy" ? 4 : 2;
                    body.Append($"(?<{groupName}>[0-9]{{{digits}}})");
                    i = close;
                    break;
                }

                case '}':
                    error = "parentesi graffa chiusa senza apertura.";
                    return false;

                case '*':
                    wildcards++;
                    if (wildcards > MaxWildcards)
                    {
                        error = $"troppi jolly «*»: al massimo {MaxWildcards}.";
                        return false;
                    }

                    body.Append(".*?");
                    break;

                case '/':
                case '\\':
                    error = "il modello non può contenere separatori di percorso.";
                    return false;

                default:
                    body.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }

        foreach (string required in new[] { "yyyy", "MM", "dd" })
        {
            if (!groupNames.ContainsKey(required))
            {
                error = "il modello deve contenere {yyyy}, {MM} e {dd}.";
                return false;
            }
        }

        try
        {
            var regex = new Regex(
                @"\A" + body + @"\z",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking,
                MatchTimeout);
            result = new FileNameTemplate(regex, groupNames);
            return true;
        }
        catch (ArgumentException)
        {
            error = "modello del nome file non valido.";
            return false;
        }
    }

    /// <summary>
    /// Confronta il nome del file con il modello e restituisce la data locale (senza fuso).
    /// </summary>
    internal bool TryMatch(string fileName, out DateTime local, out string? error)
    {
        local = default;
        error = null;

        Match match;
        try
        {
            match = _regex.Match(fileName);
        }
        catch (RegexMatchTimeoutException)
        {
            error = "nome file troppo complesso per il modello.";
            return false;
        }

        if (!match.Success)
        {
            error = "il nome non corrisponde al modello.";
            return false;
        }

        int year = Field(match, "yyyy", 1);
        int month = Field(match, "MM", 1);
        int day = Field(match, "dd", 1);
        int hour = Field(match, "HH", 0);
        int minute = Field(match, "mm", 0);
        int second = Field(match, "ss", 0);

        if (year < 1 || month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour > 23 || minute > 59 || second > 59)
        {
            error = "data non valida nel nome file.";
            return false;
        }

        local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        return true;
    }

    private int Field(Match match, string placeholder, int fallback)
    {
        if (!_groupNames.TryGetValue(placeholder, out string? groupName))
        {
            return fallback;
        }

        return int.Parse(match.Groups[groupName].Value, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
