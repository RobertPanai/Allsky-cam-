using System.Text.RegularExpressions;

namespace AllSky.Host;

/// <summary>
/// Filtro dei segreti per il log generale e per i messaggi d'errore delle risposte HTTP. Sostituisce con <c>***</c>
/// i segreti noti (in chiaro e URL-encoded) e maschera "Bearer ..." e le righe "Authorization: ...".
/// A differenza di <see cref="AllSky.Publish.Redactor"/> non tronca il testo: i log dell'Host contengono anche
/// le tracce complete delle eccezioni.
/// </summary>
internal static class SecretMask
{
    private const string Mask = "***";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex BearerRegex = new(
        @"(?i)\bBearer\s+[^\s""',;]+",
        RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex AuthorizationRegex = new(
        @"(?i)\bAuthorization\s*:[^\r\n]*",
        RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>Restituisce il testo con i segreti mascherati.</summary>
    /// <param name="text">Testo da filtrare.</param>
    /// <param name="secrets">Segreti correnti (anche vuoti: vengono ignorati).</param>
    public static string Apply(string text, IReadOnlyCollection<string> secrets)
    {
        string value = text;
        foreach (string secret in secrets.Where(s => s.Length > 0).OrderByDescending(s => s.Length))
        {
            value = value.Replace(secret, Mask, StringComparison.Ordinal);
            string encoded = Uri.EscapeDataString(secret);
            if (encoded.Length > 0 && encoded != secret)
            {
                value = value.Replace(encoded, Mask, StringComparison.Ordinal);
            }
        }

        value = BearerRegex.Replace(value, "Bearer " + Mask);
        return AuthorizationRegex.Replace(value, "Authorization: " + Mask);
    }
}
