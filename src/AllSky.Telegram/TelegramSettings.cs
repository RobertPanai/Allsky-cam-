using AllSky.Publish;

namespace AllSky.Telegram;

/// <summary>
/// Impostazioni Telegram. Il token e' un <see cref="Secret"/>: il suo ToString non lo rivela, quindi nemmeno il ToString
/// di questo record. Su disco il token e' cifrato da <see cref="TelegramSettingsStore"/>.
/// Di default l'invio e' spento e nessuna chat e' autorizzata.
/// </summary>
public sealed record TelegramSettings
{
    public bool Enabled { get; init; }

    /// <summary>Token del bot ricevuto da BotFather. Segreto.</summary>
    public Secret BotToken { get; init; }

    /// <summary>Chat alle quali il modulo puo' inviare. Tutto il resto viene rifiutato prima della rete.</summary>
    public IReadOnlyList<long> AuthorizedChatIds { get; init; } = Array.Empty<long>();

    /// <summary>Messaggi ammessi al giorno (conteggio UTC). Default 50: scelta locale, non un limite di Telegram.</summary>
    public int DailyLimit { get; init; } = SendQueue.DefaultDailyLimit;

    /// <summary>Politica per i video oltre 50 MB.</summary>
    public OversizeVideoPolicy OversizeVideoPolicy { get; init; } = OversizeVideoPolicy.SendStillsWithLink;

    public bool IsAuthorized(long chatId) => AuthorizedChatIds.Contains(chatId);
}
