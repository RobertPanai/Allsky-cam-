namespace AllSky.YouTube;

/// <summary>
/// Limiti dei campi di snippet di una risorsa video di YouTube Data API v3.
/// Fonte: https://developers.google.com/youtube/v3/docs/videos (risorsa video, snippet.title e snippet.description).
/// Verificati il 2026-10-21; la pagina riporta come ultimo aggiornamento il 2026-10-08 UTC.
/// - snippet.title: "maximum length of 100 characters".
/// - snippet.description: "maximum length of 5000 bytes".
/// - Titolo e descrizione non ammettono '&lt;' e '&gt;' (la pagina lo indica per entrambi i campi).
/// La pagina dice "characters" senza precisare se si conta per code point o per unità UTF-16:
/// il codice conta in unità UTF-16 (string.Length), cioè in modo prudenziale.
/// </summary>
public static class YouTubeLimits
{
    /// <summary>Lunghezza massima del titolo, in caratteri (unità UTF-16).</summary>
    public const int TitleMaxLength = 100;

    /// <summary>Lunghezza massima della descrizione, in byte UTF-8.</summary>
    public const int DescriptionMaxBytes = 5000;
}
