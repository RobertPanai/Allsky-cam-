using System.Text.RegularExpressions;

namespace AllSky.Mcp;

/// <summary>
/// Regole HTTP del server MCP, senza dipendenze dal listener: testabili in modo puro.
/// </summary>
public static class McpHttpPolicy
{
    /// <summary>Unico percorso dell'endpoint MCP.</summary>
    public const string EndpointPath = "/mcp";

    /// <summary>Dimensione massima del corpo della richiesta, in byte (64 KB).</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>
    /// Vero se l'intestazione Host e' 127.0.0.1:porta oppure localhost:porta. Un altro Host (per esempio un dominio
    /// che un DNS rebinding fa risolvere su 127.0.0.1) o un'intestazione assente vengono rifiutati.
    /// </summary>
    public static bool IsAllowedHost(string? hostHeader, int port)
    {
        if (string.IsNullOrEmpty(hostHeader))
        {
            return false;
        }

        return string.Equals(hostHeader, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
            || string.Equals(hostHeader, $"localhost:{port}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Vero se l'intestazione Origin e' assente oppure e' un'origine locale (http o https verso 127.0.0.1, localhost o ::1,
    /// qualunque porta). Origin presente e non locale, "null" compreso, viene rifiutata: la specifica chiede 403 [V].
    /// </summary>
    public static bool IsAllowedOrigin(string? originHeader)
    {
        if (originHeader is null)
        {
            return true;
        }

        if (!Uri.TryCreate(originHeader, UriKind.Absolute, out Uri? origin))
        {
            return false;
        }

        if (origin.Scheme is not ("http" or "https"))
        {
            return false;
        }

        return origin.Host is "127.0.0.1" or "localhost" or "[::1]" or "::1";
    }

    /// <summary>Vero se il tipo di contenuto (senza parametri) e' application/json.</summary>
    public static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        int semicolon = contentType.IndexOf(';', StringComparison.Ordinal);
        string media = (semicolon < 0 ? contentType : contentType[..semicolon]).Trim();
        return string.Equals(media, "application/json", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Filtro dei testi destinati al client MCP: nessun percorso di file, indirizzo IP, URL o parola che indichi un segreto.
/// Un testo sospetto viene sostituito con un messaggio generico, mai parzialmente mascherato.
/// </summary>
public static class McpRedaction
{
    /// <summary>Sostituto dei testi che non si possono esporre.</summary>
    public const string Withheld = "Dettagli non inclusi: consultare il log dell'applicazione.";

    private static readonly Regex Sensitive = new(
        @"[A-Za-z]:[\\/]"                                   // percorso Windows (C:\ o C:/)
        + @"|\\\\"                                          // UNC o doppio backslash
        + @"|(?<![\w.])/[\w.\-]+/[\w./\-]+"                 // percorso Unix con almeno due segmenti
        + @"|https?://"                                     // URL
        + @"|\b\d{1,3}(\.\d{1,3}){3}\b"                     // indirizzo IPv4
        + @"|[A-Za-z0-9_\-+/]{32,}"                         // stringa lunga senza spazi (token, chiavi)
        + @"|(?i)token|secret|segret|passw|chiave|api[_\- ]?key|bearer|authorization|credenzial",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Vero se il testo contiene qualcosa che non va esposto al client.</summary>
    public static bool LooksSensitive(string text) => Sensitive.IsMatch(text);

    /// <summary>Restituisce il testo se e' sicuro, altrimenti <see cref="Withheld"/>. Null resta null.</summary>
    public static string? Clean(string? text)
    {
        if (text is null)
        {
            return null;
        }

        return LooksSensitive(text) ? Withheld : text;
    }
}
