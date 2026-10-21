using System.Net;

namespace AllSky.Publish.Tests;

/// <summary>
/// HttpMessageHandler finto: registra la richiesta (metodo, URL, header, corpo, ToString) e risponde con una funzione del test.
/// Il corpo della richiesta viene letto subito, perche' <see cref="UplinkHttpClient"/> dispone la richiesta dopo l'invio.
/// </summary>
public sealed class UplinkTestHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    public UplinkTestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));
    }

    /// <summary>Numero di richieste ricevute.</summary>
    public int Calls { get; private set; }

    /// <summary>Metodo dell'ultima richiesta.</summary>
    public HttpMethod? Method { get; private set; }

    /// <summary>URL dell'ultima richiesta.</summary>
    public Uri? RequestUri { get; private set; }

    /// <summary>Risultato di ToString() della richiesta, catturato al ricevimento.</summary>
    public string? RequestText { get; private set; }

    /// <summary>Corpo dell'ultima richiesta (null se assente).</summary>
    public byte[]? Body { get; private set; }

    /// <summary>Content-Type del corpo dell'ultima richiesta.</summary>
    public string? ContentType { get; private set; }

    /// <summary>Valore di un header dell'ultima richiesta (valori multipli uniti da virgola), o null.</summary>
    public string? Header(string name) => _headers.TryGetValue(name, out string? value) ? value : null;

    /// <summary>Risponde sempre con lo stato indicato, corpo e Retry-After facoltativi.</summary>
    public static UplinkTestHandler Reply(int status, string body = "", string? retryAfter = null)
        => new((_, _) => Task.FromResult(Build(status, body, retryAfter)));

    /// <summary>Non risponde mai: l'attesa termina solo con l'annullamento del token ricevuto.</summary>
    public static UplinkTestHandler Hang()
        => new(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return Build(200, string.Empty, null);
        });

    /// <summary>Solleva l'eccezione indicata invece di rispondere.</summary>
    public static UplinkTestHandler Fail(Exception exception)
        => new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    /// <summary>Costruisce una risposta di test.</summary>
    public static HttpResponseMessage Build(int status, string body, string? retryAfter)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        if (retryAfter is not null)
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Method = request.Method;
        RequestUri = request.RequestUri;
        RequestText = request.ToString();

        _headers.Clear();
        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            _headers[header.Key] = string.Join(",", header.Value);

        if (request.Content is not null)
        {
            Body = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            ContentType = request.Content.Headers.ContentType?.ToString();
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                _headers[header.Key] = string.Join(",", header.Value);
        }

        return await _responder(request, cancellationToken).ConfigureAwait(false);
    }
}
