using System.Net;
using System.Net.Sockets;
using AllSky.Nina;

namespace AllSky.Nina.Tests;

/// <summary>Orologio finto: restituisce sempre l'istante impostato.</summary>
internal sealed class NinaFakeTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Handler HTTP finto per NinaApiSource. Registra ogni richiesta e segnala ogni violazione
/// (metodo diverso da GET, percorso fuori dalla lista ammessa). Nessuna rete.
/// </summary>
internal sealed class NinaScriptedHandler : HttpMessageHandler
{
    /// <summary>Gli unici percorsi che la sorgente puo' chiamare.</summary>
    public static readonly string[] PercorsiAmmessi =
    {
        "/v2/api/equipment/mount/info",
        "/v2/api/sequence/json",
        "/v2/api/sequence/state",
    };

    private readonly Dictionary<string, Func<CancellationToken, Task<HttpResponseMessage>>> _risposte = new(StringComparer.Ordinal);

    /// <summary>Richieste ricevute, come "GET http://host:porta/percorso".</summary>
    public List<string> Richieste { get; } = new();

    /// <summary>Violazioni di sola lettura viste dall'handler. Devono restare vuote.</summary>
    public List<string> Violazioni { get; } = new();

    /// <summary>Risposta con corpo JSON (o testo) e codice dato.</summary>
    public NinaScriptedHandler Risponde(string percorso, string corpo, HttpStatusCode codice = HttpStatusCode.OK)
    {
        _risposte[percorso] = _ => Task.FromResult(Risposta(codice, corpo));
        return this;
    }

    /// <summary>Errore di trasporto dato come eccezione.</summary>
    public NinaScriptedHandler Lancia(string percorso, Exception errore)
    {
        _risposte[percorso] = _ => Task.FromException<HttpResponseMessage>(errore);
        return this;
    }

    /// <summary>Connessione rifiutata, come la vede HttpClient su loopback senza NINA.</summary>
    public NinaScriptedHandler Rifiutata(string percorso) =>
        Lancia(percorso, new HttpRequestException("rifiutata", new SocketException((int)SocketError.ConnectionRefused)));

    /// <summary>Non risponde mai: finche' il token non e' annullato.</summary>
    public NinaScriptedHandler Blocca(string percorso)
    {
        _risposte[percorso] = async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return Risposta(HttpStatusCode.OK, "{}");
        };
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage richiesta, CancellationToken ct)
    {
        string percorso = richiesta.RequestUri?.AbsolutePath ?? string.Empty;
        Richieste.Add($"{richiesta.Method} {richiesta.RequestUri}");

        if (richiesta.Method != HttpMethod.Get)
        {
            Violazioni.Add("metodo non GET: " + richiesta.Method);
        }

        if (!PercorsiAmmessi.Contains(percorso))
        {
            Violazioni.Add("endpoint non ammesso: " + percorso);
        }

        if (richiesta.Method != HttpMethod.Get || !PercorsiAmmessi.Contains(percorso))
        {
            return Task.FromException<HttpResponseMessage>(new InvalidOperationException("violazione di sola lettura"));
        }

        if (_risposte.TryGetValue(percorso, out var risposta))
        {
            return risposta(ct);
        }

        return Task.FromResult(Risposta(HttpStatusCode.ServiceUnavailable, string.Empty));
    }

    private static HttpResponseMessage Risposta(HttpStatusCode codice, string corpo) => new(codice)
    {
        Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"),
    };
}

/// <summary>Sorgente finta per il poller: il valore dipende dal numero della lettura (1, 2, ...).</summary>
internal sealed class NinaScriptedSource : INinaSource
{
    private readonly Func<int, NinaState> _produci;
    private int _letture;

    public NinaScriptedSource(Func<int, NinaState> produci)
    {
        _produci = produci;
    }

    public int Letture => Volatile.Read(ref _letture);

    public Task<NinaState> ReadAsync(CancellationToken ct)
    {
        int numero = Interlocked.Increment(ref _letture);
        return Task.FromResult(_produci(numero));
    }
}
