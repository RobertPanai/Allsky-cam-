using System.Text.RegularExpressions;

namespace AllSky.Publish;

/// <summary>
/// Rende sicuro per log, messaggi e report un testo che potrebbe contenere segreti o URL con credenziali.
/// Regole, nell'ordine: sostituzione dei segreti (anche in forma URL-encoded) con ***, caratteri di controllo
/// rimossi (tab, a capo e simili diventano spazi), userinfo rimossa dagli URL, query string e fragment rimossi,
/// 'Bearer &lt;token&gt;' e 'Authorization: ...' mascherati, testo tagliato a 300 caratteri (incluso il "...").
/// Non lancia mai: se il provider dei segreti fallisce il testo non puo' essere verificato e viene sostituito
/// con una frase fissa; null diventa stringa vuota.
/// </summary>
public sealed class Redactor
{
    /// <summary>Lunghezza massima del testo sanificato, incluso l'eventuale "...".</summary>
    public const int MaxLength = 300;

    private const string Mask = "***";
    private const string FallbackText = "Errore interno: dettagli non disponibili.";
    private const string Ellipsis = "...";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex UserInfoRegex = new(
        @"(?i)([a-z][a-z0-9+.\-]*://)[^\s/?#@""'<>]+@",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex QueryOrFragmentRegex = new(
        @"(?i)([a-z][a-z0-9+.\-]*://[^\s?#""'<>]*)[?#][^\s""'<>]*",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex BearerRegex = new(
        @"(?i)\bBearer\s+[^\s""',;]+",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex AuthorizationRegex = new(
        @"(?i)\bAuthorization\s*:.*",
        RegexOptions.CultureInvariant, RegexTimeout);

    private readonly Func<IReadOnlyCollection<string>> _secrets;

    /// <summary>Crea il sanificatore. <paramref name="secrets"/> restituisce i segreti correnti (anche vuoti o null).</summary>
    public Redactor(Func<IReadOnlyCollection<string>> secrets)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    }

    /// <summary>Restituisce il testo sanificato. Non lancia mai; null restituisce stringa vuota.</summary>
    public string Sanitize(string? text)
    {
        if (text is null)
            return string.Empty;
        try
        {
            string value = ReplaceSecrets(text);
            value = CleanControlCharacters(value);
            value = UserInfoRegex.Replace(value, "$1");
            value = QueryOrFragmentRegex.Replace(value, "$1");
            value = BearerRegex.Replace(value, "Bearer " + Mask);
            value = AuthorizationRegex.Replace(value, "Authorization: " + Mask);
            return Truncate(value.Trim());
        }
        catch (Exception)
        {
            // Se qualcosa va storto non si puo' garantire la sanificazione: si restituisce una frase fissa.
            return FallbackText;
        }
    }

    /// <summary>
    /// Sostituisce ogni segreto non vuoto, in forma grezza e URL-encoded. I segreti piu' lunghi vengono
    /// sostituiti per primi, cosi' un segreto che contiene un altro non lascia residui.
    /// Se il provider fallisce l'eccezione risale a Sanitize, che restituisce la frase fissa.
    /// </summary>
    private string ReplaceSecrets(string text)
    {
        IReadOnlyCollection<string>? provided = _secrets();
        if (provided is null || provided.Count == 0)
            return text;

        var forms = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? secret in provided)
        {
            if (string.IsNullOrEmpty(secret))
                continue;
            forms.Add(secret);
            forms.Add(Uri.EscapeDataString(secret));
        }

        foreach (string form in forms.OrderByDescending(f => f.Length).ThenBy(f => f, StringComparer.Ordinal))
            text = text.Replace(form, Mask, StringComparison.Ordinal);
        return text;
    }

    /// <summary>Tab, a capo e simili diventano spazi; gli altri caratteri di controllo vengono rimossi.</summary>
    private static string CleanControlCharacters(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\t' or '\r' or '\n')
                sb.Append(' ');
            else if (!char.IsControl(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Tronca a <see cref="MaxLength"/> caratteri senza spezzare una coppia di surrogati.</summary>
    private static string Truncate(string value)
    {
        if (value.Length <= MaxLength)
            return value;

        int keep = MaxLength - Ellipsis.Length;
        if (char.IsHighSurrogate(value[keep - 1]))
            keep--;
        return value[..keep] + Ellipsis;
    }
}
