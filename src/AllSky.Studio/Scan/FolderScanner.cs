namespace AllSky.Studio;

/// <summary>
/// Scansione di una cartella di JPEG o PNG: ricava l'istante di ogni frame con la regola scelta e
/// produce frame ordinati, file scartati, avvisi di coerenza e un'anteprima.
/// <list type="bullet">
/// <item>Non ricorsiva: si guardano solo i file della cartella indicata.</item>
/// <item>Un file illeggibile o corrotto viene scartato e la scansione prosegue.</item>
/// <item>I collegamenti simbolici che puntano fuori dalla cartella non vengono seguiti (il file è scartato).</item>
/// <item>Gli avvisi e i motivi contengono solo nomi di file, mai percorsi completi.</item>
/// </list>
/// </summary>
public static class FolderScanner
{
    private static readonly TimeSpan LargeMedianGap = TimeSpan.FromHours(1);
    private static readonly TimeSpan DateJump = TimeSpan.FromHours(24);
    private const int PreviewEdge = 5;
    private const int PreviewAll = 2 * PreviewEdge;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Scansiona la cartella con la regola data.</summary>
    /// <param name="folder">Cartella da scansionare.</param>
    /// <param name="rule">Regola per l'istante dei frame.</param>
    /// <param name="options">Fuso orario ed estensioni. Null = UTC e estensioni predefinite.</param>
    /// <exception cref="StudioException">Cartella inesistente o illeggibile, regola o opzioni non valide.</exception>
    public static ScanResult Scan(string folder, TimestampRule rule, ScanOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(rule);
        options ??= new ScanOptions();

        string? ruleError = ValidateRuleOrNull(rule, out FileNameTemplate? template);
        if (ruleError is not null)
        {
            throw new StudioException(ruleError);
        }

        if (!StudioFileExtensions.TryNormalize(options.Extensions, out IReadOnlySet<string> extensions, out string? extError))
        {
            throw new StudioException(extError!);
        }

        TimeZoneInfo? zone = null;
        if (!string.IsNullOrWhiteSpace(options.LocalTimeZoneId) && !LocalTime.TryResolveZone(options.LocalTimeZoneId, out zone))
        {
            throw new StudioException("fuso orario locale non riconosciuto.");
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(root))
        {
            throw new StudioException("cartella non trovata.");
        }

        List<FileInfo> files;
        try
        {
            files = new DirectoryInfo(root).EnumerateFiles()
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StudioException("cartella non leggibile.", ex);
        }

        var frames = new List<StudioFrame>();
        var skipped = new List<SkippedFile>();
        int ambiguous = 0;
        int nonexistent = 0;

        foreach (FileInfo file in files)
        {
            string name = file.Name;
            try
            {
                if (!extensions.Contains(Path.GetExtension(name).ToLowerInvariant()))
                {
                    skipped.Add(new SkippedFile(name, "estensione non ammessa"));
                    continue;
                }

                if (file.LinkTarget is not null && !IsLinkInside(file.FullName, root, out string? linkReason))
                {
                    skipped.Add(new SkippedFile(name, linkReason!));
                    continue;
                }

                ImageKind kind = ReadImageKind(file.FullName);
                if (kind == ImageKind.Unknown)
                {
                    skipped.Add(new SkippedFile(name, "non è un'immagine JPEG o PNG"));
                    continue;
                }

                DateTimeOffset? timestamp = null;
                string? reason = null;
                switch (rule.Mode)
                {
                    case TimestampRuleModes.Exif:
                        if (kind == ImageKind.Png)
                        {
                            reason = "EXIF non disponibile nei PNG";
                        }
                        else if (!ExifDateReader.TryReadFile(file.FullName, out DateTime exifLocal))
                        {
                            reason = "data EXIF non trovata";
                        }
                        else
                        {
                            timestamp = ToUtc(exifLocal, zone, ref ambiguous, ref nonexistent);
                        }

                        break;

                    case TimestampRuleModes.FileNamePattern:
                        if (!template!.TryMatch(name, out DateTime nameLocal, out string? matchError))
                        {
                            reason = matchError;
                        }
                        else
                        {
                            timestamp = ToUtc(nameLocal, zone, ref ambiguous, ref nonexistent);
                        }

                        break;

                    default: // FileModifiedTime
                        timestamp = new DateTimeOffset(file.LastWriteTimeUtc);
                        break;
                }

                if (reason is not null || timestamp is null)
                {
                    skipped.Add(new SkippedFile(name, reason ?? "data non trovata"));
                    continue;
                }

                frames.Add(new StudioFrame(name, file.FullName, timestamp.Value));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new SkippedFile(name, "file non leggibile"));
            }
        }

        List<StudioFrame> ordered = frames
            .OrderBy(f => f.TimestampUtc)
            .ThenBy(f => f.FileName, StringComparer.Ordinal)
            .ToList();

        var warnings = BuildWarnings(ordered, rule, zone, ambiguous, nonexistent);
        return new ScanResult(ordered, skipped, warnings, BuildPreview(ordered));
    }

    private static string? ValidateRuleOrNull(TimestampRule rule, out FileNameTemplate? template)
    {
        return StudioProjectValidator.ValidateRule(rule, out template);
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo? zone, ref int ambiguous, ref int nonexistent)
    {
        DateTimeOffset utc = LocalTime.ToUtc(local, zone, out LocalTimeNote note);
        if (note == LocalTimeNote.Ambiguous)
        {
            ambiguous++;
        }
        else if (note == LocalTimeNote.Nonexistent)
        {
            nonexistent++;
        }

        return utc;
    }

    /// <summary>Un collegamento è accettato solo se la destinazione finale è un file esistente dentro la cartella.</summary>
    private static bool IsLinkInside(string linkPath, string root, out string? reason)
    {
        reason = null;
        FileSystemInfo? target;
        try
        {
            target = File.ResolveLinkTarget(linkPath, returnFinalTarget: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            reason = "collegamento non risolvibile";
            return false;
        }

        if (target is null || !target.Exists)
        {
            reason = "collegamento non risolvibile";
            return false;
        }

        string prefix = root + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!target.FullName.StartsWith(prefix, comparison))
        {
            reason = "collegamento fuori dalla cartella";
            return false;
        }

        return true;
    }

    private enum ImageKind
    {
        Unknown,
        Jpeg,
        Png,
    }

    private static ImageKind ReadImageKind(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var head = new byte[PngSignature.Length];
        int filled = 0;
        while (filled < head.Length)
        {
            int read = stream.Read(head, filled, head.Length - filled);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        if (filled >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return ImageKind.Jpeg;
        }

        if (filled == PngSignature.Length && head.AsSpan().SequenceEqual(PngSignature))
        {
            return ImageKind.Png;
        }

        return ImageKind.Unknown;
    }

    private static List<string> BuildWarnings(
        IReadOnlyList<StudioFrame> frames,
        TimestampRule rule,
        TimeZoneInfo? zone,
        int ambiguous,
        int nonexistent)
    {
        var warnings = new List<string>();

        bool usesLocalTime = rule.Mode is TimestampRuleModes.Exif or TimestampRuleModes.FileNamePattern;
        if (zone is null && usesLocalTime && frames.Count > 0)
        {
            warnings.Add("nessun fuso orario locale impostato: le date EXIF o del nome file sono lette come UTC.");
        }

        if (ambiguous > 0)
        {
            warnings.Add($"{ambiguous} frame hanno un orario ambiguo (fine dell'ora legale): scelta la prima occorrenza, cioè l'ora legale.");
        }

        if (nonexistent > 0)
        {
            warnings.Add($"{nonexistent} frame hanno un orario inesistente (inizio dell'ora legale): interpretati con l'ora solare precedente al salto.");
        }

        List<IGrouping<DateTimeOffset, StudioFrame>> shared = frames
            .GroupBy(f => f.TimestampUtc)
            .Where(g => g.Count() > 1)
            .ToList();
        if (shared.Count > 0)
        {
            IGrouping<DateTimeOffset, StudioFrame> first = shared[0];
            string example = $"«{first.ElementAt(0).FileName}» e «{first.ElementAt(1).FileName}»";
            warnings.Add($"{shared.Count} istanti condivisi da più frame, ad esempio {example}: i frame sono tenuti tutti.");
        }

        if (frames.Count >= 2)
        {
            var gaps = new List<TimeSpan>(frames.Count - 1);
            for (int i = 1; i < frames.Count; i++)
            {
                gaps.Add(frames[i].TimestampUtc - frames[i - 1].TimestampUtc);
            }

            gaps.Sort();
            TimeSpan median = gaps.Count % 2 == 1
                ? gaps[gaps.Count / 2]
                : TimeSpan.FromTicks((gaps[(gaps.Count / 2) - 1].Ticks + gaps[gaps.Count / 2].Ticks) / 2);
            if (median > LargeMedianGap)
            {
                warnings.Add($"l'intervallo mediano tra i frame è di {median.TotalMinutes:F0} minuti (oltre un'ora): controllare che la cartella contenga una sola sessione.");
            }

            int jumps = 0;
            for (int i = 1; i < frames.Count; i++)
            {
                if (frames[i].TimestampUtc - frames[i - 1].TimestampUtc > DateJump)
                {
                    jumps++;
                }
            }

            if (jumps > 0)
            {
                warnings.Add($"{jumps} salti di data oltre 24 ore tra frame consecutivi: controllare la cartella.");
            }
        }

        return warnings;
    }

    private static List<PreviewItem> BuildPreview(IReadOnlyList<StudioFrame> frames)
    {
        IEnumerable<StudioFrame> selected = frames.Count <= PreviewAll
            ? frames
            : frames.Take(PreviewEdge).Concat(frames.Skip(frames.Count - PreviewEdge));
        return selected.Select(f => new PreviewItem(f.FileName, f.TimestampUtc)).ToList();
    }
}
