namespace AllSky.Telegram;

/// <summary>
/// Ingresso unico per l'invio verso Telegram. Controlli in ordine; solo l'ultimo passo usa la rete:
/// 1) invio attivo nelle impostazioni, 2) chat tra quelle autorizzate, 3) token presente,
/// 4) validazione dei dati e scelta del metodo (<see cref="DeliveryPlanner"/>), 5) coda (<see cref="SendQueue"/>).
/// I metodi di invio non leggono aggiornamenti e non aprono porte.
/// </summary>
public sealed class TelegramSender
{
    private readonly TelegramSettings _settings;
    private readonly TelegramClient _client;
    private readonly SendQueue _queue;
    private readonly TelegramLog _log;

    public TelegramSender(TelegramSettings settings, TelegramClient client, SendQueue queue, TelegramLog log)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<TelegramResult> SendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        TelegramResult? refusal = Gate(chatId) ?? TelegramLimits.CheckText(text);
        if (refusal is not null)
            return refusal;

        return await _queue.EnqueueAsync(chatId, c => _client.SendMessageAsync(chatId, text, c), ct).ConfigureAwait(false);
    }

    /// <summary>Foto. Se non rispetta i limiti di sendPhoto ma entra in 50 MB viene inviata come documento.</summary>
    public Task<TelegramResult> SendPhotoAsync(long chatId, byte[] photo, string fileName, string? caption,
        int? width = null, int? height = null, CancellationToken ct = default)
        => SendMediaAsync(chatId, MediaKind.Photo, photo, fileName, caption, width, height, ct);

    /// <summary>Video. Oltre 50 MB: rifiutato con il suggerimento della politica scelta, senza rete.</summary>
    public Task<TelegramResult> SendVideoAsync(long chatId, byte[] video, string fileName, string? caption, CancellationToken ct = default)
        => SendMediaAsync(chatId, MediaKind.Video, video, fileName, caption, null, null, ct);

    public Task<TelegramResult> SendDocumentAsync(long chatId, byte[] document, string fileName, string? caption, CancellationToken ct = default)
        => SendMediaAsync(chatId, MediaKind.Document, document, fileName, caption, null, null, ct);

    /// <summary>
    /// Prova di invio: verifica il token con getMe. Non passa dalla coda e non invia messaggi.
    /// Non richiede che l'invio sia attivo, perche' serve proprio a verificare la configurazione.
    /// </summary>
    public async Task<TelegramResult> TestConnectionAsync(CancellationToken ct = default)
    {
        if (_settings.BotToken.IsEmpty)
            return TelegramResult.Failure(TelegramErrorKind.Unauthorized, "Il token del bot non è configurato.");

        TelegramResult result = await _client.GetMeAsync(ct).ConfigureAwait(false);
        _log.Info("Prova di invio: " + result.Message);
        return result;
    }

    private async Task<TelegramResult> SendMediaAsync(long chatId, MediaKind kind, byte[] bytes, string fileName, string? caption,
        int? width, int? height, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        TelegramResult? refusal = Gate(chatId) ?? TelegramLimits.CheckCaption(caption) ?? TelegramLimits.CheckFileName(fileName);
        if (refusal is not null)
            return refusal;

        DeliveryPlan plan = DeliveryPlanner.Plan(kind, bytes.Length, width, height, _settings.OversizeVideoPolicy);
        switch (plan.Method)
        {
            case DeliveryMethod.Photo:
                return await _queue.EnqueueAsync(chatId,
                    c => _client.SendPhotoAsync(chatId, bytes, fileName, caption, width, height, c), ct).ConfigureAwait(false);

            case DeliveryMethod.Video:
                return await _queue.EnqueueAsync(chatId,
                    c => _client.SendVideoAsync(chatId, bytes, fileName, caption, true, c), ct).ConfigureAwait(false);

            case DeliveryMethod.Document:
                if (kind != MediaKind.Document)
                    _log.Info(plan.Reason);
                return await _queue.EnqueueAsync(chatId,
                    c => _client.SendDocumentAsync(chatId, bytes, fileName, caption, c), ct).ConfigureAwait(false);

            default:
                return plan.Refusal ?? TelegramResult.Failure(TelegramErrorKind.InvalidInput, plan.Reason);
        }
    }

    /// <summary>Controlli locali prima della coda: invio attivo, chat autorizzata, token e identificativo di chat.</summary>
    private TelegramResult? Gate(long chatId)
    {
        if (!_settings.Enabled)
            return TelegramResult.Failure(TelegramErrorKind.Unauthorized, "Telegram è disattivato nelle impostazioni.");
        if (!_settings.IsAuthorized(chatId))
            return TelegramResult.Failure(TelegramErrorKind.Unauthorized, "La chat non è tra quelle autorizzate: nessun invio.");
        if (_settings.BotToken.IsEmpty)
            return TelegramResult.Failure(TelegramErrorKind.Unauthorized, "Il token del bot non è configurato.");
        return TelegramLimits.CheckChatId(chatId);
    }
}
