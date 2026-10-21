using AllSky.Core;
using AllSky.Imaging.Jpeg;
using AllSky.Publish;

namespace AllSky.Host;

/// <summary>
/// Segreti in memoria per la sanificazione: quelli salvati (token e valore dell'header extra) e quelli digitati in una
/// prova di connessione non ancora salvati. Thread-safe.
/// </summary>
internal sealed class PublishSecrets
{
    private readonly object _gate = new();
    private readonly List<string> _saved = new();      // _gate
    private readonly List<string> _transient = new();  // _gate

    /// <summary>Sostituisce i segreti salvati con quelli delle impostazioni indicate.</summary>
    public void SetSaved(PublishSettings settings)
    {
        lock (_gate)
        {
            _saved.Clear();
            _saved.Add(settings.Token.Reveal());
            _saved.Add(settings.ExtraHeaderValue.Reveal());
        }
    }

    /// <summary>Imposta i segreti digitati nella prova di connessione in corso (non salvati).</summary>
    public void SetTransient(IEnumerable<string?> values)
    {
        lock (_gate)
        {
            _transient.Clear();
            foreach (string? value in values)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    _transient.Add(value);
                }
            }
        }
    }

    /// <summary>Rimuove i segreti della prova di connessione.</summary>
    public void ClearTransient()
    {
        lock (_gate)
        {
            _transient.Clear();
        }
    }

    /// <summary>Tutti i segreti correnti, salvati e provvisori. Non vuoti non garantiti: chi usa il valore deve ignorare le stringhe vuote.</summary>
    public IReadOnlyCollection<string> Current()
    {
        lock (_gate)
        {
            return _saved.Concat(_transient).Where(v => v.Length > 0).ToArray();
        }
    }
}

/// <summary>
/// Servizi della pubblicazione creati dall'Host: impostazioni (con lo store su disco), publisher, client HTTP, log e sanificazione.
/// Creati da <see cref="HostPublishing.TryCreate"/>; se la creazione fallisce l'Host parte senza pubblicazione.
/// </summary>
internal sealed class PublishServices : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HttpClient _http;
    private PublishSettings _current;  // _gate

    /// <summary>Crea i servizi. Usato solo da <see cref="HostPublishing"/>.</summary>
    public PublishServices(
        PublishSettingsStore store,
        IPublisher publisher,
        HttpClient http,
        PublishLog log,
        Redactor redactor,
        PublishSecrets secrets,
        PublishSettings initial)
    {
        Store = store;
        Publisher = publisher;
        _http = http;
        Log = log;
        Redactor = redactor;
        Secrets = secrets;
        _current = initial;
    }

    /// <summary>Archivio delle impostazioni su disco.</summary>
    public PublishSettingsStore Store { get; }

    /// <summary>Publisher usato dal motore e dagli endpoint.</summary>
    public IPublisher Publisher { get; }

    /// <summary>Log dedicato della pubblicazione (publish-AAAAMMGG.log).</summary>
    public PublishLog Log { get; }

    /// <summary>Sanificatore dei testi destinati a log o risposte.</summary>
    public Redactor Redactor { get; }

    /// <summary>Segreti in memoria per la sanificazione.</summary>
    public PublishSecrets Secrets { get; }

    /// <summary>Impostazioni salvate correnti (senza segreti in chiaro nei campi visibili).</summary>
    public PublishSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Registra le impostazioni appena salvate, aggiornando anche i segreti per la sanificazione.</summary>
    public void Replace(PublishSettings settings)
    {
        lock (_gate)
        {
            _current = settings;
        }

        Secrets.SetSaved(settings);
    }

    /// <summary>Chiude il client HTTP. Il publisher va fermato prima con <see cref="HostPublishing.StopPublisherAsync"/>.</summary>
    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Costruzione dei servizi di pubblicazione dentro l'Host, con avvio e arresto che non fanno mai cadere l'Host.</summary>
internal static class HostPublishing
{
    private const string SettingsFileName = "publish.json";
    private const string BootstrapFileName = "bootstrap.json";
    private const string QueueFolderName = "publish-queue";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Crea i servizi. Qualunque errore viene registrato nel log dell'Host e restituisce null: l'Host parte comunque, senza pubblicazione.
    /// </summary>
    /// <param name="options">Opzioni dell'Host (protezione e gestore HTTP di test, se presenti).</param>
    /// <param name="dataFolder">Cartella dati: qui stanno publish.json, bootstrap.json e publish-queue.</param>
    /// <param name="logFolder">Cartella dei log (il log della pubblicazione va in questa cartella).</param>
    /// <param name="secrets">Registro dei segreti, condiviso con il filtro del log generale.</param>
    /// <param name="hostLog">Log generale dell'Host.</param>
    public static PublishServices? TryCreate(HostOptions options, string dataFolder, string logFolder, PublishSecrets secrets, DailyFileLog hostLog)
    {
        try
        {
            return Create(options, dataFolder, logFolder, secrets);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hostLog.Write("Pubblicazione non disponibile: l'applicazione parte senza pubblicazione.", ex);
            return null;
        }
    }

    /// <summary>Avvia il ciclo di pubblicazione. Chiamare dopo l'avvio del motore. Un errore viene solo registrato.</summary>
    public static async Task StartPublisherAsync(PublishServices publishing, DailyFileLog hostLog)
    {
        try
        {
            // Il ciclo termina solo con StopPublisherAsync: non e' legato al token di avvio dell'Host.
            await publishing.Publisher.StartAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hostLog.Write("Avvio della pubblicazione non riuscito", ex);
        }
    }

    /// <summary>Ferma il ciclo di pubblicazione. Chiamare prima di fermare il motore. Non lancia.</summary>
    public static async Task StopPublisherAsync(PublishServices publishing, DailyFileLog hostLog)
    {
        try
        {
            await publishing.Publisher.StopAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hostLog.Write("Arresto della pubblicazione non pulito", ex);
        }
    }

    private static PublishServices Create(HostOptions options, string dataFolder, string logFolder, PublishSecrets secrets)
    {
        ISecretProtector protector = options.SecretProtector
            ?? (OperatingSystem.IsWindows() ? new AllSky.Publish.DpapiSecretProtector() : new UnsupportedSecretProtector());
        IClock clock = new SystemClock();
        var redactor = new Redactor(secrets.Current);
        var log = new PublishLog(logFolder, clock, redactor.Sanitize);
        var store = new PublishSettingsStore(Path.Combine(dataFolder, SettingsFileName), protector);

        // Il file di avvio si applica prima di leggere le impostazioni, cosi' il primo caricamento le vede.
        try
        {
            BootstrapLoader.Apply(Path.Combine(dataFolder, BootstrapFileName), store, log.AsInfo());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error("Applicazione del file di avvio non riuscita: impostazioni invariate", ex);
        }

        PublishSettings initial = store.Load();
        secrets.SetSaved(initial);

        HttpClient http = CreateHttpClient(options);
        try
        {
            var sender = new UplinkHttpClient(http, clock, redactor);
            var queue = new DiskQueue(Path.Combine(dataFolder, QueueFolderName), clock);
            var worker = new PublishWorker(
                initial,
                clock,
                new SystemPublishDelay(),
                sender,
                new WebImagePreparer(new BaselineJpegEncoder()),
                queue,
                new RetryPolicy(new SystemJitter()),
                log.AsInfo());
            return new PublishServices(store, worker, http, log, redactor, secrets, initial);
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Un solo client per tutta la vita dell'Host. Il timeout di richiesta non e' qui (infinito): lo applica
    /// <see cref="UplinkHttpClient"/> a 30 secondi per ogni invio.
    /// </summary>
    private static HttpClient CreateHttpClient(HostOptions options)
    {
        if (options.PublishHttpHandler is { } handler)
        {
            return new HttpClient(handler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }

        var socket = new SocketsHttpHandler { ConnectTimeout = ConnectTimeout };
        return new HttpClient(socket) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }
}
