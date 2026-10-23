using System.IO;

namespace AllSky.Telegram;

/// <summary>
/// Limiti di invio da docs/TELEGRAM-API.md. I valori di testo, didascalia, foto e file sono letti da copia della
/// documentazione (aiogram 3.27.0, [V via copia]); la pagina ufficiale non era leggibile. Da confrontare con
/// https://core.telegram.org/bots/api prima del rilascio.
/// Le MB sono decimali (10 MB = 10.000.000 byte): e' il valore piu' prudente tra i due significati.
/// Ogni controllo restituisce null se il valore e' accettabile, altrimenti l'errore da restituire senza chiamare la rete.
/// </summary>
public static class TelegramLimits
{
    /// <summary>Testo di sendMessage: 1..4096 caratteri (UTF-16, come nel conteggio di .NET).</summary>
    public const int TextMaxChars = 4096;

    /// <summary>Didascalia di foto, video e documenti: 0..1024 caratteri.</summary>
    public const int CaptionMaxChars = 1024;

    /// <summary>Peso massimo di una foto inviata con sendPhoto.</summary>
    public const long PhotoMaxBytes = 10_000_000;

    /// <summary>Larghezza + altezza massime di una foto inviata con sendPhoto.</summary>
    public const int PhotoMaxDimensionSum = 10_000;

    /// <summary>Rapporto massimo tra lato lungo e lato corto di una foto inviata con sendPhoto.</summary>
    public const int PhotoMaxAspectRatio = 20;

    /// <summary>Peso massimo per video e documenti inviati da un bot (qualsiasi tipo).</summary>
    public const long FileMaxBytes = 50_000_000;

    public static TelegramResult? CheckChatId(long chatId)
        => chatId == 0 ? Invalid("Identificativo di chat non valido.") : null;

    public static TelegramResult? CheckText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Invalid("Il testo del messaggio è vuoto.");
        if (text.Length > TextMaxChars)
            return Invalid($"Il testo supera {TextMaxChars} caratteri ({text.Length}).");
        return null;
    }

    public static TelegramResult? CheckCaption(string? caption)
    {
        if (caption is null || caption.Length <= CaptionMaxChars)
            return null;
        return Invalid($"La didascalia supera {CaptionMaxChars} caratteri ({caption.Length}).");
    }

    /// <summary>Il nome deve essere un nome di file semplice: senza percorso, senza caratteri di controllo e senza virgolette.</summary>
    public static TelegramResult? CheckFileName(string? fileName)
        => NormalizeFileName(fileName) is null ? Invalid("Nome del file non valido.") : null;

    /// <summary>Nome file normalizzato (solo il nome finale), oppure null se non utilizzabile.</summary>
    public static string? NormalizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        string name = Path.GetFileName(fileName.Trim().Replace('\\', '/'));
        if (name.Length == 0)
            return null;
        foreach (char c in name)
        {
            if (char.IsControl(c) || c == '"')
                return null;
        }
        return name;
    }

    /// <summary>Peso del file: vuoto e' un errore di input, oltre il massimo e' TooLarge.</summary>
    public static TelegramResult? CheckSize(long bytes, long maxBytes, string what)
    {
        if (bytes <= 0)
            return Invalid("Il file è vuoto.");
        if (bytes > maxBytes)
            return TelegramResult.Failure(TelegramErrorKind.TooLarge, $"{what} supera il limite di {maxBytes / 1_000_000} MB per i bot.");
        return null;
    }

    /// <summary>Dimensioni di una foto: se sono note devono essere positive e rispettare somma e rapporto. Se non sono note, solo il peso conta.</summary>
    public static TelegramResult? CheckPhotoDimensions(int? width, int? height)
    {
        if (width is null || height is null)
            return null;
        if (width <= 0 || height <= 0)
            return Invalid("Le dimensioni della foto non sono valide.");
        if (!PhotoDimensionsFit(width.Value, height.Value))
            return Invalid("La foto supera i limiti di sendPhoto (somma delle dimensioni o rapporto): usa sendDocument.");
        return null;
    }

    /// <summary>Vero se dimensioni positive note rispettano somma e rapporto di sendPhoto.</summary>
    internal static bool PhotoDimensionsFit(int width, int height)
    {
        if ((long)width + height > PhotoMaxDimensionSum)
            return false;
        long min = Math.Min(width, height);
        long max = Math.Max(width, height);
        return max <= (long)PhotoMaxAspectRatio * min;
    }

    internal static TelegramResult Invalid(string message)
        => TelegramResult.Failure(TelegramErrorKind.InvalidInput, message);
}
