using System.Text.RegularExpressions;

namespace AllSky.Publish;

/// <summary>
/// Validazione delle impostazioni di pubblicazione.
/// Regole: con Enabled=false si controllano solo i campi valorizzati; con Enabled=true sono obbligatori
/// serverUrl, deviceId e token. I campi numerici hanno sempre un valore, quindi vengono controllati sempre.
/// I messaggi sono in italiano e non contengono mai il token, il valore dell'header extra o l'indirizzo del server:
/// indicano solo il campo (path camelCase) e cosa correggere.
/// Scelta: un carattere di controllo o uno spazio nell'indirizzo del server viene rifiutato prima del parsing.
/// Scelta: nel token e nel valore dell'header sono rifiutati i caratteri di controllo (non sono ammessi negli header HTTP).
/// </summary>
public static class SettingsValidator
{
    /// <summary>Lunghezza massima dell'indirizzo del server.</summary>
    public const int MaxServerUrlLength = 300;

    private static readonly Regex DeviceIdPattern = new(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex HeaderNamePattern = new(@"^[A-Za-z0-9-]{1,64}\z", RegexOptions.CultureInvariant);
    private static readonly string[] ReservedHeaders = { "Authorization", "Host", "Content-Type", "Content-Length" };
    private static readonly string[] LocalHosts = { "localhost", "127.0.0.1", "::1" };

    /// <summary>Valida le impostazioni e restituisce l'elenco degli errori (vuoto se valide).</summary>
    /// <param name="s">Impostazioni da validare.</param>
    public static IReadOnlyList<SettingsError> Validate(PublishSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var errors = new List<SettingsError>();

        ValidateServerUrl(s, errors);
        ValidateDeviceId(s, errors);
        ValidateToken(s, errors);
        ValidateProfileName(s, errors);

        AddIfOutOfRange(errors, "intervalSeconds", s.IntervalSeconds, 15, 86400,
            "L'intervallo tra gli invii deve essere compreso tra 15 e 86400 secondi.");
        AddIfOutOfRange(errors, "longEdgePx", s.LongEdgePx, 200, 2000,
            "Il lato lungo dell'immagine deve essere compreso tra 200 e 2000 pixel.");
        AddIfOutOfRange(errors, "jpegQuality", s.JpegQuality, 30, 95,
            "La qualita' JPEG deve essere compresa tra 30 e 95.");

        ValidateExtraHeader(s, errors);

        return errors;
    }

    private static void ValidateServerUrl(PublishSettings s, List<SettingsError> errors)
    {
        string url = s.ServerUrl ?? string.Empty;
        if (url.Length == 0)
        {
            if (s.Enabled)
                Add(errors, "serverUrl", "Inserisci l'indirizzo del server, ad esempio https://nome-del-server.");
            return;
        }

        if (url.Length > MaxServerUrlLength)
        {
            Add(errors, "serverUrl", "L'indirizzo del server supera i 300 caratteri. Controlla di aver incollato solo l'indirizzo base.");
            return;
        }

        if (url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            Add(errors, "serverUrl", "L'indirizzo del server contiene spazi o caratteri non validi. Copialo di nuovo, senza spazi.");
            return;
        }

        if (url.Contains('?') || url.Contains('#'))
        {
            Add(errors, "serverUrl", "L'indirizzo del server non deve contenere parametri (?) o frammenti (#): un indirizzo con parametri puo' contenere un token. Inserisci solo l'indirizzo base.");
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            Add(errors, "serverUrl", "Indirizzo del server non valido: inserisci un indirizzo completo che inizi con https://.");
            return;
        }

        if (uri.UserInfo.Length > 0 || HasUserInfo(url))
        {
            Add(errors, "serverUrl", "L'indirizzo del server non deve contenere nome utente o password. Il token va inserito nel campo apposito.");
            return;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            Add(errors, "serverUrl", "Nell'indirizzo del server manca il nome del server.");
            return;
        }

        if (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsLocalHost(uri.Host))
                Add(errors, "serverUrl", "Con http sono ammessi solo localhost, 127.0.0.1 o ::1. Per un server remoto usa https://.");
            return;
        }

        Add(errors, "serverUrl", "Schema dell'indirizzo non ammesso: usa https://.");
    }

    private static void ValidateDeviceId(PublishSettings s, List<SettingsError> errors)
    {
        string id = s.DeviceId ?? string.Empty;
        if (id.Length == 0)
        {
            if (s.Enabled)
                Add(errors, "deviceId", "Inserisci l'identificativo del dispositivo: lettere, numeri, punto, trattino o underscore, massimo 64 caratteri.");
            return;
        }

        if (!DeviceIdPattern.IsMatch(id))
            Add(errors, "deviceId", "L'identificativo del dispositivo puo' contenere solo lettere, numeri, punto, trattino e underscore (massimo 64 caratteri).");
    }

    private static void ValidateToken(PublishSettings s, List<SettingsError> errors)
    {
        if (s.Token.IsEmpty)
        {
            if (s.Enabled)
                Add(errors, "token", "Inserisci il token fornito dal server.");
            return;
        }

        if (ContainsControl(s.Token.Reveal()))
            Add(errors, "token", "Il token contiene a capo o caratteri non validi. Copialo di nuovo dal server.");
    }

    private static void ValidateProfileName(PublishSettings s, List<SettingsError> errors)
    {
        string? name = s.ProfileName;
        if (string.IsNullOrEmpty(name))
            return;

        if (name.Length > 80)
            Add(errors, "profileName", "Il nome del profilo puo' avere al massimo 80 caratteri.");
        else if (name.Any(char.IsControl))
            Add(errors, "profileName", "Il nome del profilo non puo' contenere a capo o caratteri di controllo.");
    }

    private static void ValidateExtraHeader(PublishSettings s, List<SettingsError> errors)
    {
        string? name = s.ExtraHeaderName;
        bool hasName = !string.IsNullOrEmpty(name);
        bool hasValue = !s.ExtraHeaderValue.IsEmpty;

        if (hasName)
        {
            if (!HeaderNamePattern.IsMatch(name!))
                Add(errors, "extraHeaderName", "Il nome dell'header aggiuntivo puo' contenere solo lettere, numeri e trattini (massimo 64 caratteri).");
            else if (ReservedHeaders.Any(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase)))
                Add(errors, "extraHeaderName", "Questo nome di header e' riservato dal sistema: scegline un altro.");

            if (!hasValue)
                Add(errors, "extraHeaderValue", "Inserisci il valore dell'header aggiuntivo, oppure svuota anche il nome.");
        }
        else if (hasValue)
        {
            Add(errors, "extraHeaderName", "Inserisci anche il nome dell'header aggiuntivo, oppure svuota il valore.");
        }

        if (hasValue && ContainsControl(s.ExtraHeaderValue.Reveal()))
            Add(errors, "extraHeaderValue", "Il valore dell'header aggiuntivo contiene a capo o caratteri non validi.");
    }

    private static void AddIfOutOfRange(List<SettingsError> errors, string path, int value, int min, int max, string message)
    {
        if (value < min || value > max)
            Add(errors, path, message);
    }

    /// <summary>True se nella parte di autorita' dell'URL compare una '@' (credenziali incorporate).</summary>
    private static bool HasUserInfo(string url)
    {
        int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            return false;

        int authStart = schemeEnd + 3;
        int authEnd = url.IndexOf('/', authStart);
        string authority = authEnd < 0 ? url[authStart..] : url[authStart..authEnd];
        return authority.Contains('@');
    }

    private static bool IsLocalHost(string host)
    {
        string bare = host.Trim('[', ']');
        return LocalHosts.Any(h => string.Equals(h, bare, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsControl(string value) => value.Any(char.IsControl);

    private static void Add(List<SettingsError> errors, string path, string message)
        => errors.Add(new SettingsError(path, message));
}
