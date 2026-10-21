using AllSky.Updates;
using Velopack;
using Velopack.Sources;

namespace AllSky.Tray.Updates;

/// <summary>
/// Backend degli aggiornamenti basato su Velopack (licenza MIT). Sta nel Tray perché Velopack è solo Windows:
/// l'Host riceve l'interfaccia <see cref="IUpdateBackend"/> e non dipende da Velopack.
/// </summary>
/// <remarks>
/// Velopack richiede che <c>VelopackApp.Build().Run()</c> sia la prima istruzione di Main (vedi Program.cs).
/// </remarks>
internal sealed class VelopackBackend : IUpdateBackend
{
    /// <summary>Repository GitHub delle release di AllSky Brain. È l'unico punto del progetto in cui compare questo indirizzo.</summary>
    internal const string RepositoryUrl = "https://github.com/RobertPanai/Allsky-cam-";

    private readonly UpdateManager _manager;
    private readonly object _gate = new();
    private UpdateInfo? _pending; // _gate

    private VelopackBackend(UpdateManager manager)
    {
        _manager = manager;
    }

    /// <summary>
    /// Crea il backend. Se Velopack non si inizializza restituisce un backend non disponibile: l'app parte lo stesso,
    /// con gli aggiornamenti disabilitati.
    /// </summary>
    public static IUpdateBackend Create()
    {
        try
        {
            // Repository pubblico: nessun token. Solo release stabili.
            return new VelopackBackend(new UpdateManager(new GithubSource(RepositoryUrl, null, false)));
        }
        catch (Exception)
        {
            return new UnavailableUpdateBackend();
        }
    }

    /// <inheritdoc />
    public bool IsInstalled => _manager.IsInstalled;

    /// <inheritdoc />
    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    /// <inheritdoc />
    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        // CheckForUpdatesAsync (Velopack 1.x) non accetta un CancellationToken: il timeout lo applica UpdateService.
        UpdateInfo? info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _pending = info;
        }

        return info?.TargetFullRelease.Version.ToString();
    }

    /// <inheritdoc />
    public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        UpdateInfo pending = Pending();
        return _manager.DownloadUpdatesAsync(pending, percent => progress.Report(percent), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Velopack avvia il programma di aggiornamento, che attende l'uscita di AllSky e poi installa e riavvia.
    /// Chi chiama deve chiudere l'app subito dopo: il programma attende al massimo 60 secondi.
    /// </remarks>
    public void ApplyAndRestartLater()
    {
        _manager.WaitExitThenApplyUpdates(Pending().TargetFullRelease, silent: false, restart: true);
    }

    private UpdateInfo Pending()
    {
        lock (_gate)
        {
            return _pending ?? throw new InvalidOperationException("Nessun aggiornamento da scaricare o installare.");
        }
    }
}
