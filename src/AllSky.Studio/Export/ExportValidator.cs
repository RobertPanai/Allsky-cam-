using System.Globalization;
using AllSky.Telegram;
using AllSky.YouTube;

namespace AllSky.Studio;

/// <summary>
/// Controlli di preset, destinazioni ed esportazioni. Non lanciano mai: ogni problema è una riga di testo in
/// italiano, senza percorsi completi e senza valori di segreti.
/// </summary>
public static class ExportValidator
{
    private const int MaxFolderLength = 1024;
    private const int MaxYouTubeErrorsShown = 3;

    /// <summary>Controlla un preset. Lista vuota = valido.</summary>
    public static IReadOnlyList<string> ValidatePreset(ExportPreset? preset)
    {
        var errors = new List<string>();
        if (preset is null)
        {
            errors.Add("preset mancante.");
            return errors;
        }

        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            errors.Add("il preset deve avere un nome.");
        }
        else if (preset.Name.Length > ExportPresets.MaxNameLength)
        {
            errors.Add($"il nome del preset è troppo lungo (massimo {ExportPresets.MaxNameLength} caratteri).");
        }
        else if (preset.Name.Any(char.IsControl))
        {
            errors.Add("il nome del preset contiene caratteri non ammessi.");
        }

        if (!IsValidDimension(preset.Width))
        {
            errors.Add($"larghezza non valida: da {ExportPresets.MinDimension} a {ExportPresets.MaxDimension} pixel, oppure 0 per la sorgente.");
        }

        if (!IsValidDimension(preset.Height))
        {
            errors.Add($"altezza non valida: da {ExportPresets.MinDimension} a {ExportPresets.MaxDimension} pixel, oppure 0 per la sorgente.");
        }

        if ((preset.Width > 0 && preset.Width % 2 != 0) || (preset.Height > 0 && preset.Height % 2 != 0))
        {
            errors.Add("larghezza e altezza devono essere pari.");
        }

        if (preset.Fps != 0 && (preset.Fps < 1 || preset.Fps > ExportPresets.MaxFps))
        {
            errors.Add($"frame rate non valido: da 1 a {ExportPresets.MaxFps}, oppure 0 per quello del montaggio.");
        }

        if (preset.Quality < ExportPresets.MinQuality || preset.Quality > ExportPresets.MaxQuality)
        {
            errors.Add($"qualità non valida: da {ExportPresets.MinQuality} a {ExportPresets.MaxQuality}.");
        }

        if (preset.Container != ExportPresets.Mp4)
        {
            errors.Add("contenitore non supportato: per ora solo mp4.");
        }

        return errors;
    }

    /// <summary>Controlla una destinazione, con i suoi campi specifici. Lista vuota = valida.</summary>
    public static IReadOnlyList<string> ValidateDestination(ExportDestination? destination)
    {
        switch (destination)
        {
            case null:
                return ["destinazione mancante."];

            case LocalFileDestination local:
                return ValidateLocal(local);

            case YouTubeDestination youtube:
                return ValidateYouTube(youtube);

            case TelegramDestination telegram:
                return ValidateTelegram(telegram);

            default:
                return ["tipo di destinazione non riconosciuto."];
        }
    }

    /// <summary>Controlla preset e destinazione insieme. Lista vuota = valida.</summary>
    public static IReadOnlyList<string> ValidateTarget(ExportTarget? target)
    {
        if (target is null)
        {
            return ["esportazione mancante."];
        }

        var errors = new List<string>(ValidatePreset(target.Preset));
        errors.AddRange(ValidateDestination(target.Destination));
        return errors;
    }

    /// <summary>Identificativo di chat: intero diverso da zero, anche negativo, scritto come testo.</summary>
    public static bool TryParseChatId(string? text, out long chatId)
    {
        chatId = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out chatId)
            && TelegramLimits.CheckChatId(chatId) is null;
    }

    /// <summary>Privacy in forma di testo: "private", "unlisted" o "public" (minuscolo, senza variazioni).</summary>
    public static bool TryParsePrivacy(string? text, out YouTubePrivacy privacy)
    {
        switch (text)
        {
            case "private":
                privacy = YouTubePrivacy.Private;
                return true;
            case "unlisted":
                privacy = YouTubePrivacy.Unlisted;
                return true;
            case "public":
                privacy = YouTubePrivacy.Public;
                return true;
            default:
                privacy = YouTubePrivacy.Private;
                return false;
        }
    }

    private static bool IsValidDimension(int value) =>
        value == 0 || value >= ExportPresets.MinDimension && value <= ExportPresets.MaxDimension;

    private static List<string> ValidateLocal(LocalFileDestination local)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(local.Folder))
        {
            errors.Add("la cartella di destinazione è vuota.");
        }
        else if (local.Folder.Length > MaxFolderLength)
        {
            errors.Add("il percorso della cartella è troppo lungo.");
        }
        else if (local.Folder.Any(char.IsControl))
        {
            errors.Add("la cartella contiene caratteri non ammessi.");
        }

        errors.AddRange(ExportFileNames.ValidateTemplate(local.FileNameTemplate));

        if (!Enum.IsDefined(local.Overwrite))
        {
            errors.Add("politica di sovrascrittura sconosciuta: usare Fail, Rename o Overwrite.");
        }

        return errors;
    }

    private static List<string> ValidateYouTube(YouTubeDestination youtube)
    {
        var errors = new List<string>();

        TemplateParseResult<TitleTemplate> title = TitleTemplate.Parse(youtube.Title);
        if (!title.IsValid)
        {
            errors.AddRange(title.Errors.Take(MaxYouTubeErrorsShown).Select(e => "modello del titolo: " + e.Message));
        }

        TemplateParseResult<DescriptionTemplate> description = DescriptionTemplate.Parse(youtube.Description ?? string.Empty);
        if (!description.IsValid)
        {
            errors.AddRange(description.Errors.Take(MaxYouTubeErrorsShown).Select(e => "modello della descrizione: " + e.Message));
        }

        if (!TryParsePrivacy(youtube.Privacy, out _))
        {
            errors.Add("privacy sconosciuta: usare private, unlisted o public.");
        }

        return errors;
    }

    private static List<string> ValidateTelegram(TelegramDestination telegram)
    {
        var errors = new List<string>();
        if (!TryParseChatId(telegram.ChatId, out _))
        {
            errors.Add("l'identificativo della chat deve essere un numero intero diverso da zero.");
        }

        if (telegram.Caption is not null && telegram.Caption.Length > TelegramLimits.CaptionMaxChars)
        {
            errors.Add($"la didascalia supera {TelegramLimits.CaptionMaxChars} caratteri.");
        }

        return errors;
    }
}
