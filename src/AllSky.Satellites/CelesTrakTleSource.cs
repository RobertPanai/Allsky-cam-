using System.Globalization;
using System.Text.Json;

namespace AllSky.Satellites;

/// <summary>
/// Sorgente TLE da CelesTrak, con cache su disco e blocco di 24 ore dopo un errore.
/// </summary>
/// <remarks>
/// Regole della usage policy di CelesTrak (letta il 21/10/2026 su https://celestrak.org/usage-policy.php):
/// <list type="bullet">
/// <item>Una richiesta per bersaglio, e solo se la cache è più vecchia di <c>minInterval</c> (i GP si aggiornano ogni 2 ore).</item>
/// <item>Qualsiasi risposta diversa da 200, reindirizzamento (301) o errore di rete: stop a ogni richiesta per 24 ore.
/// Il blocco è salvato su disco e sopravvive al riavvio. Serve una persona per riprendere.</item>
/// <item>50x e 404 non fanno eccezione. Ogni altro esito non-200 vale come blocco.</item>
/// </list>
/// L'<see cref="HttpClient"/> iniettato deve avere <c>AllowAutoRedirect = false</c>, così un 301 non viene seguito in silenzio.
/// Il reindirizzamento viene rilevato comunque confrontando l'URL finale con quello richiesto.
/// </remarks>
public sealed class CelesTrakTleSource : ITleSource, ITleFetcher
{
    /// <summary>Unico URL usato dal progetto: una richiesta per numero di catalogo, formato TLE.</summary>
    private const string UrlTemplate = "https://celestrak.org/NORAD/elements/gp.php?CATNR={0}&FORMAT=TLE";

    /// <summary>Durata del blocco dopo un errore, in ore.</summary>
    public static readonly TimeSpan BlockDuration = TimeSpan.FromHours(24);

    /// <summary>Intervallo minimo tra due download per lo stesso bersaglio, se non indicato.</summary>
    public static readonly TimeSpan DefaultMinInterval = TimeSpan.FromHours(2);

    private const string BlockFileName = "block-state.json";

    private readonly HttpClient _http;
    private readonly ICelesTrakClock _clock;
    private readonly string _cacheFolder;
    private readonly TimeSpan _minInterval;
    private readonly string _userAgent;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Crea la sorgente.</summary>
    /// <param name="http">Client HTTP (con <c>AllowAutoRedirect = false</c>).</param>
    /// <param name="clock">Orologio, per la cache e il blocco.</param>
    /// <param name="cacheFolder">Cartella della cache: un file .tle e un .json per NORAD, più lo stato del blocco.</param>
    /// <param name="minInterval">Età minima della cache prima di una nuova richiesta. Se assente, 2 ore.</param>
    public CelesTrakTleSource(HttpClient http, ICelesTrakClock clock, string cacheFolder, TimeSpan? minInterval = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFolder);

        _http = http;
        _clock = clock;
        _cacheFolder = cacheFolder;
        _minInterval = minInterval ?? DefaultMinInterval;

        Version? version = typeof(CelesTrakTleSource).Assembly.GetName().Version;
        _userAgent = "AllSkyBrain/" + (version is null ? "0.0.0" : version.ToString(3));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TleSet>> GetAsync(IReadOnlyList<SatelliteTarget> targets, CancellationToken cancellationToken)
    {
        TleFetchResult result = await FetchAsync(targets, cancellationToken).ConfigureAwait(false);
        return result.Sets;
    }

    /// <summary>Come <see cref="GetAsync"/>, ma restituisce anche lo stato e il messaggio per l'utente.</summary>
    public async Task<TleFetchResult> FetchAsync(IReadOnlyList<SatelliteTarget> targets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await FetchCoreAsync(targets, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TleFetchResult> FetchCoreAsync(IReadOnlyList<SatelliteTarget> targets, CancellationToken ct)
    {
        Directory.CreateDirectory(_cacheFolder);
        DateTimeOffset now = _clock.UtcNow;

        var sets = new List<TleSet>();
        var messages = new List<string>();
        bool blocked = false;      // una richiesta sarebbe partita ma è sospesa dal blocco
        bool networkUsed = false;  // almeno un download riuscito
        bool formatFailed = false; // almeno un TLE rifiutato

        BlockState? block = ReadBlock();
        if (block is not null && IsBlockActive(block, now))
        {
            blocked = true;
            messages.Add(BlockedMessage(block));
        }

        bool blockedHit = false;   // una richiesta è stata davvero sospesa dal blocco

        foreach (SatelliteTarget target in targets)
        {
            ct.ThrowIfCancellationRequested();

            TleSet? cached = ReadCache(target);
            if (cached is not null && IsFresh(cached.FetchedUtc, now))
            {
                sets.Add(cached);
                continue;
            }

            if (blocked)
            {
                // Nessuna richiesta: si usa la cache anche se vecchia.
                blockedHit = true;
                if (cached is not null)
                {
                    sets.Add(cached);
                }

                continue;
            }

            Attempt attempt = await DownloadAsync(target, now, ct).ConfigureAwait(false);

            if (attempt.HaltReason is not null)
            {
                // Risposta non-200, reindirizzamento o rete: si ferma tutto per 24 ore.
                var newBlock = new BlockState(now + BlockDuration, attempt.HaltReason);
                blocked = true;
                blockedHit = true;
                messages.Add(BlockedMessage(newBlock));
                if (!TryWriteBlock(newBlock))
                {
                    messages.Add("Il blocco non è stato salvato su disco: dopo un riavvio le richieste potrebbero ripartire.");
                }

                if (cached is not null)
                {
                    sets.Add(cached);
                }

                continue;
            }

            if (attempt.FormatError is not null)
            {
                // Dati rifiutati: non si salva nulla e non scatta il blocco (la risposta era 200).
                formatFailed = true;
                messages.Add(attempt.FormatError);
                if (cached is not null)
                {
                    sets.Add(cached);
                }

                continue;
            }

            TleSet fresh = attempt.Set!;
            networkUsed = true;
            sets.Add(fresh);
            if (!TrySaveCache(fresh))
            {
                messages.Add($"Cache non aggiornata per il NORAD {target.NoradId}: la cartella non è scrivibile.");
            }
        }

        TleFetchStatus status =
            blockedHit ? TleFetchStatus.Blocked :
            formatFailed ? TleFetchStatus.Failed :
            networkUsed ? TleFetchStatus.Fresh :
            TleFetchStatus.FromCache;

        string? message = messages.Count == 0 ? null : string.Join(" ", messages);
        return new TleFetchResult(sets, status, message);
    }

    /// <summary>Esito di un singolo download: un TLE valido, un rifiuto di formato o un motivo di arresto.</summary>
    private sealed record Attempt(TleSet? Set, string? FormatError, string? HaltReason);

    private async Task<Attempt> DownloadAsync(SatelliteTarget target, DateTimeOffset now, CancellationToken ct)
    {
        int id = target.NoradId;
        string url = string.Format(CultureInfo.InvariantCulture, UrlTemplate, id);
        string body;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(_userAgent);

            using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                return Halt($"CelesTrak ha risposto HTTP {(int)response.StatusCode} per il NORAD {id}.");
            }

            if (response.RequestMessage?.RequestUri is { } finalUri && !finalUri.Equals(new Uri(url)))
            {
                return Halt($"CelesTrak ha reindirizzato la richiesta per il NORAD {id}: URL obsoleto o dominio errato.");
            }

            body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Halt($"Impossibile contattare CelesTrak per il NORAD {id}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Halt($"Timeout nella richiesta a CelesTrak per il NORAD {id}.");
        }

        if (!TleParser.TryParse(body, target, now, out TleSet? set, out string? error))
        {
            return new Attempt(null, $"Dati TLE non validi per il NORAD {id}: {error}. Nessun dato salvato.", null);
        }

        return new Attempt(set, null, null);
    }

    private static Attempt Halt(string reason) => new(null, null, reason);

    private static bool IsBlockActive(BlockState block, DateTimeOffset now) =>
        block.UntilUtc > now && block.UntilUtc <= now + BlockDuration;

    private bool IsFresh(DateTimeOffset fetchedUtc, DateTimeOffset now)
    {
        TimeSpan age = now - fetchedUtc;
        return age >= TimeSpan.Zero && age < _minInterval;
    }

    private static string BlockedMessage(BlockState block) =>
        $"{block.Reason} Nuove richieste sospese fino al {block.UntilUtc.UtcDateTime:yyyy-MM-dd HH:mm} UTC: " +
        "segnalare il problema a una persona prima di riprovare.";

    // ----- Cache su disco: un .tle (tre righe) e un .json (istante del download) per NORAD -----

    private sealed record CacheMeta(DateTimeOffset FetchedUtc);

    private TleSet? ReadCache(SatelliteTarget target)
    {
        try
        {
            string tlePath = TlePath(target.NoradId);
            string metaPath = MetaPath(target.NoradId);
            if (!File.Exists(tlePath) || !File.Exists(metaPath))
            {
                return null;
            }

            CacheMeta? meta = JsonSerializer.Deserialize<CacheMeta>(File.ReadAllText(metaPath));
            if (meta is null)
            {
                return null;
            }

            // Una cache corrotta o con checksum errato viene ignorata, come se non esistesse.
            return TleParser.TryParse(File.ReadAllText(tlePath), target, meta.FetchedUtc, out TleSet? set, out _) ? set : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private bool TrySaveCache(TleSet set)
    {
        try
        {
            WriteAtomic(TlePath(set.NoradId), $"{set.Name}\n{set.Line1}\n{set.Line2}\n");
            WriteAtomic(MetaPath(set.NoradId), JsonSerializer.Serialize(new CacheMeta(set.FetchedUtc)));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ----- Stato del blocco, su disco per sopravvivere al riavvio -----

    private sealed record BlockState(DateTimeOffset UntilUtc, string Reason);

    private BlockState? ReadBlock()
    {
        try
        {
            string path = BlockPath;
            return File.Exists(path) ? JsonSerializer.Deserialize<BlockState>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private bool TryWriteBlock(BlockState block)
    {
        try
        {
            WriteAtomic(BlockPath, JsonSerializer.Serialize(block));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Scrittura atomica: file temporaneo nella stessa cartella, poi spostamento sul file definitivo.</summary>
    private static void WriteAtomic(string path, string content)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    private string TlePath(int noradId) => Path.Combine(_cacheFolder, noradId.ToString(CultureInfo.InvariantCulture) + ".tle");

    private string MetaPath(int noradId) => Path.Combine(_cacheFolder, noradId.ToString(CultureInfo.InvariantCulture) + ".json");

    private string BlockPath => Path.Combine(_cacheFolder, BlockFileName);
}
