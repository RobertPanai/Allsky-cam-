using System.Text.RegularExpressions;
using AllSky.Core.LogView;

namespace AllSky.Nina;

/// <summary>
/// Pulizia dei messaggi prima di salvarli: segreti (<see cref="LogSecretMask"/>), percorsi di cartelle utente
/// (es. <c>C:\Users\nome</c>, <c>/home/nome</c>, <c>%localappdata%</c>) e indirizzi IP (IPv4 e IPv6 con "::").
/// I numeri di versione (es. 3.2.0.9001) non sono indirizzi e restano.
/// </summary>
public static class NinaMessageSanitizer
{
    /// <summary>Lunghezza massima del messaggio salvato.</summary>
    public const int MaxLength = 500;

    /// <summary>Sostituto di un percorso di cartella utente.</summary>
    public const string UserPathMask = "<percorso utente>";

    /// <summary>Sostituto di un indirizzo IP.</summary>
    public const string IpAddressMask = "<indirizzo IP>";

    /// <summary>Testo usato se la pulizia non riesce per timeout: meglio nascondere tutto.</summary>
    public const string UnreadableMask = "<messaggio oscurato>";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex WindowsUserPathRegex = new(
        @"(?i)\b[a-z]:[\\/]+users[\\/][^\s""'<>|]*", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex UnixUserPathRegex = new(
        @"(?i)(?<![\w.])/(?:home|users)/[^\s""'<>|]+", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex EnvUserPathRegex = new(
        @"(?i)%(?:localappdata|appdata|userprofile|homepath|homedrive)%[^\s""'<>|]*", RegexOptions.CultureInvariant, RegexTimeout);

    private const string Octet = @"(?:25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])";

    // Un IPv4 non preceduto da cifre o punto e non seguito da "cifra" o ".cifra" (evita 3.2.0.9001).
    private static readonly Regex IpV4Regex = new(
        $@"(?<![0-9.])(?:{Octet}\.){{3}}{Octet}(?!\.?[0-9])", RegexOptions.CultureInvariant, RegexTimeout);

    // IPv6 solo nella forma con "::" (altrimenti hh:mm:ss dei log verrebbe mascherato).
    private static readonly Regex IpV6Regex = new(
        @"(?<![\w:])(?=[0-9A-Fa-f:]*::)[0-9A-Fa-f:]{2,39}(?![\w:])", RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex SpacesRegex = new(@"\s+", RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>Restituisce il testo pulito, una riga, lungo al massimo <see cref="MaxLength"/>. Null diventa stringa vuota.</summary>
    public static string Clean(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        try
        {
            string value = LogSecretMask.Apply(message);
            value = WindowsUserPathRegex.Replace(value, UserPathMask);
            value = UnixUserPathRegex.Replace(value, UserPathMask);
            value = EnvUserPathRegex.Replace(value, UserPathMask);
            value = IpV4Regex.Replace(value, IpAddressMask);
            value = IpV6Regex.Replace(value, IpAddressMask);
            value = SpacesRegex.Replace(value, " ").Trim();
            return value.Length > MaxLength ? value[..(MaxLength - 3)] + "..." : value;
        }
        catch (RegexMatchTimeoutException)
        {
            return UnreadableMask;
        }
    }
}
