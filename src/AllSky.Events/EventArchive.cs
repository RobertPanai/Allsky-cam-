using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AllSky.Archive;

namespace AllSky.Events;

/// <summary>
/// Archivio su disco degli eventi.
/// <para>
/// Layout: <c>&lt;root&gt;/nights/&lt;YYYY-MM-DD&gt;/events/&lt;id&gt;/event.json</c>, piu' i file citati in <see cref="EventFiles"/>.
/// Usa la stessa radice e la stessa cartella <c>nights</c> di <see cref="NightArchive"/>: cancellando una notte
/// (retention) si cancellano anche i suoi eventi.
/// </para>
/// <para>
/// Scritture atomiche (file .tmp poi rinomina). Un lock per istanza, come in <see cref="NightArchive"/>.
/// Ogni id e ogni nome di file viene validato prima di toccare il disco: nessun percorso esce da
/// <c>&lt;root&gt;/nights/&lt;notte&gt;/events/&lt;id&gt;</c>. Le voci con id, notte o nomi di file non validi, con JSON
/// corrotto o con una versione dello schema non supportata vengono ignorate in lettura.
/// </para>
/// </summary>
public sealed class EventArchive
{
    /// <summary>Nome del file di metadati dentro la cartella dell'evento.</summary>
    public const string EventFileName = "event.json";

    private const string NightsFolderName = "nights";
    private const string EventsFolderName = "events";
    private const int MaxTextLength = 500;

    private static readonly Regex SafeFileNameRegex = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly object _gate = new();
    private readonly string _nightsRoot;

    /// <summary>Apre l'archivio sotto <paramref name="rootFolder"/>. Le cartelle si creano alla prima scrittura.</summary>
    /// <param name="rootFolder">Stessa radice usata da <see cref="NightArchive"/>.</param>
    public EventArchive(string rootFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder);
        _nightsRoot = Path.Combine(Path.GetFullPath(rootFolder), NightsFolderName);
    }

    /// <summary>Cartella di un evento. Valida notte e id: lancia <see cref="ArgumentException"/> se non sono validi.</summary>
    public string GetEventFolder(string nightId, string eventId)
    {
        ValidateIds(nightId, eventId);
        string full = Path.GetFullPath(Path.Combine(_nightsRoot, nightId, EventsFolderName, eventId));
        EnsureUnderNights(full);
        return full;
    }

    /// <summary>Percorso di un file dell'evento. Il file puo' non esistere: il chiamante lo verifica.</summary>
    public string GetEventFilePath(string nightId, string eventId, string fileName)
    {
        ValidateFileName(fileName);
        return Path.Combine(GetEventFolder(nightId, eventId), fileName);
    }

    /// <summary>
    /// Crea la cartella di un evento nuovo. Lancia <see cref="InvalidOperationException"/> se esiste gia'
    /// (un evento non viene mai sovrascritto, cosi' non si perdono le etichette).
    /// </summary>
    public string CreateEventFolder(string nightId, string eventId)
    {
        string folder = GetEventFolder(nightId, eventId);
        lock (_gate)
        {
            if (Directory.Exists(folder))
            {
                throw new InvalidOperationException("Evento gia' presente: " + eventId);
            }

            Directory.CreateDirectory(folder);
        }
        return folder;
    }

    /// <summary>Scrive un file dentro la cartella di un evento esistente, in modo atomico.</summary>
    public void WriteFile(string nightId, string eventId, string fileName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        string path = GetEventFilePath(nightId, eventId, fileName);
        lock (_gate)
        {
            RequireFolder(nightId, eventId);
            WriteAtomic(path, data);
        }
    }

    /// <summary>
    /// Scrive <c>event.json</c> di un evento esistente, in modo atomico. Il record deve avere
    /// <see cref="EventRecord.CurrentSchemaVersion"/>, id e notte validi e nomi di file sicuri.
    /// </summary>
    public void WriteRecord(EventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            WriteRecordLocked(record);
        }
    }

    /// <summary>Evento per notte e id, o null se non esiste o non e' leggibile.</summary>
    public EventRecord? Get(string nightId, string eventId)
    {
        lock (_gate)
        {
            return ReadRecordLocked(nightId, eventId);
        }
    }

    /// <summary>
    /// Elenco degli eventi filtrati, dal piu' recente al piu' vecchio. Di default esclude gli eventi scartati
    /// (vedi <see cref="EventQuery.IncludeDiscarded"/>).
    /// </summary>
    public IReadOnlyList<EventRecord> List(EventQuery? query = null)
    {
        query ??= new EventQuery();
        if (query.NightId is not null)
        {
            ValidateNightId(query.NightId);
        }

        var result = new List<EventRecord>();
        lock (_gate)
        {
            IReadOnlyList<string> nights = query.NightId is null
                ? NightIdsLocked()
                : new[] { query.NightId };

            foreach (string nightId in nights)
            {
                string eventsDir = Path.Combine(_nightsRoot, nightId, EventsFolderName);
                if (!Directory.Exists(eventsDir) || IsLink(new DirectoryInfo(eventsDir)))
                {
                    continue;
                }

                foreach (DirectoryInfo dir in new DirectoryInfo(eventsDir).EnumerateDirectories())
                {
                    if (IsLink(dir) || !EventIds.IsValid(dir.Name))
                    {
                        continue;
                    }

                    EventRecord? record = ReadRecordLocked(nightId, dir.Name);
                    if (record is not null && Matches(record, query))
                    {
                        result.Add(record);
                    }
                }
            }
        }

        result.Sort((a, b) =>
        {
            int byTime = b.TimestampUtc.CompareTo(a.TimestampUtc);
            return byTime != 0 ? byTime : string.CompareOrdinal(b.Id, a.Id);
        });
        return result;
    }

    /// <summary>Imposta l'etichetta dell'utente. Con <see cref="UserLabel.None"/> il motivo viene cancellato.</summary>
    /// <exception cref="KeyNotFoundException">Evento inesistente.</exception>
    public EventRecord SetLabel(string nightId, string eventId, UserLabel label, string? reason = null)
    {
        if (!Enum.IsDefined(label))
        {
            throw new ArgumentOutOfRangeException(nameof(label), label, "Etichetta sconosciuta.");
        }

        string? cleanReason = CleanText(reason, nameof(reason));
        lock (_gate)
        {
            EventRecord current = ReadRecordLocked(nightId, eventId)
                ?? throw new KeyNotFoundException("Evento non trovato: " + eventId);
            EventRecord updated = current with
            {
                Label = label,
                LabelReason = label == UserLabel.None ? null : cleanReason,
            };
            WriteRecordLocked(updated);
            return updated;
        }
    }

    /// <summary>
    /// Cambia lo stato dell'evento (per esempio <see cref="EventStatus.Discarded"/>) senza toccare i suoi file.
    /// Con <see cref="EventStatus.Active"/> il motivo viene cancellato.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Evento inesistente.</exception>
    public EventRecord SetStatus(string nightId, string eventId, EventStatus status, string? reason = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Stato sconosciuto.");
        }

        string? cleanReason = CleanText(reason, nameof(reason));
        lock (_gate)
        {
            EventRecord current = ReadRecordLocked(nightId, eventId)
                ?? throw new KeyNotFoundException("Evento non trovato: " + eventId);
            EventRecord updated = current with
            {
                Status = status,
                StatusReason = status == EventStatus.Active ? null : cleanReason,
            };
            WriteRecordLocked(updated);
            return updated;
        }
    }

    /// <summary>
    /// Esporta l'evento come zip: una cartella con il nome dell'id, dentro event.json e i file prodotti.
    /// La scrittura passa da un temporaneo. Il zip non puo' stare dentro la cartella dell'evento.
    /// </summary>
    /// <returns>Percorso completo del file zip.</returns>
    public string ExportZip(string nightId, string eventId, string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        string folder = GetEventFolder(nightId, eventId);
        string full = Path.GetFullPath(zipPath);
        if (full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Il file zip non puo' trovarsi dentro la cartella dell'evento.", nameof(zipPath));
        }

        lock (_gate)
        {
            EventRecord record = ReadRecordLocked(nightId, eventId)
                ?? throw new KeyNotFoundException("Evento non trovato: " + eventId);

            string? directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = full + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    AddEntry(zip, folder, eventId, EventFileName);
                    foreach (string? name in new[] { record.Files.Crop, record.Files.Frame, record.Files.Clip })
                    {
                        if (name is not null && File.Exists(Path.Combine(folder, name)))
                        {
                            AddEntry(zip, folder, eventId, name);
                        }
                    }
                }

                File.Move(temp, full, overwrite: true);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }

            return full;
        }
    }

    // ---- Lettura e scrittura del record (chiamati sotto lock) ----

    private EventRecord? ReadRecordLocked(string nightId, string eventId)
    {
        string path = Path.Combine(GetEventFolder(nightId, eventId), EventFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        EventRecord? record;
        try
        {
            record = JsonSerializer.Deserialize<EventRecord>(File.ReadAllBytes(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (record is null
            || record.SchemaVersion < 1
            || record.SchemaVersion > EventRecord.CurrentSchemaVersion
            || record.Id != eventId
            || record.NightId != nightId
            || !FileNamesAreSafe(record))
        {
            return null;
        }

        return record;
    }

    private void WriteRecordLocked(EventRecord record)
    {
        ValidateRecord(record);
        RequireFolder(record.NightId, record.Id);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        WriteAtomic(Path.Combine(GetEventFolder(record.NightId, record.Id), EventFileName), json);
    }

    private void RequireFolder(string nightId, string eventId)
    {
        if (!Directory.Exists(GetEventFolder(nightId, eventId)))
        {
            throw new InvalidOperationException("Cartella dell'evento assente: " + eventId);
        }
    }

    private IReadOnlyList<string> NightIdsLocked()
    {
        var ids = new List<string>();
        if (!Directory.Exists(_nightsRoot))
        {
            return ids;
        }

        foreach (DirectoryInfo dir in new DirectoryInfo(_nightsRoot).EnumerateDirectories())
        {
            if (NightArchive.IsValidNightId(dir.Name) && !IsLink(dir))
            {
                ids.Add(dir.Name);
            }
        }
        return ids;
    }

    // ---- Validazione ----

    private static void ValidateRecord(EventRecord record)
    {
        if (record.SchemaVersion != EventRecord.CurrentSchemaVersion)
        {
            throw new ArgumentException("Versione dello schema non supportata: " + record.SchemaVersion, nameof(record));
        }

        ValidateIds(record.NightId, record.Id);
        if (!FileNamesAreSafe(record))
        {
            throw new ArgumentException("Nome di file non valido nell'evento.", nameof(record));
        }
        if (record.LabelReason is { Length: > MaxTextLength } || record.StatusReason is { Length: > MaxTextLength })
        {
            throw new ArgumentException("Motivo troppo lungo.", nameof(record));
        }
    }

    private static bool FileNamesAreSafe(EventRecord record)
    {
        foreach (string? name in new[] { record.Files.Crop, record.Files.Frame, record.Files.Clip })
        {
            if (name is not null && !SafeFileNameRegex.IsMatch(name))
            {
                return false;
            }
        }
        return true;
    }

    private static void ValidateIds(string nightId, string eventId)
    {
        ValidateNightId(nightId);
        if (!EventIds.IsValid(eventId))
        {
            throw new ArgumentException("Identificatore di evento non valido.", nameof(eventId));
        }
    }

    private static void ValidateNightId(string nightId)
    {
        if (!NightArchive.IsValidNightId(nightId))
        {
            throw new ArgumentException("Identificatore di notte non valido: atteso YYYY-MM-DD.", nameof(nightId));
        }
    }

    private static void ValidateFileName(string fileName)
    {
        if (fileName is null || !SafeFileNameRegex.IsMatch(fileName))
        {
            throw new ArgumentException("Nome di file non valido.", nameof(fileName));
        }
    }

    private void EnsureUnderNights(string full)
    {
        string prefix = Path.TrimEndingDirectorySeparator(_nightsRoot) + Path.DirectorySeparatorChar;
        StringComparison cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(prefix, cmp))
        {
            throw new ArgumentException("Percorso fuori dalla cartella degli eventi.");
        }
    }

    private static bool Matches(EventRecord record, EventQuery query)
    {
        return (query.NightId is null || record.NightId == query.NightId)
            && (query.Kind is null || record.Kind == query.Kind)
            && (query.Label is null || record.Label == query.Label)
            && (query.IncludeDiscarded || record.Status == EventStatus.Active);
    }

    private static string? CleanText(string? text, string paramName)
    {
        string? trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (trimmed is { Length: > MaxTextLength })
        {
            throw new ArgumentException("Testo troppo lungo.", paramName);
        }
        return trimmed;
    }

    // ---- Utilita' sul file system ----

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static void AddEntry(ZipArchive zip, string folder, string eventId, string fileName)
    {
        zip.CreateEntryFromFile(Path.Combine(folder, fileName), eventId + "/" + fileName, CompressionLevel.Optimal);
    }

    /// <summary>Scrittura atomica: file .tmp accanto alla destinazione, poi rinomina sovrascrivendo.</summary>
    private static void WriteAtomic(string path, byte[] data)
    {
        string temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un temporaneo residuo non nasconde l'errore originale.
        }
    }

    /// <summary>Vero per symlink e junction. Non vengono mai seguiti.</summary>
    private static bool IsLink(FileSystemInfo fsi)
    {
        return (fsi.Attributes & FileAttributes.ReparsePoint) != 0 || fsi.LinkTarget is not null;
    }
}
