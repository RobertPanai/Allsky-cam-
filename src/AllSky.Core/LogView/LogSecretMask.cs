using System.Text.RegularExpressions;

namespace AllSky.Core.LogView;

/// <summary>
/// Maschera i segreti nel testo dei log prima che arrivino alla UI o agli appunti. È un controllo in profondità:
/// l'Host già filtra i propri segreti in scrittura, ma i log possono contenere testo di altre fonti o di versioni precedenti.
/// Sostituisce il valore con <c>***</c> e lascia la chiave, così la riga resta leggibile.
/// </summary>
/// <remarks>
/// Copre: intestazioni <c>Authorization</c> e <c>Bearer</c>; coppie <c>chiave: valore</c> o <c>chiave=valore</c> (anche con
/// virgolette e nei parametri d'URL) per chiavi che contengono token, password, pwd, secret, api key, client secret;
/// formati noti di Google (<c>GOCSPX-</c>, <c>ya29.</c>, <c>1//</c>).
/// Logica pura. Idempotente: applicata due volte dà lo stesso risultato.
/// </remarks>
public static class LogSecretMask
{
    /// <summary>Valore che sostituisce il segreto.</summary>
    public const string Mask = "***";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex BearerRegex = new(
        @"(?i)\bBearer[ \t]+[^\s""',;]+",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex AuthorizationRegex = new(
        @"(?i)\bAuthorization[ \t]*:[^\r\n]*",
        RegexOptions.CultureInvariant, RegexTimeout);

    // Chiave senza spazi che contiene una parola sensibile (access_token, client-secret, Password...), poi : o =.
    // Il valore è tra virgolette singole o doppie, oppure fino al primo separatore.
    private static readonly Regex KeyValueRegex = new(
        @"(?i)(?<key>[""']?[\w\-.]*(?:token|password|passwd|pwd|secret|api[_\-]?key|apikey)[\w\-.]*[""']?)(?<sep>[ \t]*[:=][ \t]*)(?:(?<quote>[""'])(?<qv>[^""'\r\n]*)\k<quote>|(?<bv>[^\s,;&}\])]+))",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex ClientSecretRegex = new(
        @"(?i)(?<key>client[ _\-]secret)(?<sep>[ \t]*[:=][ \t]*)(?:(?<quote>[""'])(?<qv>[^""'\r\n]*)\k<quote>|(?<bv>[^\s,;&}\])]+))",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex GoogleSecretRegex = new(
        @"\bGOCSPX-[A-Za-z0-9_\-]+",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex GoogleAccessTokenRegex = new(
        @"\bya29\.[A-Za-z0-9_\-.]+",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex GoogleRefreshTokenRegex = new(
        @"\b1//[A-Za-z0-9_\-]{10,}",
        RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>Restituisce il testo con i segreti mascherati.</summary>
    /// <param name="text">Testo da filtrare; null restituisce stringa vuota.</param>
    public static string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string value = BearerRegex.Replace(text, "Bearer " + Mask);
        value = AuthorizationRegex.Replace(value, "Authorization: " + Mask);
        value = ClientSecretRegex.Replace(value, ReplaceValue);
        value = KeyValueRegex.Replace(value, ReplaceValue);
        value = GoogleSecretRegex.Replace(value, Mask);
        value = GoogleAccessTokenRegex.Replace(value, Mask);
        return GoogleRefreshTokenRegex.Replace(value, Mask);
    }

    private static string ReplaceValue(Match match)
    {
        Group quoted = match.Groups["quote"];
        string value = quoted.Success
            ? quoted.Value + Mask + quoted.Value
            : Mask;
        return match.Groups["key"].Value + match.Groups["sep"].Value + value;
    }
}
