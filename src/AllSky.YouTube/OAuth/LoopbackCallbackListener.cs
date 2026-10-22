using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AllSky.YouTube;

/// <summary>Esito ricevuto dal browser sul redirect loopback. Il codice non compare mai in ToString.</summary>
public sealed record LoopbackCallback(OAuthSecret Code, string? Error)
{
    public override string ToString() =>
        $"LoopbackCallback {{ Code = {Code}, Error = {Error ?? "(nessuno)"} }}";
}

/// <summary>
/// Listener HTTP loopback per il solo login OAuth. Ascolta SOLO su 127.0.0.1, su una porta libera scelta dal sistema
/// (guida Google: "start an HTTP listener on a random available port"). Accetta la prima richiesta GET con lo state
/// atteso e con code o error. Va usato sempre con <c>using</c>: Dispose chiude il listener.
/// </summary>
public sealed class LoopbackCallbackListener : IDisposable
{
    private const int MaxCodeLength = 2048;
    private const int MaxErrorLength = 64;

    private readonly HttpListener _listener;
    private bool _disposed;

    private LoopbackCallbackListener(HttpListener listener, string redirectUri)
    {
        _listener = listener;
        RedirectUri = redirectUri;
    }

    /// <summary>Redirect URI da passare a Google, nella forma <c>http://127.0.0.1:PORTA</c>.</summary>
    public string RedirectUri { get; }

    /// <summary>Avvia il listener su una porta libera di 127.0.0.1.</summary>
    /// <remarks>
    /// La porta viene scelta con un TcpListener di prova e poi liberata: c'e' una piccola finestra in cui un altro
    /// processo potrebbe occuparla. In quel caso Start lancia un'eccezione e il login va ripetuto.
    /// </remarks>
    public static LoopbackCallbackListener Start()
    {
        int port = FindFreeLoopbackPort();
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
            listener.Start();
        }
        catch
        {
            listener.Close();
            throw;
        }

        return new LoopbackCallbackListener(listener, $"http://127.0.0.1:{port}");
    }

    /// <summary>
    /// Attende il redirect del browser. Le richieste con state errato, senza code/error o con metodo diverso da GET
    /// ricevono una risposta fissa e NON vengono accettate: il login continua ad attendere fino al timeout.
    /// </summary>
    /// <exception cref="TimeoutException">Nessun redirect valido entro <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException">Annullato da <paramref name="cancellationToken"/>.</exception>
    public async Task<LoopbackCallback> WaitForCallbackAsync(
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedState);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Il timeout deve essere positivo.");
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        using CancellationTokenRegistration stop = linked.Token.Register(static state => StopQuietly((HttpListener)state!), _listener);

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (linked.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("Login OAuth annullato.", cancellationToken);
                throw new TimeoutException("Nessuna risposta dal browser entro il tempo previsto per il login.");
            }

            if (TryHandle(context, expectedState, out LoopbackCallback? callback))
                return callback!;
        }
    }

    /// <summary>Chiude il listener e libera la porta. Sicuro da chiamare piu' volte.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _listener.Close();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
        {
            // Gia' chiuso dal sistema: nulla da fare.
        }
    }

    private static bool TryHandle(HttpListenerContext context, string expectedState, out LoopbackCallback? callback)
    {
        callback = null;
        HttpListenerRequest request = context.Request;

        if (!string.Equals(request.HttpMethod, "GET", StringComparison.Ordinal))
        {
            Respond(context, HttpStatusCode.MethodNotAllowed, Pages.Invalid);
            return false;
        }

        System.Collections.Specialized.NameValueCollection query = request.QueryString;
        string? state = query["state"];
        string? code = query["code"];
        string? error = query["error"];

        if (state is null || (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(error)))
        {
            Respond(context, HttpStatusCode.NotFound, Pages.NotFound);
            return false;
        }

        if (!FixedTimeEquals(state, expectedState))
        {
            Respond(context, HttpStatusCode.BadRequest, Pages.Invalid);
            return false;
        }

        if (!string.IsNullOrEmpty(error))
        {
            Respond(context, HttpStatusCode.OK, Pages.Denied);
            callback = new LoopbackCallback(default, Truncate(error, MaxErrorLength));
            return true;
        }

        if (code!.Length > MaxCodeLength)
        {
            Respond(context, HttpStatusCode.BadRequest, Pages.Invalid);
            return false;
        }

        Respond(context, HttpStatusCode.OK, Pages.Done);
        callback = new LoopbackCallback(new OAuthSecret(code), null);
        return true;
    }

    private static int FindFreeLoopbackPort()
    {
        var probe = new TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private static void StopQuietly(HttpListener listener)
    {
        try
        {
            if (listener.IsListening)
                listener.Stop();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
        {
            // Gia' fermato o chiuso: l'attesa termina comunque.
        }
    }

    private static void Respond(HttpListenerContext context, HttpStatusCode status, string html)
    {
        byte[] body = Encoding.UTF8.GetBytes(html);
        HttpListenerResponse response = context.Response;
        try
        {
            response.StatusCode = (int)status;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = body.Length;
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'";
            if (status == HttpStatusCode.MethodNotAllowed)
                response.Headers["Allow"] = "GET";
            response.OutputStream.Write(body, 0, body.Length);
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
        {
            // Il browser ha chiuso la connessione: la risposta non serve piu'.
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                // Gia' chiusa.
            }
        }
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        byte[] x = Encoding.UTF8.GetBytes(a);
        byte[] y = Encoding.UTF8.GetBytes(b);
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x, y);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>Pagine fisse: nessun input dell'utente viene riflesso nell'HTML.</summary>
    private static class Pages
    {
        public const string Done =
            "<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\"><title>All Sky Brain</title>" +
            "<style>body{font-family:sans-serif;background:#05070A;color:#e6ecff;padding:2rem}</style></head>" +
            "<body><h1>Accesso completato</h1><p>Puoi chiudere questa scheda e tornare a All Sky Brain.</p></body></html>";

        public const string Denied =
            "<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\"><title>All Sky Brain</title>" +
            "<style>body{font-family:sans-serif;background:#05070A;color:#e6ecff;padding:2rem}</style></head>" +
            "<body><h1>Autorizzazione non concessa</h1><p>Puoi chiudere questa scheda e riprovare da All Sky Brain.</p></body></html>";

        public const string Invalid =
            "<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\"><title>All Sky Brain</title>" +
            "<style>body{font-family:sans-serif;background:#05070A;color:#e6ecff;padding:2rem}</style></head>" +
            "<body><h1>Richiesta non valida</h1><p>Torna a All Sky Brain e riprova il collegamento.</p></body></html>";

        public const string NotFound =
            "<!doctype html><html lang=\"it\"><head><meta charset=\"utf-8\"><title>All Sky Brain</title>" +
            "<style>body{font-family:sans-serif;background:#05070A;color:#e6ecff;padding:2rem}</style></head>" +
            "<body><h1>Non trovato</h1></body></html>";
    }
}
