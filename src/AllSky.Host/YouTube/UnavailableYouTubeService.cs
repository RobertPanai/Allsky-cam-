using AllSky.Core;
using AllSky.Core.YouTube;

namespace AllSky.Host;

/// <summary>
/// Servizio di ripiego quando il caricamento YouTube non parte in questo avvio (per esempio il registro e' illeggibile).
/// L'Host resta utilizzabile; la vista mostra il problema e le azioni non fanno nulla.
/// </summary>
internal sealed class UnavailableYouTubeService : IYouTubeService
{
    public static UnavailableYouTubeService Instance { get; } = new();

    private static readonly YouTubeStatus StatusValue = new(false, false, null, false, null, null, YouTubeProblem.Unavailable);

    public YouTubeStatus GetStatus() => StatusValue;

    public YouTubeSettingsSnapshot GetSettings() =>
        YouTubeSettingsRules.ToSnapshot(new AppConfig.YouTubeConfig());

    // Nessuna notifica: lo stato non cambia mai in questa istanza.
    public event Action<YouTubeStatus>? StatusChanged { add { } remove { } }

    public Task<YouTubeLoginResult> LoginAsync(string clientId, string clientSecret, CancellationToken cancellationToken) =>
        Task.FromResult(new YouTubeLoginResult(false, YouTubeProblem.Unavailable));

    public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public YouTubeSaveResult SaveSettings(YouTubeSettingsChange change) => new(false, []);

    public Task<YouTubeUploadOutcome> UploadLastNightNowAsync(bool acceptUncertainRisk, CancellationToken cancellationToken) =>
        Task.FromResult(new YouTubeUploadOutcome(YouTubeUploadOutcomeKind.Unavailable, null));
}
