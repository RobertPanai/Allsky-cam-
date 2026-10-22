using System.Net.Sockets;
using AllSky.Capture;
using AllSky.Core;
using AllSky.Engine;
using AllSky.Imaging.Jpeg;
using AllSky.Processing;
using AllSky.Timelapse;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AllSky.Host;

/// <summary>
/// Punto d'ingresso dell'Host HTTP locale: avvia il motore, il server e la UI. Non e' un eseguibile: l'app Windows
/// (AllSky.Tray) lo chiama. Nessuna dipendenza da WinForms.
/// </summary>
public static class AllSkyHost
{
    /// <summary>Porta predefinita, usata se la configurazione non ne indica una valida.</summary>
    public const int DefaultPort = 5810;

    private const string LanNoPasswordMessage =
        "L'accesso dalla rete locale non ha una password. Impostarla nelle impostazioni di All Sky Brain.";
    private const string LanWrongPasswordMessage = "Password non valida per l'accesso dalla rete locale.";

    /// <summary>Quante porte successive provare se quella richiesta e' occupata (5810 .. 5820).</summary>
    private const int PortSearchSpan = 10;

    /// <summary>
    /// Avvia il motore e il server HTTP. Crea il file di configurazione con i valori predefiniti se manca.
    /// </summary>
    /// <param name="options">Percorsi e porta.</param>
    /// <param name="cancellationToken">Annullamento dell'avvio: se scatta prima che l'Host sia pronto, tutto viene fermato.</param>
    /// <returns>Runtime in esecuzione, con l'URL reale.</returns>
    /// <exception cref="InvalidOperationException">Cartelle non scrivibili o nessuna porta libera.</exception>
    /// <exception cref="DirectoryNotFoundException">La cartella della UI non esiste.</exception>
    public static Task<AllSkyHostRuntime> StartAsync(HostOptions options, CancellationToken cancellationToken = default)
    {
        return StartCoreAsync(options, HostTuning.Default, cancellationToken);
    }

    /// <summary>Avvio con parametri interni, usato dai test.</summary>
    internal static async Task<AllSkyHostRuntime> StartCoreAsync(HostOptions options, HostTuning tuning, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConfigPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.WebRoot);
        if (options.PortOverride is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.PortOverride, "La porta deve essere compresa tra 0 e 65535.");
        }

        string configPath = Path.GetFullPath(options.ConfigPath);
        string dataFolder = EnsureDirectory(Path.GetFullPath(options.DataFolder));
        string webRoot = Path.GetFullPath(options.WebRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(webRoot))
        {
            throw new DirectoryNotFoundException($"Cartella della UI non trovata: '{webRoot}'. Reinstallare l'applicazione o verificare il percorso.");
        }

        string logFolder = Path.Combine(dataFolder, "logs");
        // I segreti della pubblicazione sono filtrati da tutti i log dell'Host, anche dalle tracce delle eccezioni.
        var secrets = new PublishSecrets();
        Func<string, string> sanitize = text => SecretMask.Apply(text, secrets.Current());
        var log = new DailyFileLog(logFolder, sanitize);
        log.Write($"Avvio di AllSky Host. Dati: '{dataFolder}'. Configurazione: '{configPath}'.");

        AppConfig initial = LoadOrCreateConfig(configPath, dataFolder, log);

        int preferred = options.PortOverride ?? initial.Ui.Port;
        if (preferred is not 0 and < 1024)
        {
            log.Write($"Porta {preferred} non ammessa (le porte sotto 1024 richiedono privilegi): uso {DefaultPort}.");
            preferred = DefaultPort;
        }

        // Interfaccia web spenta di default: senza ui.webEnabled l'Host non apre alcuna porta HTTP.
        bool webEnabled = initial.Ui.WebEnabled;

        // Rete locale solo se richiesta, con interfaccia web attiva e con una password impostata. Altrimenti si resta su 127.0.0.1.
        bool lanRequested = options.AllowLanOverride || initial.Ui.AllowLan;
        bool lan = webEnabled && lanRequested && !string.IsNullOrEmpty(initial.Ui.LanPasswordHash);
        if (lanRequested && !lan)
        {
            log.Write("Accesso dalla rete locale richiesto ma non attivo: servono l'interfaccia web e una password (ui.lanPasswordHash).");
        }

        // Un guasto della pubblicazione non impedisce l'avvio: TryCreate restituisce null e l'Host parte senza.
        PublishServices? publishing = HostPublishing.TryCreate(options, dataFolder, logFolder, secrets, log);

        // Aggiornamenti: il servizio nasce qui ma il timer parte solo a Host pronto (vedi Start piu' sotto).
        UpdateServices updates = UpdateServices.Create(options.UpdateBackend, dataFolder);

        AllSkyEngine? engine = null;
        YouTubeServices? youTube = null;
        WebApplication? app = null;
        var shutdown = new CancellationTokenSource();
        bool ready = false;
        try
        {
            var cameras = new CameraProvider(() => engine?.GetConfig() ?? initial, tuning, log);
            var encoder = new BaselineJpegEncoder();
            engine = new AllSkyEngine(new EngineDependencies(
                Clock: new SystemClock(),
                CameraProvider: cameras,
                ProcessorFactory: cfg => new FrameProcessor(cfg, encoder, null),
                TimelapseBuilder: new TimelapseBuilder(),
                CaptureDelay: new SystemCaptureDelay(),
                JpegEncoder: encoder,
                ConfigPath: configPath,
                Log: log.Write,
                Publisher: publishing?.Publisher,
                FrameOverlayFactory: options.FrameOverlayFactory,
                TleFetcher: options.TleFetcher,
                OrbitPropagators: options.OrbitPropagators));

            await engine.StartAsync(cancellationToken);

            // Il publisher parte dopo il motore: il primo frame arriva quando la cattura e' gia' attiva.
            if (publishing is not null)
            {
                await HostPublishing.StartPublisherAsync(publishing, log);
            }

            // Il caricamento YouTube parte dopo il motore: il primo tick puo' leggere l'archivio gia' attivo.
            youTube = YouTubeServices.TryCreate(options, dataFolder, engine, log);
            youTube?.Start();

            var context = new HostContext(engine, log, shutdown.Token, logFolder, webRoot, publishing, sanitize, updates);
            string? url = null;
            if (webEnabled)
            {
                app = await StartWebAsync(preferred, lan, context, log, cancellationToken);

                string bound = app.Urls.First();
                string local = bound.Replace("://0.0.0.0", "://127.0.0.1", StringComparison.Ordinal)
                    .Replace("://[::]", "://127.0.0.1", StringComparison.Ordinal);
                url = $"http://127.0.0.1:{new Uri(local).Port}";
            }

            if (lan)
            {
                log.Write("ATTENZIONE: accesso dalla rete locale attivo (ui.allowLan). Le richieste esterne chiedono la password.");
            }

            log.Write(url is null
                ? "AllSky Host avviato senza interfaccia web (ui.webEnabled = false)."
                : $"AllSky Host in ascolto su {url}" + (lan ? " e sulla rete locale" : string.Empty) + ".");
            ready = true;
            updates.Service.Start();
            return new AllSkyHostRuntime(app, engine, shutdown, log, url, publishing, updates.Service, updates.Store, youTube);
        }
        finally
        {
            if (!ready)
            {
                // Avvio fallito dopo la creazione delle risorse: si chiude tutto cio' che e' stato aperto.
                shutdown.Cancel();
                shutdown.Dispose();
                if (app is not null)
                {
                    await StopWebAfterFailureAsync(app, log);
                }

                if (publishing is not null)
                {
                    await HostPublishing.StopPublisherAsync(publishing, log);
                    await publishing.DisposeAsync();
                }

                if (youTube is not null)
                {
                    await youTube.StopAsync();
                    await youTube.DisposeAsync();
                }

                if (engine is not null)
                {
                    await StopAfterFailureAsync(engine, log);
                }
            }
        }
    }

    /// <summary>
    /// Prova le porte in ordine (la richiesta, poi le successive fino a <see cref="PortSearchSpan"/>) finche' una
    /// e' libera. La porta 0 chiede al sistema una porta libera, senza tentativi successivi.
    /// </summary>
    private static async Task<WebApplication> StartWebAsync(int preferred, bool lan, HostContext context, DailyFileLog log, CancellationToken ct)
    {
        IEnumerable<int> candidates = preferred == 0
            ? [0]
            : Enumerable.Range(preferred, Math.Min(PortSearchSpan + 1, 65536 - preferred));

        foreach (int port in candidates)
        {
            WebApplication candidate = BuildWebApplication(port, lan, context);
            try
            {
                await candidate.StartAsync(ct);
                return candidate;
            }
            catch (Exception ex) when (IsAddressInUse(ex))
            {
                log.Write($"Porta {port} occupata: provo la successiva.");
                await candidate.DisposeAsync();
            }
            catch
            {
                await candidate.DisposeAsync();
                throw;
            }
        }

        throw new InvalidOperationException(
            $"Nessuna porta libera da {preferred} a {preferred + PortSearchSpan}. Chiudere l'altra applicazione che la usa, "
            + "oppure cambiare la porta (ui.port) nelle impostazioni.");
    }

    /// <summary>Costruisce il server HTTP con gli endpoint e la UI, senza avviarlo.</summary>
    private static WebApplication BuildWebApplication(int port, bool lan, HostContext context)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
        });

        // I log passano dal file dell'Host, non dalla console.
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(lan ? $"http://0.0.0.0:{port}" : $"http://127.0.0.1:{port}");

        WebApplication app = builder.Build();

        app.Use(async (httpContext, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (httpContext.Response.HasStarted)
                {
                    // Lo stream e' gia' partito (per esempio un evento SSE): non si puo' piu' cambiare la risposta.
                    context.Log.Write("Errore durante una risposta gia' iniziata", ex);
                    return;
                }

                (int status, string message) = ErrorMapping.Map(ex, context);
                httpContext.Response.StatusCode = status;
                await httpContext.Response.WriteAsJsonAsync(new ErrorBody(message), HostJson.Options);
            }
        });

        // Richieste dalla rete locale: serve la password (HTTP Basic). Quelle dal computer stesso passano sempre.
        var lanAccess = new LanAccess();
        app.Use(async (httpContext, next) =>
        {
            if (PublishRoutes.IsLocalRequest(httpContext.Connection.RemoteIpAddress))
            {
                await next();
                return;
            }

            string? storedHash = context.Engine.GetConfig().Ui.LanPasswordHash;
            if (string.IsNullOrEmpty(storedHash))
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsJsonAsync(new ErrorBody(LanNoPasswordMessage), HostJson.Options);
                return;
            }

            if (!lanAccess.IsAuthorized(httpContext.Request.Headers["Authorization"].ToString(), storedHash))
            {
                httpContext.Response.Headers["WWW-Authenticate"] = "Basic realm=\"All Sky Brain\", charset=\"UTF-8\"";
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await httpContext.Response.WriteAsJsonAsync(new ErrorBody(LanWrongPasswordMessage), HostJson.Options);
                return;
            }

            await next();
        });

        ApiRoutes.Map(app, context);
        PublishRoutes.Map(app, context);
        UpdateRoutes.Map(app, context);

        RequestDelegate fallback = httpContext =>
        {
            if (httpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return httpContext.Response.WriteAsJsonAsync(new ErrorBody("Percorso non previsto: controllare l'indirizzo della richiesta."), HostJson.Options);
            }

            return StaticFiles.ServeAsync(httpContext, context.WebRoot);
        };
        // Pattern esplicito: il MapFallback senza pattern non copre i percorsi con un punto (index.html, js/api.js).
        app.MapFallback("{*path}", fallback);

        return app;
    }

    /// <summary>
    /// Crea il file di configurazione con i valori predefiniti se manca. La cartella dati diventa anche
    /// la cartella dell'archivio (<c>storage.folder</c>), quindi le notti finiscono in <c>DataFolder/nights</c>.
    /// Un file gia' esistente non viene toccato: la sua cartella d'archivio resta quella scelta dall'utente.
    /// </summary>
    private static AppConfig LoadOrCreateConfig(string configPath, string dataFolder, DailyFileLog log)
    {
        var store = new ConfigStore();
        if (File.Exists(configPath))
        {
            return store.Load(configPath);
        }

        AppConfig defaults = new AppConfig
        {
            Storage = new AppConfig.StorageConfig { Folder = dataFolder },
        };

        try
        {
            store.Save(configPath, defaults);
            log.Write($"Configurazione predefinita creata in '{configPath}'.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Impossibile creare la configurazione in '{configPath}': {ex.Message}. Controllare i permessi della cartella.", ex);
        }

        return defaults;
    }

    private static string EnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Impossibile creare la cartella dati '{path}': {ex.Message}. Scegliere un'altra cartella o controllarne i permessi.", ex);
        }
    }

    /// <summary>Vero se la catena di eccezioni contiene "indirizzo gia' in uso" (Kestrel la incapsula in un IOException).</summary>
    private static bool IsAddressInUse(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
            {
                return true;
            }

            if (current.GetType().Name.Contains("AddressInUse", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task StopWebAfterFailureAsync(WebApplication app, DailyFileLog log)
    {
        try
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Write("Arresto del server HTTP dopo un avvio non riuscito", ex);
        }
    }

    private static async Task StopAfterFailureAsync(AllSkyEngine engine, DailyFileLog log)
    {
        try
        {
            await engine.StopAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Write("Arresto del motore dopo un avvio non riuscito", ex);
        }
    }
}
