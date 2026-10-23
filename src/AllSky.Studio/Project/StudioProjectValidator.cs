using AllSky.Archive;

namespace AllSky.Studio;

/// <summary>
/// Controlli di coerenza di un progetto. I messaggi sono in italiano, senza percorsi completi.
/// </summary>
internal static class StudioProjectValidator
{
    internal const int MaxNameLength = 200;
    internal const int MaxPathLength = 1024;
    internal const double MinIntervalSeconds = 0.1;
    internal const double MaxIntervalSeconds = 3600;

    /// <summary>Lancia <see cref="StudioException"/> con tutti i problemi trovati (al massimo cinque).</summary>
    internal static void Validate(StudioProject project, string fileName)
    {
        var problems = new List<string>();

        if (project.Version != StudioProjectStore.CurrentVersion)
        {
            problems.Add(project.Version < 1
                ? "numero di versione non valido."
                : $"versione del progetto non supportata: {project.Version}.");
        }

        if (string.IsNullOrWhiteSpace(project.Name))
        {
            problems.Add("il progetto deve avere un nome.");
        }
        else if (project.Name.Length > MaxNameLength)
        {
            problems.Add("il nome del progetto è troppo lungo.");
        }

        if (project.LocalTimeZoneId is not null && !LocalTime.TryResolveZone(project.LocalTimeZoneId, out _))
        {
            problems.Add("fuso orario locale non riconosciuto.");
        }

        if (project.Sources is null)
        {
            problems.Add("elenco delle sorgenti mancante.");
        }
        else
        {
            for (int i = 0; i < project.Sources.Count; i++)
            {
                string? error = ValidateSource(project.Sources[i]);
                if (error is not null)
                {
                    problems.Add($"sorgente {i + 1}: {error}");
                }
            }
        }

        if (project.Exports is null)
        {
            problems.Add("elenco delle esportazioni mancante.");
        }
        else
        {
            for (int i = 0; i < project.Exports.Count; i++)
            {
                IReadOnlyList<string> exportErrors = ExportValidator.ValidateTarget(project.Exports[i]);
                if (exportErrors.Count > 0)
                {
                    problems.Add($"esportazione {i + 1}: {exportErrors[0]}");
                }
            }
        }

        if (project.Operations is null)
        {
            problems.Add("elenco delle operazioni mancante.");
        }
        else
        {
            for (int i = 0; i < project.Operations.Count; i++)
            {
                StudioOperation? operation = project.Operations[i];
                if (operation is null)
                {
                    problems.Add($"operazione {i + 1}: voce vuota.");
                }
                else if (string.IsNullOrWhiteSpace(operation.Type))
                {
                    problems.Add($"operazione {i + 1}: tipo mancante.");
                }
                else if (operation.Parameters is null)
                {
                    problems.Add($"operazione {i + 1}: parametri mancanti.");
                }
            }
        }

        if (problems.Count > 0)
        {
            string shown = string.Join(" ", problems.Take(5));
            throw new StudioException($"il progetto «{fileName}» contiene errori: {shown}");
        }
    }

    /// <summary>Controlla una regola di data. Restituisce il messaggio di errore oppure null.</summary>
    /// <param name="rule">Regola da controllare.</param>
    /// <param name="pattern">Modello compilato, solo con <see cref="TimestampRuleModes.FileNamePattern"/>.</param>
    internal static string? ValidateRule(TimestampRule rule, out FileNameTemplate? pattern)
    {
        pattern = null;
        switch (rule.Mode)
        {
            case TimestampRuleModes.Exif:
            case TimestampRuleModes.FileModifiedTime:
                return null;
            case TimestampRuleModes.FileNamePattern:
                return FileNameTemplate.TryCompile(rule.FileNamePattern, out pattern, out string? error)
                    ? null
                    : error;
            default:
                return "modalità di data non riconosciuta.";
        }
    }

    private static string? ValidateSource(StudioSource? source)
    {
        if (source is null)
        {
            return "voce vuota.";
        }

        switch (source.Kind)
        {
            case StudioSourceKinds.ArchiveNight:
                return NightArchive.IsValidNightId(source.NightId)
                    ? null
                    : "identificatore di notte non valido (atteso AAAA-MM-GG).";

            case StudioSourceKinds.Folder:
                if (string.IsNullOrWhiteSpace(source.SourcePath))
                {
                    return "cartella mancante.";
                }

                if (source.SourcePath.Length > MaxPathLength)
                {
                    return "percorso della cartella troppo lungo.";
                }

                if (source.TimestampRule is null)
                {
                    return "regola per la data dei frame mancante.";
                }

                string? ruleError = ValidateRule(source.TimestampRule, out _);
                if (ruleError is not null)
                {
                    return ruleError;
                }

                return StudioFileExtensions.TryNormalize(source.Extensions, out _, out string? extError)
                    ? null
                    : extError;

            case StudioSourceKinds.Video:
                if (string.IsNullOrWhiteSpace(source.SourcePath))
                {
                    return "file video mancante.";
                }

                if (source.SourcePath.Length > MaxPathLength)
                {
                    return "percorso del video troppo lungo.";
                }

                double? interval = source.IntervalSeconds;
                if (interval is null
                    || !double.IsFinite(interval.Value)
                    || interval.Value < MinIntervalSeconds
                    || interval.Value > MaxIntervalSeconds)
                {
                    return "intervallo di estrazione non valido (da 0,1 a 3600 secondi).";
                }

                return null;

            default:
                return "tipo di sorgente non riconosciuto.";
        }
    }
}
