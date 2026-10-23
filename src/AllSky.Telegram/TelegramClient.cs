using System.Globalization;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AllSky.Publish;

namespace AllSky.Telegram;

/// <summary>
/// Client HTTP della Bot API di Telegram: solo richieste in uscita, nessun listener, nessun getUpdates.
/// Usa un <see cref="HttpClient"/> iniettato (nei test un gestore finto). Il token entra solo nel percorso della richiesta
/// e non compare in messaggi, eccezioni o ToString.
/// Ogni metodo valida i dati prima di inviare: se non rispettano i limiti restituisce l'errore senza chiamare la rete.
/// Non ritenta: i ritentativi sono di <see cref="SendQueue"/>. Errori di rete e di Telegram diventano <see cref="TelegramResult"/>;
/// l'unica eccezione che esce e' <see cref="OperationCanceledException"/> quando annulla il chiamante.
/// </summary>
public sealed class TelegramClient
{
    /// <summary>Indirizzo pubblico della Bot API.</summary>
    public static readonly Uri DefaultBaseUrl = new("https://api.telegram.org");

    /// <summary>Timeout di ogni richiesta (scelta locale, pensata per il caricamento di file fino a 50 MB).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Quanto del corpo della risposta viene letto al massimo: le risposte di Telegram sono brevi.</summary>
    private const int MaxResponseBytes = 64 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _baseUrl;
    private readonly Secret _botToken;
    private readonly TimeSpan _timeout;

    /// <param name="http">Client HTTP riusato per tutte le richieste.</param>
    /// <param name="botToken">Token del bot. Vuoto e' ammesso: ogni chiamata risponde Unauthorized senza rete.</param>
    /// <param name="baseUrl">Indirizzo base (default https://api.telegram.org). Deve essere https, oppure http solo in locale.</param>
    /// <param name="timeout">Timeout per richiesta (default 5 minuti).</param>
    public TelegramClient(HttpClient http, Secret botToken, Uri? baseUrl = null, TimeSpan? timeout = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _botToken = botToken;
        _baseUrl = ValidateBaseUrl(baseUrl ?? DefaultBaseUrl);

        TimeSpan effective = timeout ?? DefaultTimeout;
        if (effective <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), effective, "Il timeout deve essere positivo.");
        _timeout = effective;
    }

    public override string ToString() => "TelegramClient";

    public Task<TelegramResult> SendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        TelegramResult? refusal = TelegramLimits.CheckChatId(chatId) ?? TelegramLimits.CheckText(text);
        if (refusal is not null)
            return Task.FromResult(refusal);

        return SendRequestAsync(HttpMethod.Post, "sendMessage",
            JsonBody(new Dictionary<string, object?> { ["chat_id"] = chatId, ["text"] = text }),
            "Messaggio inviato.", ct);
    }

    public Task<TelegramResult> SendPhotoAsync(long chatId, byte[] photo, string fileName, string? caption,
        int? width = null, int? height = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(photo);
        TelegramResult? refusal = CheckUpload(chatId, photo, fileName, caption, "La foto", TelegramLimits.PhotoMaxBytes)
            ?? TelegramLimits.CheckPhotoDimensions(width, height);
        if (refusal is not null)
            return Task.FromResult(refusal);

        return SendRequestAsync(HttpMethod.Post, "sendPhoto",
            Multipart("photo", chatId, photo, fileName, caption),
            "Foto inviata.", ct);
    }

    public Task<TelegramResult> SendVideoAsync(long chatId, byte[] video, string fileName, string? caption,
        bool supportsStreaming = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(video);
        TelegramResult? refusal = CheckUpload(chatId, video, fileName, caption, "Il video", TelegramLimits.FileMaxBytes);
        if (refusal is not null)
            return Task.FromResult(refusal);

        return SendRequestAsync(HttpMethod.Post, "sendVideo",
            Multipart("video", chatId, video, fileName, caption,
                ("supports_streaming", supportsStreaming ? "true" : "false")),
            "Video inviato.", ct);
    }

    public Task<TelegramResult> SendVideoAsync(long chatId, Stream video, string fileName, string? caption,
        bool supportsStreaming = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(video);
        TelegramResult? read = ReadBounded(video, TelegramLimits.FileMaxBytes, "Il video", out byte[] bytes);
        if (read is not null)
            return Task.FromResult(read);

        return SendVideoAsync(chatId, bytes, fileName, caption, supportsStreaming, ct);
    }

    public Task<TelegramResult> SendDocumentAsync(long chatId, byte[] document, string fileName, string? caption,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        TelegramResult? refusal = CheckUpload(chatId, document, fileName, caption, "Il documento", TelegramLimits.FileMaxBytes);
        if (refusal is not null)
            return Task.FromResult(refusal);

        return SendRequestAsync(HttpMethod.Post, "sendDocument",
            Multipart("document", chatId, document, fileName, caption),
            "Documento inviato.", ct);
    }

    public Task<TelegramResult> SendDocumentAsync(long chatId, Stream document, string fileName, string? caption,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        TelegramResult? read = ReadBounded(document, TelegramLimits.FileMaxBytes, "Il documento", out byte[] bytes);
        if (read is not null)
            return Task.FromResult(read);

        return SendDocumentAsync(chatId, bytes, fileName, caption, ct);
    }

    /// <summary>Verifica il token con getMe (prova di invio). Nessun messaggio viene inviato.</summary>
    public Task<TelegramResult> GetMeAsync(CancellationToken ct = default)
        => SendRequestAsync(HttpMethod.Get, "getMe", null, "Connessione a Telegram riuscita.", ct);

    private static TelegramResult? CheckUpload(long chatId, byte[] bytes, string fileName, string? caption, string what, long maxBytes)
        => TelegramLimits.CheckChatId(chatId)
           ?? TelegramLimits.CheckCaption(caption)
           ?? TelegramLimits.CheckFileName(fileName)
           ?? TelegramLimits.CheckSize(bytes.Length, maxBytes, what);

    /// <summary>
    /// Legge un flusso riavvolgibile con lunghezza nota, senza superare <paramref name="maxBytes"/>: il controllo
    /// della dimensione avviene prima di leggere il contenuto.
    /// </summary>
    internal static TelegramResult? ReadBounded(Stream stream, long maxBytes, string what, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!stream.CanRead || !stream.CanSeek)
            return TelegramLimits.Invalid("Il flusso del file deve essere leggibile e avere una lunghezza nota.");

        long remaining = stream.Length - stream.Position;
        TelegramResult? size = TelegramLimits.CheckSize(remaining, maxBytes, what);
        if (size is not null)
            return size;

        bytes = new byte[remaining];
        stream.ReadExactly(bytes);
        return null;
    }

    /// <summary>
    /// Esegue una richiesta. Il token viene letto solo qui, nel percorso. Ogni eccezione di rete viene trasformata in
    /// Network con un messaggio fisso (le eccezioni di HttpClient contengono l'URL, quindi non vengono riportate).
    /// </summary>
    private async Task<TelegramResult> SendRequestAsync(HttpMethod httpMethod, string method, Func<HttpContent>? content,
        string successMessage, CancellationToken ct)
    {
        string token = _botToken.Reveal();
        if (token.Length == 0)
            return TelegramResult.Failure(TelegramErrorKind.Unauthorized, "Il token del bot non è configurato.");
        if (!IsWellFormedToken(token))
            return TelegramResult.Failure(TelegramErrorKind.Unauthorized, "Il token del bot ha un formato non valido: controllalo nelle impostazioni.");

        try
        {
            var uri = new Uri(_baseUrl, "/bot" + token + "/" + method);
            using var request = new HttpRequestMessage(httpMethod, uri);
            if (content is not null)
                request.Content = content();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_timeout);

            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            string? body = await ReadBodyAsync(response.Content, timeoutCts.Token).ConfigureAwait(false);
            return Map((int)response.StatusCode, body, successMessage);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return TelegramResult.Failure(TelegramErrorKind.Network, "Telegram non ha risposto in tempo: riprovo più tardi.");
        }
        catch (Exception)
        {
            return TelegramResult.Failure(TelegramErrorKind.Network, "Connessione a Telegram non riuscita: controlla la rete.");
        }
    }

    /// <summary>Traduce il codice HTTP e il corpo in un esito. Il corpo grezzo di Telegram non viene mai riportato.</summary>
    internal static TelegramResult Map(int status, string? body, string successMessage)
    {
        ParseResponse(body, out bool ok, out int? retryAfter);

        if (status is >= 200 and <= 299)
        {
            return ok
                ? TelegramResult.Success(successMessage)
                : TelegramResult.Failure(TelegramErrorKind.Network, "Risposta di Telegram non leggibile: riprovo più tardi.");
        }

        return status switch
        {
            401 => TelegramResult.Failure(TelegramErrorKind.Unauthorized, "Telegram ha rifiutato il token del bot: controllalo nelle impostazioni."),
            // [NV] Il significato del 404 non e' letto nella documentazione: in pratica puo' indicare un token sbagliato.
            404 => TelegramResult.Failure(TelegramErrorKind.Rejected, "Telegram non ha riconosciuto la richiesta: controlla il token del bot."),
            400 or 403 => TelegramResult.Failure(TelegramErrorKind.Rejected, "Telegram ha rifiutato la richiesta: controlla la chat e i permessi del bot."),
            413 => TelegramResult.Failure(TelegramErrorKind.TooLarge, "Telegram ha rifiutato il file perché troppo grande."),
            429 => TelegramResult.Failure(TelegramErrorKind.RateLimited, "Telegram chiede di rallentare l'invio.", retryAfter),
            >= 500 and <= 599 => TelegramResult.Failure(TelegramErrorKind.Network, "Telegram è temporaneamente non disponibile: riprovo più tardi."),
            _ => TelegramResult.Failure(TelegramErrorKind.Rejected, $"Telegram ha rifiutato la richiesta (codice {status})."),
        };
    }

    /// <summary>
    /// Legge <c>ok</c> e <c>parameters.retry_after</c> dal corpo JSON. Un corpo non JSON non lancia: ok=false, retry null.
    /// </summary>
    private static void ParseResponse(string? body, out bool ok, out int? retryAfter)
    {
        ok = false;
        retryAfter = null;
        if (string.IsNullOrWhiteSpace(body))
            return;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;

            ok = root.TryGetProperty("ok", out JsonElement okElement) && okElement.ValueKind == JsonValueKind.True;

            if (root.TryGetProperty("parameters", out JsonElement parameters)
                && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("retry_after", out JsonElement retry)
                && retry.TryGetInt32(out int seconds)
                && seconds >= 0)
            {
                retryAfter = seconds;
            }
        }
        catch (JsonException)
        {
            ok = false;
            retryAfter = null;
        }
    }

    private static async Task<string?> ReadBodyAsync(HttpContent content, CancellationToken ct)
    {
        try
        {
            await using Stream stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var buffer = new byte[MaxResponseBytes];
            int total = 0;
            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                total += read;
            }
            return Encoding.UTF8.GetString(buffer, 0, total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static Func<HttpContent> JsonBody(IDictionary<string, object?> payload)
        => () => new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    /// <summary>Corpo multipart/form-data: chat_id, didascalia facoltativa, campi extra e il file.</summary>
    private static Func<HttpContent> Multipart(string fileField, long chatId, byte[] bytes, string fileName, string? caption,
        params (string Name, string Value)[] extra)
        => () =>
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(chatId.ToString(CultureInfo.InvariantCulture)), "chat_id");
            if (!string.IsNullOrEmpty(caption))
                form.Add(new StringContent(caption), "caption");
            foreach ((string name, string value) in extra)
                form.Add(new StringContent(value), name);

            string safeName = TelegramLimits.NormalizeFileName(fileName) ?? "file";
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeFor(safeName));
            form.Add(file, fileField, safeName);
            return form;
        };

    private static string MediaTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };

    /// <summary>Il token entra nel percorso dell'URL: solo caratteri sicuri, niente barre, spazi o caratteri speciali.</summary>
    private static bool IsWellFormedToken(string token)
    {
        foreach (char c in token)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is ':' or '_' or '-'))
                return false;
        }
        return true;
    }

    private static Uri ValidateBaseUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri)
            throw new ArgumentException("L'indirizzo dell'API deve essere assoluto.", nameof(url));
        bool httpsOk = url.Scheme == Uri.UriSchemeHttps;
        bool httpLocal = url.Scheme == Uri.UriSchemeHttp && url.IsLoopback;
        if (!httpsOk && !httpLocal)
            throw new ArgumentException("L'indirizzo dell'API deve usare https (http solo in locale).", nameof(url));
        if (url.AbsolutePath != "/" || url.Query.Length > 0 || url.Fragment.Length > 0 || url.UserInfo.Length > 0)
            throw new ArgumentException("L'indirizzo dell'API deve essere solo origine, senza percorso, query o credenziali.", nameof(url));
        return new Uri(url.GetLeftPart(UriPartial.Authority) + "/");
    }
}
