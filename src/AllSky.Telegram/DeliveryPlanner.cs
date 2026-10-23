namespace AllSky.Telegram;

/// <summary>Tipo di file da inviare.</summary>
public enum MediaKind { Photo, Video, Document }

/// <summary>Cosa fare di un video oltre 50 MB: scelta dell'utente (<see cref="TelegramSettings.OversizeVideoPolicy"/>).</summary>
public enum OversizeVideoPolicy
{
    /// <summary>Suggerisce di ricodificare il video a una dimensione minore.</summary>
    Reencode,
    /// <summary>Suggerisce di inviare keogram e startrails con un link al video.</summary>
    SendStillsWithLink,
}

/// <summary>Metodo scelto per l'invio.</summary>
public enum DeliveryMethod { Photo, Video, Document, Refused }

/// <summary>
/// Esito della pianificazione. Se <see cref="Method"/> e' <see cref="DeliveryMethod.Refused"/>, <see cref="Refusal"/>
/// contiene l'errore da restituire senza inviare nulla. <see cref="Reason"/> e' una frase italiana per il registro.
/// </summary>
public sealed record DeliveryPlan(DeliveryMethod Method, string Reason, TelegramResult? Refusal);

/// <summary>
/// Sceglie il metodo di invio di un file. Funzione pura: nessuna rete, nessun orologio.
/// Foto entro i limiti di sendPhoto: foto. Foto oltre quei limiti ma entro 50 MB: documento.
/// Video entro 50 MB: video. Oltre: rifiutato con il suggerimento secondo la politica scelta.
/// </summary>
public static class DeliveryPlanner
{
    public static DeliveryPlan Plan(MediaKind kind, long sizeBytes, int? width, int? height, OversizeVideoPolicy policy)
    {
        if (sizeBytes <= 0)
            return Refuse(TelegramErrorKind.InvalidInput, "Il file è vuoto.");

        switch (kind)
        {
            case MediaKind.Photo:
                return PlanPhoto(sizeBytes, width, height);

            case MediaKind.Video:
                if (sizeBytes <= TelegramLimits.FileMaxBytes)
                    return Plan(DeliveryMethod.Video, "Video entro il limite di 50 MB.");
                return Refuse(TelegramErrorKind.TooLarge, OversizeVideoMessage(policy));

            default:
                if (sizeBytes <= TelegramLimits.FileMaxBytes)
                    return Plan(DeliveryMethod.Document, "Documento entro il limite di 50 MB.");
                return Refuse(TelegramErrorKind.TooLarge, "Il documento supera 50 MB: comprimilo o condividi un link.");
        }
    }

    private static DeliveryPlan PlanPhoto(long sizeBytes, int? width, int? height)
    {
        if (width is <= 0 || height is <= 0)
            return Refuse(TelegramErrorKind.InvalidInput, "Le dimensioni della foto non sono valide.");

        if (sizeBytes <= TelegramLimits.PhotoMaxBytes
            && (width is null || height is null || TelegramLimits.PhotoDimensionsFit(width.Value, height.Value)))
        {
            return Plan(DeliveryMethod.Photo, "Foto entro i limiti di sendPhoto.");
        }

        if (sizeBytes <= TelegramLimits.FileMaxBytes)
            return Plan(DeliveryMethod.Document, "La foto supera i limiti di sendPhoto (peso, somma delle dimensioni o rapporto): inviata come documento.");

        return Refuse(TelegramErrorKind.TooLarge, "La foto supera 50 MB: ridimensionala o comprimila.");
    }

    private static string OversizeVideoMessage(OversizeVideoPolicy policy) => policy switch
    {
        OversizeVideoPolicy.SendStillsWithLink
            => "Il video supera 50 MB. Suggerimento: invia keogram e startrails con un link al video.",
        _ => "Il video supera 50 MB. Suggerimento: ricodifica il video a una dimensione più piccola e riprova.",
    };

    private static DeliveryPlan Plan(DeliveryMethod method, string reason) => new(method, reason, null);

    private static DeliveryPlan Refuse(TelegramErrorKind kind, string message)
        => new(DeliveryMethod.Refused, message, TelegramResult.Failure(kind, message));
}
