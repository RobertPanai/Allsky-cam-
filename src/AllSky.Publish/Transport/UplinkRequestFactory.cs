using System.Net.Http.Headers;
using System.Text;

namespace AllSky.Publish;

/// <summary>
/// Richiesta HTTP del protocollo Uplink. Il <c>ToString</c> mostra solo metodo e URL: l'header Authorization
/// (e l'eventuale header extra) non compaiono mai, neppure in messaggi di eccezione o nei log di HttpClient.
/// </summary>
internal sealed class UplinkHttpRequest : HttpRequestMessage
{
    public UplinkHttpRequest(HttpMethod method, Uri uri) : base(method, uri) { }

    /// <summary>Solo metodo e URL: nessun header, nessun contenuto.</summary>
    public override string ToString() => $"{Method} {RequestUri}";
}

/// <summary>
/// Costruisce le richieste del protocollo Uplink: invio del frame (POST multipart) e verifica della connessione (GET).
/// Non imposta mai DefaultRequestHeaders condivisi: ogni richiesta porta i propri header.
/// </summary>
public static class UplinkRequestFactory
{
    /// <summary>Percorso relativo dell'invio di un frame.</summary>
    public const string AllSkyPath = "api/uplink/allsky";

    /// <summary>
    /// Percorso relativo della verifica di connessione. ATTENZIONE: e' un'ipotesi, da confermare con il server;
    /// va cambiato qui se il server usa un altro endpoint.
    /// </summary>
    public const string HealthPath = "api/uplink/health";

    /// <summary>Nome della parte immagine nel multipart.</summary>
    public const string ImageFileName = "frame.jpg";

    /// <summary>Header che l'header extra non puo' sovrascrivere (gestiti dal client).</summary>
    private static readonly string[] ReservedHeaderNames =
    {
        "Authorization", "Content-Type", "Content-Length", "Host", "Transfer-Encoding",
    };

    /// <summary>
    /// Crea la POST multipart verso {ServerUrl}/api/uplink/allsky con le parti "meta" (application/json) e "image" (image/jpeg).
    /// Il ProfileName della parte meta e' quello delle impostazioni; <paramref name="deviceProfileName"/> e' usato solo
    /// se le impostazioni non hanno un nome profilo (scelta di interpretazione: le impostazioni hanno la precedenza).
    /// Lancia eccezioni se l'URL o le impostazioni non sono utilizzabili: il chiamante le trasforma in esito.
    /// </summary>
    public static HttpRequestMessage Create(QueuedItem item, byte[] jpeg, PublishSettings s, string? deviceProfileName = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(jpeg);
        ArgumentNullException.ThrowIfNull(s);

        string? profileName = string.IsNullOrWhiteSpace(s.ProfileName) ? deviceProfileName : s.ProfileName;
        var meta = new UplinkMeta(
            PublishInfo.SchemaVersion,
            s.DeviceId,
            profileName,
            item.CaptureId,
            item.CapturedAtUtc,
            item.ExposureMs,
            item.Gain,
            item.Mode,
            item.TemperatureC);
        string metaJson = UplinkJson.SerializeMeta(meta);

        var request = new UplinkHttpRequest(HttpMethod.Post, BuildUri(s.ServerUrl, AllSkyPath));
        ApplyCredentials(request, s);

        var metaContent = new StringContent(metaJson, Encoding.UTF8, "application/json");

        var imageContent = new ByteArrayContent(jpeg);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        var multipart = new MultipartFormDataContent();
        multipart.Add(metaContent, "meta");
        multipart.Add(imageContent, "image", ImageFileName);
        request.Content = multipart;

        return request;
    }

    /// <summary>Crea la GET di verifica connessione verso {ServerUrl}/api/uplink/health (endpoint ipotetico, vedi <see cref="HealthPath"/>).</summary>
    public static HttpRequestMessage CreateHealth(PublishSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var request = new UplinkHttpRequest(HttpMethod.Get, BuildUri(s.ServerUrl, HealthPath));
        ApplyCredentials(request, s);
        return request;
    }

    /// <summary>
    /// Unisce base e percorso senza doppie barre, usando <see cref="Uri"/>; un eventuale percorso nel ServerUrl viene conservato.
    /// Accetta solo URL assoluti http o https: senza questo controllo un URL vuoto diventerebbe un file locale.
    /// Lancia <see cref="ArgumentException"/> se l'URL non e' utilizzabile.
    /// </summary>
    internal static Uri BuildUri(string serverUrl, string relativePath)
    {
        string root = (serverUrl ?? string.Empty).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(root, UriKind.Absolute, out Uri? baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("URL del server non valido: serve un indirizzo http o https.", nameof(serverUrl));
        }
        return new Uri(baseUri, relativePath);
    }

    /// <summary>Header di autenticazione (Bearer) e header extra facoltativo, sulla sola richiesta indicata.</summary>
    internal static void ApplyCredentials(HttpRequestMessage request, PublishSettings s)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Token.Reveal());

        string? name = s.ExtraHeaderName?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        if (ReservedHeaderNames.Any(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase)))
            return;

        string value = s.ExtraHeaderValue.Reveal();
        if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            return;

        request.Headers.TryAddWithoutValidation(name, value);
    }
}
