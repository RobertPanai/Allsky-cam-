namespace AllSky.Core.YouTube;

/// <summary>Link ai video caricati. Accetta solo id con caratteri previsti: l'id finisce in un link aperto nel browser.</summary>
public static class YouTubeLinks
{
    /// <summary>Prefisso del link di riproduzione.</summary>
    public const string WatchUrlPrefix = "https://www.youtube.com/watch?v=";

    /// <summary>Vero se l'id contiene solo lettere, cifre, "-" e "_" (lunghezza da 1 a 32).</summary>
    public static bool IsValidVideoId(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= 32
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>Link del video, o null se l'id non è valido.</summary>
    public static string? WatchUrl(string? videoId) => IsValidVideoId(videoId) ? WatchUrlPrefix + videoId : null;
}
