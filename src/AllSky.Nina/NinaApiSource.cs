using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace AllSky.Nina;

/// <summary>
/// Sorgente ninaAPI in sola lettura: tre GET verso il loopback, in parallelo, ciascuno con timeout.
/// </summary>
/// <remarks>
/// La lista dei percorsi e' chiusa: nessun POST e nessun endpoint che muova hardware o cambi la sequenza
/// (vedi docs/NINA-API.md §3). I nomi dei campi JSON sono [NV]: vedi <see cref="NinaFieldNames"/>.
/// Gli errori sono etichette brevi (timeout, connessione rifiutata, risposta non JSON, HTTP nnn):
/// il corpo della risposta non entra mai in <see cref="NinaState.Error"/> ne' nei log.
/// I redirect non vengono seguiti, e il proxy di sistema non e' usato: la richiesta resta sul loopback.
/// </remarks>
public sealed class NinaApiSource : INinaSource, IDisposable
{
    /// <summary>Mount: stato, parcheggio, coordinate.</summary>
    public const string MountInfoPath = "/v2/api/equipment/mount/info";

    /// <summary>Albero della sequenza, con il soggetto.</summary>
    public const string SequenceJsonPath = "/v2/api/sequence/json";

    /// <summary>Stato della sequenza. 409 significa sequenza non inizializzata, non un errore.</summary>
    public const string SequenceStatePath = "/v2/api/sequence/state";

    /// <summary>Dimensione massima accettata per una risposta.</summary>
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan MaxRequestTimeout = TimeSpan.FromSeconds(60);

    private readonly NinaEndpoint _endpoint;
    private readonly TimeSpan _timeout;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    /// <summary>Crea la sorgente. Non esegue richieste.</summary>
    /// <param name="endpoint">Indirizzo gia' validato (solo loopback).</param>
    /// <param name="requestTimeout">Tempo massimo per ciascuna chiamata, da 1 s a 60 s (dalla config connectTimeoutSeconds).</param>
    /// <param name="handler">Handler HTTP. Null: uno nuovo, senza redirect e senza proxy. Usato dai test.</param>
    /// <param name="time">Orologio per <see cref="NinaState.ReadAtUtc"/>. Null: orologio di sistema.</param>
    /// <exception cref="ArgumentOutOfRangeException">Timeout fuori dall'intervallo.</exception>
    public NinaApiSource(NinaEndpoint endpoint, TimeSpan requestTimeout, HttpMessageHandler? handler = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (requestTimeout <= TimeSpan.Zero || requestTimeout > MaxRequestTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "Il timeout di una chiamata a NINA deve essere tra 1 e 60 secondi.");
        }

        _endpoint = endpoint;
        _timeout = requestTimeout;
        _time = time ?? TimeProvider.System;
        _http = new HttpClient(
            handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false },
            disposeHandler: true)
        {
            // Il timeout e' gestito per chiamata con un CancellationToken, non con HttpClient.Timeout.
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">Solo se il chiamante annulla il token.</exception>
    public async Task<NinaState> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        DateTimeOffset istante = _time.GetUtcNow().ToUniversalTime();

        Task<Esito> mount = EseguiAsync(MountInfoPath, ct);
        Task<Esito> sequenza = EseguiAsync(SequenceJsonPath, ct);
        Task<Esito> stato = EseguiAsync(SequenceStatePath, ct);
        Esito[] esiti = await Task.WhenAll(mount, sequenza, stato).ConfigureAwait(false);

        try
        {
            return Costruisci(istante, esiti[0], esiti[1], esiti[2]);
        }
        catch (Exception)
        {
            // Il parsing e' difensivo: arrivare qui e' un bug, ma non si propaga.
            return NinaState.Offline(istante, "errore interno");
        }
        finally
        {
            foreach (Esito e in esiti)
            {
                e.Documento?.Dispose();
            }
        }
    }

    /// <summary>Libera l'HttpClient e l'handler.</summary>
    public void Dispose() => _http.Dispose();

    private static NinaState Costruisci(DateTimeOffset istante, Esito mount, Esito sequenza, Esito stato)
    {
        NinaMountState? mountStato = mount.Documento is { } dm ? NinaParser.Mount(NinaParser.Dati(dm.RootElement)) : null;
        NinaTargetState? target = sequenza.Documento is { } ds ? NinaParser.FindTarget(NinaParser.Dati(ds.RootElement)) : null;
        string? sequenzaStato = stato.Documento is { } dt ? NinaParser.SequenceState(NinaParser.Dati(dt.RootElement)) : null;

        bool connesso = mount.Risposto || sequenza.Risposto || stato.Risposto;
        string? errore = Riassumi(
            ("mount", mount.Errore),
            ("sequenza", sequenza.Errore),
            ("stato", stato.Errore));

        return new NinaState(connesso, istante, mountStato, target, sequenzaStato, errore);
    }

    /// <summary>Raggruppa gli errori uguali: "timeout (mount, sequenza)". Nessun corpo di risposta.</summary>
    private static string? Riassumi(params (string Parte, string? Errore)[] voci)
    {
        var gruppi = new List<(string Messaggio, List<string> Parti)>();
        foreach ((string parte, string? errore) in voci)
        {
            if (errore is null)
            {
                continue;
            }

            int indice = gruppi.FindIndex(g => g.Messaggio == errore);
            if (indice < 0)
            {
                gruppi.Add((errore, new List<string> { parte }));
            }
            else
            {
                gruppi[indice].Parti.Add(parte);
            }
        }

        if (gruppi.Count == 0)
        {
            return null;
        }

        return string.Join("; ", gruppi.Select(g => $"{g.Messaggio} ({string.Join(", ", g.Parti)})"));
    }

    private async Task<Esito> EseguiAsync(string percorso, CancellationToken ct)
    {
        using var scadenza = CancellationTokenSource.CreateLinkedTokenSource(ct);
        scadenza.CancelAfter(_timeout);

        try
        {
            using var richiesta = new HttpRequestMessage(HttpMethod.Get, _endpoint.BuildUri(percorso));
            richiesta.Headers.Accept.ParseAdd("application/json");

            using HttpResponseMessage risposta = await _http
                .SendAsync(richiesta, HttpCompletionOption.ResponseContentRead, scadenza.Token)
                .ConfigureAwait(false);

            if (percorso == SequenceStatePath && risposta.StatusCode == HttpStatusCode.Conflict)
            {
                // Sequenza non inizializzata: NINA ha risposto, nessun dato, nessun errore.
                return Esito.SenzaDati();
            }

            if (!risposta.IsSuccessStatusCode)
            {
                return Esito.DaRisposta($"HTTP {(int)risposta.StatusCode}");
            }

            byte[]? corpo = await LeggiLimitatoAsync(risposta.Content, scadenza.Token).ConfigureAwait(false);
            if (corpo is null)
            {
                return Esito.DaRisposta("risposta troppo grande");
            }

            return Analizza(percorso, corpo);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Esito.Rete("timeout");
        }
        catch (HttpRequestException ex)
        {
            return Esito.Rete(ClassificaRete(ex));
        }
        catch (Exception)
        {
            // Nessun messaggio dell'eccezione: puo' contenere dati interni.
            return Esito.Rete("errore interno");
        }
    }

    private static Esito Analizza(string percorso, byte[] corpo)
    {
        if (corpo.Length == 0)
        {
            return Esito.DaRisposta("risposta vuota");
        }

        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(corpo);
        }
        catch (JsonException)
        {
            return Esito.DaRisposta("risposta non JSON");
        }

        // Contenitore ninaAPI con "Success" false o "StatusCode" >= 400: errore dichiarato da NINA.
        string? errore = NinaParser.ErroreContenitore(documento.RootElement);
        if (errore is null)
        {
            return Esito.Ok(documento);
        }

        bool sequenzaNonInizializzata = percorso == SequenceStatePath
            && NinaParser.CodiceStato(documento.RootElement) == (int)HttpStatusCode.Conflict;
        documento.Dispose();

        return sequenzaNonInizializzata ? Esito.SenzaDati() : Esito.DaRisposta(errore);
    }

    private static string ClassificaRete(HttpRequestException ex) =>
        ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }
            ? "connessione rifiutata"
            : "errore di rete";

    /// <summary>Legge il corpo fino a <see cref="MaxBodyBytes"/>. Null se e' piu' grande.</summary>
    private static async Task<byte[]?> LeggiLimitatoAsync(HttpContent contenuto, CancellationToken ct)
    {
        using Stream flusso = await contenuto.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var memoria = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];

        while (true)
        {
            int letti = await flusso.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (letti == 0)
            {
                break;
            }

            if (memoria.Length + letti > MaxBodyBytes)
            {
                return null;
            }

            memoria.Write(buffer, 0, letti);
        }

        return memoria.ToArray();
    }

    /// <summary>Esito di una chiamata: se NINA ha risposto, il documento (da disporre) o l'errore breve.</summary>
    private sealed record Esito(bool Risposto, string? Errore, JsonDocument? Documento)
    {
        public static Esito Ok(JsonDocument documento) => new(true, null, documento);

        public static Esito SenzaDati() => new(true, null, null);

        /// <summary>Risposta HTTP ricevuta ma non utilizzabile.</summary>
        public static Esito DaRisposta(string errore) => new(true, errore, null);

        /// <summary>Nessuna risposta HTTP (rete, timeout).</summary>
        public static Esito Rete(string errore) => new(false, errore, null);
    }
}
