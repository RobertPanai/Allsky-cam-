using AllSky.Core;
using AllSky.Core.YouTube;
using AllSky.YouTube;

namespace AllSky.Host;

/// <summary>Traduce la sezione <c>youtube</c> della configurazione nelle impostazioni del caricamento. Nessuna rete.</summary>
internal static class YouTubeConfigMapper
{
    /// <summary>Fuso orario dell'osservatorio; se il nome non esiste, UTC (come il motore).</summary>
    public static TimeZoneInfo ResolveZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Nome sconosciuto su questo sistema: si usa UTC.
            }
        }

        return TimeZoneInfo.Utc;
    }

    /// <summary>Errori della sezione, comprese le sintassi dei modelli di titolo e descrizione. Lista vuota se valida.</summary>
    public static IReadOnlyList<ValidationError> Errors(AppConfig.YouTubeConfig config)
    {
        var errors = YouTubeSettingsRules.ValidateConfig(config).ToList();

        TemplateParseResult<TitleTemplate> title = TitleTemplate.Parse(config.TitleTemplate);
        if (title.Template is null)
        {
            errors.Add(new ValidationError("youtube.titleTemplate", "Titolo: " + string.Join(" ", title.Errors.Select(e => e.Message))));
        }

        TemplateParseResult<DescriptionTemplate> description = DescriptionTemplate.Parse(config.DescriptionTemplate);
        if (description.Template is null)
        {
            errors.Add(new ValidationError("youtube.descriptionTemplate", "Descrizione: " + string.Join(" ", description.Errors.Select(e => e.Message))));
        }

        return errors;
    }

    /// <summary>Impostazioni dello scheduler. Lancia <see cref="ArgumentException"/> se la sezione non e' valida.</summary>
    public static YouTubeUploadSettings ToUploadSettings(AppConfig.YouTubeConfig config)
    {
        IReadOnlyList<ValidationError> errors = Errors(config);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors.Select(e => e.Message)));
        }

        YouTubeSettingsRules.TryParseDailyTime(config.DailyTime, out TimeOnly time);
        return new YouTubeUploadSettings(
            time,
            TimeSpan.FromHours(config.CatchUpWindowHours),
            ToPrivacy(config.Privacy),
            config.Tags.Select(t => t.Trim()).ToList(),
            config.CategoryId,
            config.Location,
            config.TitleTemplate,
            config.DescriptionTemplate,
            MaxUploadsPerDay: 1);
    }

    private static YouTubePrivacy ToPrivacy(string privacy) => privacy switch
    {
        "unlisted" => YouTubePrivacy.Unlisted,
        "public" => YouTubePrivacy.Public,
        _ => YouTubePrivacy.Private,
    };
}
