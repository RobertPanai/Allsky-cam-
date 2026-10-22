using System.Diagnostics;
using AllSky.Core;
using AllSky.Core.YouTube;
using AllSky.Engine;
using AllSky.Publish;
using AllSky.YouTube;

namespace AllSky.Host;

/// <summary>
/// Caricamento automatico YouTube dentro l'Host: servizio per la vista, ciclo in background e client HTTP.
/// Creato da <see cref="TryCreate"/>; se la creazione fallisce l'Host parte senza (vedi <see cref="IYouTubeService"/> di ripiego).
/// </summary>
internal sealed class YouTubeServices : IAsyncDisposable
{
    /// <summary>Intervallo del ciclo in background.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(10);

    private readonly HostYouTubeService _service;
    private readonly YouTubeUploadWorker _worker;
    private readonly HttpClient _oauthHttp;
    private readonly HttpClient _uploadHttp;

    private YouTubeServices(HostYouTubeService service, YouTubeUploadWorker worker, HttpClient oauthHttp, HttpClient uploadHttp)
    {
        _service = service;
        _worker = worker;
        _oauthHttp = oauthHttp;
        _uploadHttp = uploadHttp;
    }

    /// <summary>Servizio da esporre alla vista.</summary>
    public IYouTubeService Service => _service;

    /// <summary>
    /// Crea i servizi. Errori (per esempio registro danneggiato) si registrano nel log e restituiscono null:
    /// l'Host parte comunque.
    /// </summary>
    public static YouTubeServices? TryCreate(HostOptions options, string dataFolder, IAllSkyService engine, DailyFileLog log)
    {
        try
        {
            return Create(options, dataFolder, engine, log);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Write("Caricamento YouTube non disponibile: l'applicazione parte senza.", ex);
            return null;
        }
    }

    /// <summary>Avvia il ciclo in background. Il primo tick parte subito.</summary>
    public void Start() => _worker.Start();

    /// <summary>Ferma il ciclo (annullando un caricamento in corso) e lo scheduler. Non lancia.</summary>
    public async Task StopAsync()
    {
        _service.Stop();
        await _worker.StopAsync().ConfigureAwait(false);
    }

    /// <summary>Chiude i client HTTP. Chiamare dopo <see cref="StopAsync"/>.</summary>
    public ValueTask DisposeAsync()
    {
        _oauthHttp.Dispose();
        _uploadHttp.Dispose();
        return ValueTask.CompletedTask;
    }

    private static YouTubeServices Create(HostOptions options, string dataFolder, IAllSkyService engine, DailyFileLog log)
    {
        AllSky.Publish.ISecretProtector inner = options.SecretProtector
            ?? (OperatingSystem.IsWindows() ? new DpapiSecretProtector() : new UnsupportedSecretProtector());
        var protector = new YouTubeSecretProtectorAdapter(inner);
        var clock = new YouTubeClock();
        var zone = YouTubeConfigMapper.ResolveZone(engine.GetConfig().Site.Timezone);

        var store = new YouTubeAuthStore(Path.Combine(dataFolder, "youtube-auth.json"), protector);
        var journal = new UploadJournal(Path.Combine(dataFolder, "youtube-uploads.json"), protector);

        var oauthHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var uploadHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        try
        {
            var oauth = new YouTubeOAuthService(new GoogleOAuthClient(oauthHttp), store);
            var client = new ResumableUploadClient(uploadHttp, new OAuthAccessTokenSource(oauth, clock));
            var uploader = new NightUploader(client, journal, clock, zone);
            var provider = new NightArchiveLatestTimelapseProvider(engine.GetConfig, new SystemClock());
            var scheduler = new YouTubeUploadScheduler(
                clock,
                zone,
                provider,
                uploader,
                journal,
                () => YouTubeConfigMapper.ToUploadSettings(engine.GetConfig().YouTube));

            Func<Uri, CancellationToken, Task> openBrowser = options.YouTubeOpenBrowser ?? DefaultOpenBrowser;
            Action<string> logLine = message => log.Write(message);
            var service = new HostYouTubeService(engine, oauth, store, journal, scheduler, clock, openBrowser, logLine);
            var worker = new YouTubeUploadWorker(service.TickAsync, service.ShouldRun, TickInterval, logLine);
            return new YouTubeServices(service, worker, oauthHttp, uploadHttp);
        }
        catch
        {
            oauthHttp.Dispose();
            uploadHttp.Dispose();
            throw;
        }
    }

    /// <summary>Apre l'URL nel browser predefinito. Non riceve nessun segreto: solo l'URL di autorizzazione.</summary>
    private static Task DefaultOpenBrowser(Uri url, CancellationToken cancellationToken)
    {
        using Process? process = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    /// <summary>Orologio del modulo YouTube: ora UTC di sistema.</summary>
    private sealed class YouTubeClock : AllSky.YouTube.IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
