using AllSky.Core.Publication;
using AllSky.Publish;

namespace AllSky.Host;

/// <summary>
/// Adattatore di <see cref="IPublishSettingsService"/> per la vista Pubblicazione del desktop. Usa gli stessi servizi
/// e la stessa logica delle rotte <c>/api/publish/*</c> (<see cref="PublishSettingsOperations"/>), senza il controllo
/// del computer locale: l'app desktop gira nello stesso processo. Il token non esce mai: <see cref="GetSettings"/>
/// riporta solo <see cref="PublicationSettingsSnapshot.HasToken"/>.
/// Senza servizi di pubblicazione (il modulo non e' partito) risponde con stato "disabled" e salvataggi rifiutati.
/// </summary>
internal sealed class HostPublishSettingsService : IPublishSettingsService
{
    private readonly PublishServices? _services;

    /// <param name="services">Servizi della pubblicazione; null se il modulo non e' partito.</param>
    public HostPublishSettingsService(PublishServices? services)
    {
        _services = services;
        if (services is not null)
        {
            services.Publisher.StatusChanged += OnPublisherStatusChanged;
        }
    }

    /// <inheritdoc/>
    public event Action<PublicationStatusSnapshot>? StatusChanged;

    /// <inheritdoc/>
    public PublicationSettingsSnapshot GetSettings()
    {
        PublishSettings s = PublishSettingsOperations.Current(_services);
        return new PublicationSettingsSnapshot(
            Enabled: s.Enabled,
            ServerUrl: s.ServerUrl,
            DeviceId: s.DeviceId,
            HasToken: !s.Token.IsEmpty,
            ProfileName: s.ProfileName,
            IntervalSeconds: s.IntervalSeconds,
            LongEdgePx: s.LongEdgePx,
            JpegQuality: s.JpegQuality,
            ExtraHeaderName: s.ExtraHeaderName,
            HasExtraHeaderValue: !s.ExtraHeaderValue.IsEmpty);
    }

    /// <inheritdoc/>
    public PublicationStatusSnapshot GetStatus()
    {
        return _services is null
            ? new PublicationStatusSnapshot(PublicationState.Disabled, null, null, null, null, 0, 0, 0, null)
            : ToSnapshot(_services.Publisher.GetStatus(), _services.Redactor);
    }

    /// <inheritdoc/>
    public PublicationSaveResult SaveSettings(PublicationSettingsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        try
        {
            PublishSettingsOperations.SaveOutcome outcome = PublishSettingsOperations.Save(_services, ToBody(change));
            if (outcome.Ok)
            {
                return PublicationSaveResult.Success;
            }

            var issues = outcome.Errors.Select(e => new PublicationIssue(e.Path, e.Message)).ToArray();
            return new PublicationSaveResult(false, issues);
        }
        catch (Exception ex)
        {
            _services?.Log.Error("Salvataggio della pubblicazione non riuscito", ex);
            return new PublicationSaveResult(false, new[] { new PublicationIssue("publish", "Salvataggio non riuscito. Controllare il file di log dell'applicazione.") });
        }
    }

    /// <inheritdoc/>
    public async Task<PublicationTestResult> TestConnectionAsync(PublicationSettingsChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        try
        {
            PublishSettingsOperations.TestOutcome outcome = await PublishSettingsOperations.TestAsync(_services, ToBody(change), cancellationToken);
            return new PublicationTestResult(outcome.Ok, outcome.Code, outcome.Message, outcome.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PublicationTestResult(false, "cancelled", "Prova annullata.", null);
        }
        catch (Exception ex)
        {
            _services?.Log.Error("Prova di connessione della pubblicazione non riuscita", ex);
            return new PublicationTestResult(false, "unknown", "Prova di connessione non riuscita. Controllare il file di log dell'applicazione.", null);
        }
    }

    private void OnPublisherStatusChanged(PublishStatus status)
    {
        Action<PublicationStatusSnapshot>? handler = StatusChanged;
        if (handler is null || _services is null)
        {
            return;
        }

        handler(ToSnapshot(status, _services.Redactor));
    }

    /// <summary>
    /// Converte il modulo della vista nel corpo delle rotte. Il modulo invia i valori completi: un nome del profilo o un
    /// header assente (null) significa "vuoto", quindi cancella quello salvato. I segreti null restano invariati.
    /// </summary>
    private static PublishRoutes.PublishSettingsBody ToBody(PublicationSettingsChange c) => new()
    {
        Enabled = c.Enabled,
        ServerUrl = c.ServerUrl,
        DeviceId = c.DeviceId,
        ProfileName = c.ProfileName ?? string.Empty,
        IntervalSeconds = c.IntervalSeconds,
        LongEdgePx = c.LongEdgePx,
        JpegQuality = c.JpegQuality,
        ExtraHeaderName = c.ExtraHeaderName ?? string.Empty,
        Token = c.NewToken,
        ClearToken = c.ClearToken,
        ExtraHeaderValue = c.NewExtraHeaderValue,
        ClearExtraHeaderValue = c.ClearExtraHeaderValue,
    };

    private static PublicationStatusSnapshot ToSnapshot(PublishStatus status, Redactor redactor) => new(
        State: ToState(status.State),
        LastSuccessUtc: status.LastSuccessUtc,
        LastErrorCode: status.LastErrorCode,
        LastError: PublishSettingsOperations.SanitizeError(status.LastError, redactor),
        LastErrorUtc: status.LastErrorUtc,
        QueueCount: status.QueueCount,
        SentTotal: status.SentTotal,
        DiscardedTotal: status.DiscardedTotal,
        NextAttemptUtc: status.NextAttemptUtc);

    private static PublicationState ToState(PublishState state) => state switch
    {
        PublishState.Disabled => PublicationState.Disabled,
        PublishState.Idle => PublicationState.Idle,
        PublishState.Sending => PublicationState.Sending,
        PublishState.Backoff => PublicationState.Backoff,
        PublishState.Suspended => PublicationState.Suspended,
        _ => PublicationState.Unknown,
    };
}
