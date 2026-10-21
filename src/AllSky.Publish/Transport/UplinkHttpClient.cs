using AllSky.Core;

using System.Text;

namespace AllSky.Publish;

/// <summary>
/// Client HTTP del protocollo Uplink: invio dei frame (<see cref="IUplinkSender.SendAsync"/>) e verifica della connessione.
/// Usa un unico <see cref="HttpClient"/> per tutta la vita dell'oggetto e non imposta header condivisi: ogni richiesta
/// porta le proprie credenziali. Non lancia per errori di rete, timeout o risposte del server: li trasforma in
/// <see cref="SendResult"/> o <see cref="TestResult"/> sanificati. L'unica eccezione che puo' uscire e'
/// <see cref="OperationCanceledException"/>, quando il <c>CancellationToken</c> del chiamante e' annullato.
/// </summary>
public sealed class UplinkHttpClient : IUplinkSender
{
    /// <summary>Quanto del corpo della risposta viene letto al massimo.</summary>
    public const int MaxBodyBytes = 4096;

    /// <summary>Timeout di default di ogni richiesta.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly IClock _clock;
    private readonly Redactor _redactor;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Crea il client. <paramref name="http"/> viene riusato per tutti gli invii (non ricreare per ogni richiesta).
    /// <paramref name="timeout"/> limita ogni singola richiesta (default 30 s, deve essere positivo).
    /// </summary>
    public UplinkHttpClient(HttpClient http, IClock clock, Redactor redactor, TimeSpan? timeout = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));

        TimeSpan effective = timeout ?? DefaultTimeout;
        if (effective <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), effective, "Il timeout deve essere positivo.");
        _timeout = effective;
    }

    /// <summary>
    /// Invia un frame. Timeout e errori di rete diventano Transient 'timeout' / 'network'; la risposta viene
    /// classificata da <see cref="ErrorClassifier"/> leggendo al massimo 4 KB di corpo.
    /// </summary>
    public async Task<SendResult> SendAsync(QueuedItem item, byte[] jpeg, PublishSettings settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        HttpRequestMessage? request = BuildRequestOrNull(() => UplinkRequestFactory.Create(item, jpeg, settings));
        if (request is null)
            return ErrorClassifier.ClassifyInvalidRequest(_redactor);

        using (request)
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(_timeout);
            try
            {
                using HttpResponseMessage response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    .ConfigureAwait(false);

                string? body = await ReadPrefixAsync(response.Content, ct, cts.Token).ConfigureAwait(false);
                return ErrorClassifier.Classify(
                    (int)response.StatusCode,
                    body,
                    RetryAfterOf(response),
                    _clock,
                    _redactor);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Scattato il timeout della richiesta (non il token del chiamante).
                return ErrorClassifier.ClassifyTimeout(_redactor);
            }
            catch (Exception)
            {
                // Le eccezioni di rete (HttpRequestException, IOException, ...) non vengono mai propagate:
                // il messaggio originale puo' contenere URL con query o token, quindi non viene riportato.
                return ErrorClassifier.ClassifyNetworkError(_redactor);
            }
        }
    }

    /// <summary>
    /// Verifica la connessione con GET {ServerUrl}/api/uplink/health, stesso Bearer e header extra, stesso timeout.
    /// NB: l'endpoint health e' un'ipotesi da confermare con il server (vedi <see cref="UplinkRequestFactory.HealthPath"/>).
    /// Mappa: 2xx -> ok; 401/403 -> auth; 404/405 -> no_health_endpoint; altro codice -> server_error;
    /// timeout -> timeout; errore di rete -> network.
    /// </summary>
    public async Task<TestResult> TestConnectionAsync(PublishSettings settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        HttpRequestMessage? request = BuildRequestOrNull(() => UplinkRequestFactory.CreateHealth(settings));
        if (request is null)
            return Test(false, "invalid_settings", "Indirizzo del server non valido: controlla le impostazioni di pubblicazione.", null);

        using (request)
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(_timeout);
            try
            {
                using HttpResponseMessage response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    .ConfigureAwait(false);

                int code = (int)response.StatusCode;
                if (code is >= 200 and <= 299)
                    return Test(true, "ok", "Connessione riuscita", code);
                if (code is 401 or 403)
                    return Test(false, "auth", "Token o ID dispositivo rifiutati", code);
                if (code is 404 or 405)
                    return Test(false, "no_health_endpoint",
                        "Il server non espone la verifica di connessione: prova ad attivare la pubblicazione e controlla lo stato", code);
                return Test(false, "server_error",
                    $"Il server ha risposto con un errore (codice {code}): riprova più tardi", code);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return Test(false, "timeout", "Il server non ha risposto in tempo", null);
            }
            catch (Exception)
            {
                return Test(false, "network", "Connessione al server non riuscita: controlla indirizzo e rete", null);
            }
        }
    }

    /// <summary>Costruisce la richiesta; null se l'URL o le impostazioni non sono utilizzabili.</summary>
    private static HttpRequestMessage? BuildRequestOrNull(Func<HttpRequestMessage> factory)
    {
        try
        {
            return factory();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Legge al massimo <see cref="MaxBodyBytes"/> byte del corpo. Se la lettura fallisce (connessione caduta, timeout
    /// durante il corpo) restituisce null: l'esito si decide dal solo codice di stato. Il token del chiamante,
    /// se annullato, viene rilanciato.
    /// </summary>
    private async Task<string?> ReadPrefixAsync(HttpContent content, CancellationToken callerCt, CancellationToken linkedCt)
    {
        try
        {
            await using Stream stream = await content.ReadAsStreamAsync(linkedCt).ConfigureAwait(false);
            var buffer = new byte[MaxBodyBytes];
            int total = 0;
            while (total < MaxBodyBytes)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total, MaxBodyBytes - total), linkedCt).ConfigureAwait(false);
                if (read == 0)
                    break;
                total += read;
            }
            return Encoding.UTF8.GetString(buffer, 0, total);
        }
        catch (OperationCanceledException) when (callerCt.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Valore grezzo di Retry-After dalla risposta, se presente (il parsing e' in <see cref="ErrorClassifier"/>).</summary>
    private static string? RetryAfterOf(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string>? values))
            return values.FirstOrDefault();
        return null;
    }

    private TestResult Test(bool ok, string code, string message, int? statusCode)
        => new(ok, code, _redactor.Sanitize(message), statusCode);
}
