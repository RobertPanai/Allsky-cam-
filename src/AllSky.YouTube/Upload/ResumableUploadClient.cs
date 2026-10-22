using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AllSky.YouTube;

/// <summary>
/// Client del protocollo di upload resumable di YouTube Data API v3 (fonte: guida ufficiale "Resumable upload", letta il 2026-10-22).
/// Passi: POST di inizializzazione con uploadType=resumable (la URI di sessione arriva nell'header Location),
/// PUT dei chunk con Content-Range "bytes first-last/total", PUT vuoto con "bytes */total" per sapere da dove riprendere.
/// Risposte: 308 = caricamento incompleto (Range indica i byte ricevuti), 200/201 = completato, altro = errore.
/// Un 401 fa rinnovare il token una volta e ripete la richiesta. Non fa retry: quello e' in <see cref="NightUploader"/>.
/// Nessun log: messaggi d'errore solo con stato e motivo.
/// </summary>
public sealed class ResumableUploadClient
{
    public const string UploadEndpoint = "https://www.googleapis.com/upload/youtube/v3/videos";

    /// <summary>Parti scritte e lette: snippet (titolo, descrizione, tag, categoria) e status (privacy).</summary>
    public const string Part = "snippet,status";

    public const string VideoContentType = "video/mp4";

    /// <summary>La guida richiede chunk multipli di 256 KB (tranne l'ultimo).</summary>
    public const int ChunkGranularityBytes = 256 * 1024;

    /// <summary>8 MiB per chunk: 32 multipli da 256 KB.</summary>
    public const int DefaultChunkBytes = 32 * ChunkGranularityBytes;

    private readonly HttpClient _http;
    private readonly IAccessTokenSource _tokens;

    public ResumableUploadClient(HttpClient http, IAccessTokenSource tokens, int chunkBytes = DefaultChunkBytes)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        if (chunkBytes <= 0 || chunkBytes % ChunkGranularityBytes != 0)
            throw new ArgumentOutOfRangeException(nameof(chunkBytes), chunkBytes, "La dimensione del chunk deve essere un multiplo positivo di 256 KB.");
        ChunkBytes = chunkBytes;
    }

    /// <summary>Dimensione di ogni chunk (l'ultimo puo' essere piu' corto).</summary>
    public int ChunkBytes { get; }

    /// <summary>Apre una sessione resumable. Nessun byte del video viene ancora inviato.</summary>
    public async Task<Uri> StartSessionAsync(VideoMetadata metadata, long contentLength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (contentLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(contentLength), contentLength, "Il file da caricare e' vuoto.");

        byte[] body = JsonSerializer.SerializeToUtf8Bytes(VideoResource(metadata));
        using HttpResponseMessage response = await SendAuthorizedAsync(token =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, UploadEndpoint + "?uploadType=resumable&part=" + Part);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Reveal());
            request.Headers.TryAddWithoutValidation("X-Upload-Content-Length", contentLength.ToString(CultureInfo.InvariantCulture));
            request.Headers.TryAddWithoutValidation("X-Upload-Content-Type", VideoContentType);
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "UTF-8" };
            return request;
        }, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw await ErrorAsync(response, cancellationToken).ConfigureAwait(false);

        Uri? location = response.Headers.Location;
        if (location is null || !location.IsAbsoluteUri)
            throw new YouTubeApiException((int)response.StatusCode, string.Empty, "YouTube non ha restituito la URI di sessione del caricamento.");
        return location;
    }

    /// <summary>Chiede a Google fino a dove ha ricevuto i byte (PUT vuoto con "bytes */total"). Usato per riprendere dopo un'interruzione.</summary>
    public Task<UploadProgress> QueryAsync(Uri session, long totalLength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        var range = new ContentRangeHeaderValue(totalLength);
        return SendPutAsync(session, Array.Empty<byte>(), range, totalLength, cancellationToken);
    }

    /// <summary>Invia un chunk che inizia a <paramref name="offset"/>. Il chunk deve essere letto dal file prima della chiamata.</summary>
    public Task<UploadProgress> SendChunkAsync(Uri session, byte[] chunk, long offset, long totalLength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Length == 0)
            throw new ArgumentException("Il chunk e' vuoto.", nameof(chunk));
        if (offset < 0 || offset + chunk.Length > totalLength)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Chunk fuori dai limiti del file.");

        var range = new ContentRangeHeaderValue(offset, offset + chunk.Length - 1, totalLength);
        return SendPutAsync(session, chunk, range, totalLength, cancellationToken);
    }

    private async Task<UploadProgress> SendPutAsync(
        Uri session,
        byte[] payload,
        ContentRangeHeaderValue range,
        long totalLength,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendAuthorizedAsync(token =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, session);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Reveal());
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(VideoContentType);
            request.Content.Headers.ContentRange = range;
            return request;
        }, cancellationToken).ConfigureAwait(false);

        int status = (int)response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            string text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string? videoId = ReadVideoId(text);
            if (string.IsNullOrEmpty(videoId))
                throw new YouTubeApiException(status, string.Empty, "YouTube ha completato il caricamento senza restituire un id video.");
            return new UploadProgress(totalLength, videoId, null);
        }

        if (status == 308)
        {
            long next = ParseNextOffset(response.Headers, totalLength);
            TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
            return new UploadProgress(next, null, retryAfter);
        }

        throw await ErrorAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Invia la richiesta costruita da <paramref name="build"/>. Su 401 rinnova il token una sola volta e riprova.
    /// Il chiamante deve disporre la risposta.
    /// </summary>
    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<OAuthSecret, HttpRequestMessage> build,
        CancellationToken cancellationToken)
    {
        OAuthSecret token = await _tokens.GetAccessTokenAsync(false, cancellationToken).ConfigureAwait(false);
        HttpResponseMessage response = await SendOnceAsync(build, token, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        token = await _tokens.GetAccessTokenAsync(true, cancellationToken).ConfigureAwait(false);
        return await SendOnceAsync(build, token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        Func<OAuthSecret, HttpRequestMessage> build,
        OAuthSecret token,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = build(token);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Byte ricevuti secondo l'header Range ("bytes=0-N" → N+1). Senza Range non e' stato ricevuto nulla.
    /// </summary>
    internal static long ParseNextOffset(HttpResponseHeaders headers, long totalLength)
    {
        if (!headers.TryGetValues("Range", out IEnumerable<string>? values))
            return 0;

        string? value = values.FirstOrDefault();
        const string Prefix = "bytes=0-";
        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal)
            || !long.TryParse(value.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long last)
            || last < 0)
            throw new YouTubeApiException(308, "invalid_range", "YouTube ha restituito un intervallo di ripresa non valido.");

        long next = last + 1;
        if (next > totalLength)
            throw new YouTubeApiException(308, "invalid_range", "YouTube dichiara più byte di quelli del file.");
        return next;
    }

    internal static string? ReadVideoId(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out JsonElement id)
                && id.ValueKind == JsonValueKind.String)
                return id.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<YouTubeApiException> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        int status = (int)response.StatusCode;
        string reason = YouTubeApiException.ReadReason(body);
        string message = reason.Length == 0
            ? $"YouTube ha risposto HTTP {status}."
            : $"YouTube ha risposto HTTP {status} ({reason}).";
        return new YouTubeApiException(status, reason, message);
    }

    /// <summary>Risorsa <c>video</c> inviata all'inizializzazione. selfDeclaredMadeForKids e' sempre false.</summary>
    private static object VideoResource(VideoMetadata metadata) => new
    {
        snippet = new
        {
            title = metadata.Title,
            description = metadata.Description,
            tags = metadata.Tags,
            categoryId = metadata.CategoryId,
        },
        status = new
        {
            privacyStatus = metadata.Privacy.ToApiValue(),
            selfDeclaredMadeForKids = false,
        },
    };
}
