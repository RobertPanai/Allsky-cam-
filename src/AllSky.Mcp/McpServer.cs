using System.Net;
using System.Text;
using AllSky.Engine;

namespace AllSky.Mcp;

/// <summary>
/// Server MCP locale di sola lettura. Ascolta SOLO su 127.0.0.1 (mai 0.0.0.0, mai ::, mai il nome del computer) e gira
/// su task propri: un errore del server non tocca la cattura. Start e Stop sono idempotenti; Stop libera la porta.
/// </summary>
/// <remarks>
/// Controlli nell'ordine: Host (deve essere 127.0.0.1:porta o localhost:porta), Origin (assente o locale), percorso
/// (solo <c>/mcp</c>), metodo (solo POST), Content-Type (application/json), dimensione (massimo 64 KB), poi il protocollo.
/// Un controllo fallito risponde subito, senza eseguire nulla.
/// </remarks>
public sealed class McpServer : IDisposable
{
    private readonly IAllSkyService _service;
    private readonly McpTools _tools;
    private readonly McpProtocol _protocol;
    private readonly Action<string, Exception?> _log;
    private readonly object _gate = new();
    private HttpListener? _listener;       // _gate
    private CancellationTokenSource? _cts; // _gate
    private bool _disposed;                // _gate

    /// <summary>Crea il server (senza avviarlo).</summary>
    /// <param name="service">Servizio applicativo, usato solo in lettura.</param>
    /// <param name="port">Porta TCP su 127.0.0.1, da 1 a 65535 (la configurazione ammette solo 1024 e oltre).</param>
    /// <param name="renderer">Render delle viste per <c>view_png</c>. Null: non disponibile.</param>
    /// <param name="log">Log degli errori, senza dettagli verso il client. Null: nessun log.</param>
    /// <param name="utcNow">Orologio UTC per il mirino. Null: quello di sistema.</param>
    public McpServer(IAllSkyService service, int port, IViewRenderer? renderer = null, Action<string, Exception?>? log = null, Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "La porta deve essere compresa tra 1 e 65535.");
        }

        _service = service;
        Port = port;
        _log = log ?? ((_, _) => { });
        _tools = new McpTools(service, renderer, utcNow, _log);
        _protocol = new McpProtocol(service, _tools);
    }

    /// <summary>Porta TCP richiesta.</summary>
    public int Port { get; }

    /// <summary>Vero se il listener e' aperto.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _listener is not null;
            }
        }
    }

    /// <summary>
    /// Apre il listener su 127.0.0.1:porta. Se e' gia' aperto non fa nulla. Lancia <see cref="InvalidOperationException"/>
    /// se la porta non si puo' usare (occupata o non disponibile): il chiamante decide se continuare senza server.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_listener is not null)
            {
                return;
            }

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            try
            {
                listener.Start();
            }
            catch (Exception ex) when (ex is HttpListenerException or System.Net.Sockets.SocketException or IOException)
            {
                listener.Close();
                throw new InvalidOperationException($"Impossibile ascoltare su 127.0.0.1:{Port}: porta occupata o non disponibile.", ex);
            }

            var cts = new CancellationTokenSource();
            _listener = listener;
            _cts = cts;
            _ = Task.Run(() => AcceptLoopAsync(listener, cts.Token));
        }
    }

    /// <summary>Chiude il listener e libera la porta. Sicuro da chiamare piu' volte e senza server avviato.</summary>
    public void Stop()
    {
        HttpListener? listener;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            listener = _listener;
            cts = _cts;
            _listener = null;
            _cts = null;
        }

        if (listener is null)
        {
            return;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Gia' annullato: nulla da fare.
        }

        try
        {
            listener.Close();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
        {
            // Gia' chiuso dal sistema: la porta e' comunque libera.
            _log("Chiusura del server MCP: il listener era gia' chiuso", ex);
        }

        // Il CancellationTokenSource non si Dispose qui: i task ancora in volo usano il suo token.
    }

    /// <summary>Ferma il server. Dopo Dispose <see cref="Start"/> lancia.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException
                                       || ct.IsCancellationRequested)
            {
                // Chiusura normale: Stop ha chiuso il listener.
                break;
            }
            catch (Exception ex)
            {
                _log("Server MCP: errore nell'attesa delle richieste", ex);
                if (!listener.IsListening)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => HandleSafeAsync(context, ct));
        }
    }

    private async Task HandleSafeAsync(HttpListenerContext context, CancellationToken ct)
    {
        try
        {
            await HandleAsync(context, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log("Server MCP: richiesta non completata", ex);
            try
            {
                Write(context.Response, 500, JsonErrorBody(-32603, "Errore interno del server MCP."));
            }
            catch (Exception)
            {
                // La risposta non si puo' scrivere (connessione chiusa): il log ha gia' l'errore.
            }
        }
        finally
        {
            try
            {
                context.Response.Close();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
            {
                // Gia' chiusa: nulla da fare.
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (!McpHttpPolicy.IsAllowedHost(request.Headers["Host"], Port))
        {
            Write(response, 403, JsonErrorBody(-32600, "Host non consentito: usare 127.0.0.1 o localhost."));
            return;
        }

        if (!McpHttpPolicy.IsAllowedOrigin(request.Headers["Origin"]))
        {
            Write(response, 403, JsonErrorBody(-32600, "Origin non consentita: solo richieste locali."));
            return;
        }

        if (request.Url is null || request.Url.AbsolutePath != McpHttpPolicy.EndpointPath)
        {
            response.StatusCode = 404;
            return;
        }

        if (!string.Equals(request.HttpMethod, "POST", StringComparison.Ordinal))
        {
            // GET e DELETE: questo server non apre flussi SSE e non ha sessioni. La specifica ammette 405 [V].
            response.Headers["Allow"] = "POST";
            response.StatusCode = 405;
            return;
        }

        if (!McpHttpPolicy.IsJsonContentType(request.ContentType))
        {
            Write(response, 415, JsonErrorBody(-32600, "Content-Type deve essere application/json."));
            return;
        }

        if (request.ContentLength64 > McpHttpPolicy.MaxBodyBytes)
        {
            Write(response, 413, JsonErrorBody(-32600, "Corpo troppo grande: massimo 64 KB."));
            return;
        }

        byte[]? bytes = await ReadBodyAsync(request.InputStream, ct).ConfigureAwait(false);
        if (bytes is null)
        {
            Write(response, 413, JsonErrorBody(-32600, "Corpo troppo grande: massimo 64 KB."));
            return;
        }

        string body = Encoding.UTF8.GetString(bytes);
        McpReply reply = await _protocol.HandleAsync(body, request.Headers["MCP-Protocol-Version"], ct).ConfigureAwait(false);
        if (reply.Json is null)
        {
            response.StatusCode = reply.StatusCode;
            response.ContentLength64 = 0;
            return;
        }

        Write(response, reply.StatusCode, reply.Json);
    }

    /// <summary>Legge il corpo fino al limite. Restituisce null se supera 64 KB.</summary>
    private static async Task<byte[]?> ReadBodyAsync(Stream input, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int read = await input.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > McpHttpPolicy.MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static void Write(HttpListenerResponse response, int status, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static string JsonErrorBody(int code, string message) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{{\"code\":{code},\"message\":\"{message}\"}}}}";
}
