namespace AllSky.Studio;

/// <summary>Un frame di una sorgente, con l'istante ricavato. Il percorso serve al lavoro, non ai messaggi.</summary>
/// <param name="FileName">Nome del file (senza cartella).</param>
/// <param name="FilePath">Percorso completo del file.</param>
/// <param name="TimestampUtc">Istante del frame, in UTC.</param>
public sealed record StudioFrame(string FileName, string FilePath, DateTimeOffset TimestampUtc);

/// <summary>File scartato dalla scansione, con un motivo breve.</summary>
/// <param name="FileName">Nome del file.</param>
/// <param name="Reason">Motivo in italiano (es. "data EXIF non trovata").</param>
public sealed record SkippedFile(string FileName, string Reason);

/// <summary>Riga dell'anteprima: nome del file e istante risultante in UTC.</summary>
/// <param name="FileName">Nome del file.</param>
/// <param name="TimestampUtc">Istante risultante, in UTC.</param>
public sealed record PreviewItem(string FileName, DateTimeOffset TimestampUtc);

/// <summary>Opzioni della scansione di una cartella.</summary>
public sealed record ScanOptions
{
    /// <summary>
    /// Fuso orario locale (IANA o Windows). Null = UTC, con un avviso se la regola usa date locali.
    /// </summary>
    public string? LocalTimeZoneId { get; init; }

    /// <summary>Estensioni ammesse. Null = .jpg, .jpeg, .png.</summary>
    public IReadOnlyList<string>? Extensions { get; init; }
}

/// <summary>Esito della scansione di una cartella.</summary>
/// <param name="Frames">Frame ordinati per istante UTC, poi per nome file (ordinamento stabile).</param>
/// <param name="Skipped">File scartati, con motivo.</param>
/// <param name="Warnings">Avvisi di coerenza, in italiano, senza percorsi.</param>
/// <param name="Preview">Primi e ultimi cinque frame (tutti, se sono al massimo dieci).</param>
public sealed record ScanResult(
    IReadOnlyList<StudioFrame> Frames,
    IReadOnlyList<SkippedFile> Skipped,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<PreviewItem> Preview);

/// <summary>Estensioni di immagine ammesse per le cartelle.</summary>
public static class StudioFileExtensions
{
    /// <summary>Estensioni predefinite.</summary>
    public static IReadOnlyList<string> Defaults { get; } = [".jpg", ".jpeg", ".png"];

    /// <summary>Normalizza (minuscole, punto iniziale) e controlla un elenco di estensioni.</summary>
    /// <param name="extensions">Elenco dal progetto o dalle opzioni. Null = predefinite.</param>
    /// <param name="set">Insieme normalizzato.</param>
    /// <param name="error">Messaggio in italiano in caso di errore.</param>
    public static bool TryNormalize(IReadOnlyList<string>? extensions, out IReadOnlySet<string> set, out string? error)
    {
        set = new HashSet<string>(StringComparer.Ordinal);
        error = null;
        IReadOnlyList<string> source = extensions ?? Defaults;
        if (source.Count == 0)
        {
            error = "almeno un'estensione è richiesta.";
            return false;
        }

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in source)
        {
            string value = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (!value.StartsWith('.'))
            {
                value = "." + value;
            }

            if (Array.IndexOf(new[] { ".jpg", ".jpeg", ".png" }, value) < 0)
            {
                error = $"estensione non ammessa: {raw}.";
                return false;
            }

            normalized.Add(value);
        }

        set = normalized;
        return true;
    }
}
