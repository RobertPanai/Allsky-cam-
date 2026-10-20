using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AllSky.Astro;
using AllSky.Core;

namespace AllSky.Archive;

/// <summary>
/// Archivio su disco delle notti osservative.
/// Layout: <c>&lt;root&gt;/nights/&lt;YYYY-MM-DD&gt;/frames/&lt;yyyyMMddTHHmmssZ&gt;.jpg</c>,
/// <c>thumbs/</c> con le miniature, <c>meta.jsonl</c> (una riga JSON per frame),
/// <c>keogram.jpg</c>, <c>startrails.jpg</c>, <c>timelapse.mp4</c>, <c>timelapse.state</c>.
/// Il corpo dei file e' opaco (byte[]): questa classe non codifica immagini.
/// Scritture dei file atomiche (.tmp poi rinomina). Tutti i metodi pubblici sono thread-safe
/// tramite un unico lock: scelta semplice, adeguata a un frame ogni pochi secondi.
/// </summary>
public sealed class NightArchive
{
    private const string NightsFolderName = "nights";
    private const string FramesFolderName = "frames";
    private const string ThumbsFolderName = "thumbs";
    private const string MetaFileName = "meta.jsonl";
    private const string TimelapseStateFileName = "timelapse.state";
    private const string NightIdFormat = "yyyy-MM-dd";
    private const string FrameIdFormat = "yyyyMMdd'T'HHmmss'Z'";

    // \z e non $: in .NET "$" accetta anche un \n finale. [0-9] e non \d: \d accetta cifre Unicode.
    private static readonly Regex NightIdRegex = new(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}\z", RegexOptions.CultureInvariant);
    private static readonly Regex FrameIdRegex = new(@"^[0-9]{8}T[0-9]{6}Z\z", RegexOptions.CultureInvariant);

    private static readonly string[] ValidPhases = ["day", "twilight", "night"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _gate = new();
    private readonly string _root;
    private readonly string _nightsRoot;
    private readonly IClock _clock;
    private readonly TimeZoneInfo _tz;

    // Indice in memoria per notte, caricato in modo pigro da meta.jsonl.
    private readonly Dictionary<string, NightIndex> _index = new(StringComparer.Ordinal);

    /// <summary>
    /// Crea l'archivio sotto <paramref name="rootFolder"/>. Le cartelle vengono create al primo salvataggio.
    /// </summary>
    /// <param name="rootFolder">Cartella radice dell'archivio.</param>
    /// <param name="clock">Orologio, usato per riconoscere la notte in corso (mai cancellata).</param>
    /// <param name="timeZone">Fuso orario locale dell'osservatorio, per l'identificatore di notte.</param>
    public NightArchive(string rootFolder, IClock clock, TimeZoneInfo timeZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(timeZone);

        _root = Path.GetFullPath(rootFolder);
        _nightsRoot = Path.Combine(_root, NightsFolderName);
        _clock = clock;
        _tz = timeZone;
    }

    /// <summary>Vero se <paramref name="s"/> e' un identificatore di notte valido "YYYY-MM-DD" con data reale.</summary>
    public static bool IsValidNightId(string? s)
    {
        return s is not null
            && NightIdRegex.IsMatch(s)
            && DateOnly.TryParseExact(s, NightIdFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    /// <summary>Vero se <paramref name="s"/> e' un identificatore di frame valido "yyyyMMddTHHmmssZ" con data reale.</summary>
    public static bool IsValidFrameId(string? s)
    {
        return s is not null
            && FrameIdRegex.IsMatch(s)
            && DateTime.TryParseExact(s, FrameIdFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    /// <summary>
    /// Salva un frame, la sua miniatura e la riga in meta.jsonl. Il frame e' scritto per primo e la riga
    /// di indice per ultima, cosi' l'indice non punta mai a file incompleti.
    /// Se esiste gia' un frame con lo stesso id (stesso secondo UTC), viene sovrascritto e la voce
    /// in indice sostituita, senza duplicati.
    /// </summary>
    /// <param name="ts">Istante del frame (qualsiasi offset; salvato in UTC).</param>
    /// <param name="phase">"day", "twilight" o "night".</param>
    /// <param name="jpeg">Corpo del frame, non vuoto.</param>
    /// <param name="thumbJpeg">Corpo della miniatura, non vuoto.</param>
    /// <param name="meta">Esposizione, guadagno e mediana ADU.</param>
    public void SaveFrame(DateTimeOffset ts, string phase, byte[] jpeg, byte[] thumbJpeg, FrameMeta meta)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        ArgumentNullException.ThrowIfNull(thumbJpeg);
        ArgumentNullException.ThrowIfNull(meta);
        if (jpeg.Length == 0)
        {
            throw new ArgumentException("Il frame JPEG non puo' essere vuoto.", nameof(jpeg));
        }
        if (thumbJpeg.Length == 0)
        {
            throw new ArgumentException("La miniatura JPEG non puo' essere vuota.", nameof(thumbJpeg));
        }
        if (Array.IndexOf(ValidPhases, phase) < 0)
        {
            throw new ArgumentException("La fase deve essere \"day\", \"twilight\" o \"night\".", nameof(phase));
        }

        DateTimeOffset utc = ts.ToUniversalTime();
        string nightId = ObservingNight.GetNightId(utc, _tz);
        string frameId = utc.ToString(FrameIdFormat, CultureInfo.InvariantCulture);
        var entry = new FrameEntry(frameId, utc, phase, meta.ExposureMs, meta.Gain, meta.MedianAdu);

        lock (_gate)
        {
            string nightDir = Path.Combine(_nightsRoot, nightId);
            string framesDir = Path.Combine(nightDir, FramesFolderName);
            string thumbsDir = Path.Combine(nightDir, ThumbsFolderName);
            Directory.CreateDirectory(framesDir);
            Directory.CreateDirectory(thumbsDir);

            WriteAtomic(Path.Combine(framesDir, frameId + ".jpg"), jpeg);
            WriteAtomic(Path.Combine(thumbsDir, frameId + ".jpg"), thumbJpeg);

            NightIndex idx = LoadIndex(nightId);
            AppendMetaLine(nightDir, idx, entry);

            if (idx.Positions.TryGetValue(frameId, out int pos))
            {
                idx.Frames[pos] = entry;
            }
            else
            {
                idx.Positions[frameId] = idx.Frames.Count;
                idx.Frames.Add(entry);
            }
        }
    }

    /// <summary>
    /// Elenco delle notti presenti, dalla piu' recente alla piu' vecchia. Ignora le cartelle
    /// che non hanno un nome di notte valido e i collegamenti (symlink/junction).
    /// </summary>
    public IReadOnlyList<NightInfo> ListNights()
    {
        lock (_gate)
        {
            var result = new List<NightInfo>();
            if (!Directory.Exists(_nightsRoot))
            {
                return result;
            }

            foreach (DirectoryInfo dir in new DirectoryInfo(_nightsRoot).EnumerateDirectories())
            {
                if (!IsValidNightId(dir.Name) || IsLink(dir))
                {
                    continue;
                }

                string id = dir.Name;
                NightIndex idx = LoadIndex(id);
                result.Add(new NightInfo(
                    Id: id,
                    Frames: idx.Frames.Count,
                    SizeBytes: DirectorySizeBytes(dir.FullName),
                    FirstFrame: idx.Frames.Count == 0 ? null : idx.Frames.Min(f => f.Timestamp),
                    LastFrame: idx.Frames.Count == 0 ? null : idx.Frames.Max(f => f.Timestamp),
                    HasKeogram: File.Exists(Path.Combine(dir.FullName, "keogram.jpg")),
                    HasStartrails: File.Exists(Path.Combine(dir.FullName, "startrails.jpg")),
                    TimelapseState: ReadTimelapseState(id)));
            }

            result.Sort((a, b) => string.CompareOrdinal(b.Id, a.Id));
            return result;
        }
    }

    /// <summary>
    /// Frame di una notte in ordine di salvataggio (cronologico in uso normale).
    /// Una notte inesistente restituisce una lista vuota.
    /// </summary>
    /// <param name="nightId">Identificatore "YYYY-MM-DD".</param>
    /// <param name="offset">Numero di frame da saltare, maggiore o uguale a zero.</param>
    /// <param name="limit">Numero massimo di frame, maggiore di zero.</param>
    public IReadOnlyList<FrameEntry> ListFrames(string nightId, int offset, int limit)
    {
        ValidateNightId(nightId);
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "L'offset non puo' essere negativo.");
        }
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Il limite deve essere maggiore di zero.");
        }

        lock (_gate)
        {
            return LoadIndex(nightId).Frames.Skip(offset).Take(limit).ToList();
        }
    }

    /// <summary>Percorso del frame JPEG. Il file puo' non esistere: il chiamante lo verifica.</summary>
    public string GetFramePath(string nightId, string frameId)
    {
        ValidateNightId(nightId);
        ValidateFrameId(frameId);
        return ResolveUnderNights(_nightsRoot, nightId, FramesFolderName, frameId + ".jpg");
    }

    /// <summary>Percorso della miniatura JPEG. Il file puo' non esistere: il chiamante lo verifica.</summary>
    public string GetThumbPath(string nightId, string frameId)
    {
        ValidateNightId(nightId);
        ValidateFrameId(frameId);
        return ResolveUnderNights(_nightsRoot, nightId, ThumbsFolderName, frameId + ".jpg");
    }

    /// <summary>
    /// Percorso di un prodotto della notte. Il file puo' non esistere. Il timelapse va scritto dal
    /// chiamante, idealmente in un file temporaneo poi rinominato.
    /// </summary>
    public string GetProductPath(string nightId, NightProduct kind)
    {
        ValidateNightId(nightId);
        string name = kind switch
        {
            NightProduct.Keogram => "keogram.jpg",
            NightProduct.Startrails => "startrails.jpg",
            NightProduct.Timelapse => "timelapse.mp4",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Prodotto sconosciuto."),
        };
        return ResolveUnderNights(_nightsRoot, nightId, name);
    }

    /// <summary>
    /// Imposta lo stato di generazione del timelapse. <see cref="TimelapseState.Generating"/> e
    /// <see cref="TimelapseState.Failed"/> sono salvati nel marker; <see cref="TimelapseState.None"/>
    /// rimuove il marker. <see cref="TimelapseState.Ready"/> non e' impostabile: dipende da timelapse.mp4.
    /// </summary>
    public void SetTimelapseState(string nightId, TimelapseState state)
    {
        ValidateNightId(nightId);
        if (state == TimelapseState.Ready)
        {
            throw new ArgumentException("Ready non si imposta: dipende dalla presenza di timelapse.mp4.", nameof(state));
        }

        lock (_gate)
        {
            string marker = ResolveUnderNights(_nightsRoot, nightId, TimelapseStateFileName);
            if (state == TimelapseState.None)
            {
                if (File.Exists(marker))
                {
                    File.Delete(marker);
                }
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            string text = state == TimelapseState.Generating ? "generating" : "failed";
            WriteAtomic(marker, Encoding.UTF8.GetBytes(text));
        }
    }

    /// <summary>
    /// Cancella la cartella di una notte e il suo indice. Restituisce false se la notte non esiste.
    /// Non cancella la notte in corso (<see cref="InvalidOperationException"/>) ne' un collegamento.
    /// </summary>
    public bool DeleteNight(string nightId)
    {
        ValidateNightId(nightId);
        lock (_gate)
        {
            string dir = ResolveUnderNights(_nightsRoot, nightId);
            if (nightId == CurrentNightId())
            {
                throw new InvalidOperationException("Non si puo' cancellare la notte in corso di acquisizione.");
            }

            var di = new DirectoryInfo(dir);
            if (!di.Exists)
            {
                _index.Remove(nightId);
                return false;
            }
            if (IsLink(di))
            {
                throw new InvalidOperationException("La cartella della notte e' un collegamento: non viene seguito.");
            }

            DeleteTree(dir);
            _index.Remove(nightId);
            return true;
        }
    }

    /// <summary>
    /// Applica la retention: conserva le <paramref name="keepNights"/> notti piu' recenti e cancella le altre.
    /// Non cancella mai la notte in corso, ne' nulla fuori da <c>&lt;root&gt;/nights/</c>: considera solo
    /// le sottocartelle con nome di notte valido e le salta se sono collegamenti (symlink/junction).
    /// </summary>
    /// <returns>Identificatori delle notti cancellate.</returns>
    public IReadOnlyList<string> ApplyRetention(int keepNights)
    {
        if (keepNights < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keepNights), keepNights, "keepNights non puo' essere negativo.");
        }

        lock (_gate)
        {
            var deleted = new List<string>();
            if (!Directory.Exists(_nightsRoot))
            {
                return deleted;
            }

            List<string> candidates = new DirectoryInfo(_nightsRoot).EnumerateDirectories()
                .Where(d => IsValidNightId(d.Name) && !IsLink(d))
                .Select(d => d.Name)
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .ToList();

            string current = CurrentNightId();
            for (int i = keepNights; i < candidates.Count; i++)
            {
                string id = candidates[i];
                if (id == current)
                {
                    continue;
                }

                DeleteTree(Path.Combine(_nightsRoot, id));
                _index.Remove(id);
                deleted.Add(id);
            }

            return deleted;
        }
    }

    /// <summary>
    /// Uso del disco: dimensione dell'archivio e spazio libero sul volume della root.
    /// Lo spazio libero e' null se il volume non e' leggibile.
    /// </summary>
    public DiskUsage GetDiskUsage()
    {
        long used;
        lock (_gate)
        {
            used = Directory.Exists(_nightsRoot) ? DirectorySizeBytes(_nightsRoot) : 0;
        }

        long? free;
        try
        {
            free = new DriveInfo(_root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            free = null;
        }

        return new DiskUsage(used, free);
    }

    // ---- Indice e meta.jsonl ----

    /// <summary>Stato in memoria di una notte. Chiamato solo sotto lock.</summary>
    private sealed class NightIndex
    {
        public List<FrameEntry> Frames { get; } = new();

        public Dictionary<string, int> Positions { get; } = new(StringComparer.Ordinal);

        /// <summary>Vero se meta.jsonl non termina con '\n' (riga parziale da una scrittura interrotta).</summary>
        public bool MetaNeedsNewline { get; set; }
    }

    /// <summary>Carica l'indice di una notte da meta.jsonl, una volta sola. Chiamato sotto lock.</summary>
    private NightIndex LoadIndex(string nightId)
    {
        if (_index.TryGetValue(nightId, out NightIndex? cached))
        {
            return cached;
        }

        var idx = new NightIndex();
        string metaPath = Path.Combine(_nightsRoot, nightId, MetaFileName);
        if (File.Exists(metaPath))
        {
            byte[] bytes = File.ReadAllBytes(metaPath);
            idx.MetaNeedsNewline = bytes.Length > 0 && bytes[^1] != (byte)'\n';

            // Le righe non valide (es. l'ultima troncata da un arresto) vengono ignorate.
            foreach (string line in Encoding.UTF8.GetString(bytes).Split('\n'))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                FrameEntry? entry;
                try
                {
                    entry = JsonSerializer.Deserialize<FrameEntry>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (entry is null || !IsValidFrameId(entry.Id))
                {
                    continue;
                }

                if (idx.Positions.TryGetValue(entry.Id, out int pos))
                {
                    idx.Frames[pos] = entry;
                }
                else
                {
                    idx.Positions[entry.Id] = idx.Frames.Count;
                    idx.Frames.Add(entry);
                }
            }
        }

        _index[nightId] = idx;
        return idx;
    }

    /// <summary>Aggiunge una riga a meta.jsonl. Una riga per scrittura: costo costante. Chiamato sotto lock.</summary>
    private static void AppendMetaLine(string nightDir, NightIndex idx, FrameEntry entry)
    {
        byte[] line = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        using var fs = new FileStream(Path.Combine(nightDir, MetaFileName), FileMode.Append, FileAccess.Write, FileShare.Read);
        if (idx.MetaNeedsNewline)
        {
            // Chiude la riga parziale lasciata da una scrittura interrotta, altrimenti la nuova riga si fonderebbe con essa.
            fs.WriteByte((byte)'\n');
        }
        fs.Write(line);
        fs.WriteByte((byte)'\n');
        fs.Flush(flushToDisk: true);
        idx.MetaNeedsNewline = false;
    }

    // ---- Stato del timelapse ----

    private TimelapseState ReadTimelapseState(string nightId)
    {
        string nightDir = Path.Combine(_nightsRoot, nightId);
        string marker = Path.Combine(nightDir, TimelapseStateFileName);
        if (File.Exists(marker))
        {
            string text = File.ReadAllText(marker).Trim();
            if (text == "generating")
            {
                return TimelapseState.Generating;
            }
            if (text == "failed")
            {
                return TimelapseState.Failed;
            }
        }

        return File.Exists(Path.Combine(nightDir, "timelapse.mp4")) ? TimelapseState.Ready : TimelapseState.None;
    }

    // ---- Validazione dei percorsi ----

    private static void ValidateNightId(string nightId)
    {
        if (!IsValidNightId(nightId))
        {
            throw new ArgumentException("Identificatore di notte non valido: atteso YYYY-MM-DD.", nameof(nightId));
        }
    }

    private static void ValidateFrameId(string frameId)
    {
        if (!IsValidFrameId(frameId))
        {
            throw new ArgumentException("Identificatore di frame non valido: atteso yyyyMMddTHHmmssZ.", nameof(frameId));
        }
    }

    /// <summary>
    /// Compone un percorso sotto <c>nights/</c> e verifica che il percorso canonico resti dentro la cartella.
    /// Difesa in profondita': gli id sono gia' validati e non contengono separatori.
    /// </summary>
    private string ResolveUnderNights(params string[] parts)
    {
        string full = Path.GetFullPath(Path.Combine(parts));
        string prefix = Path.TrimEndingDirectorySeparator(_nightsRoot) + Path.DirectorySeparatorChar;
        StringComparison cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(prefix, cmp))
        {
            throw new ArgumentException("Percorso fuori dalla cartella delle notti.");
        }

        return full;
    }

    private string CurrentNightId()
    {
        return ObservingNight.GetNightId(_clock.UtcNow, _tz);
    }

    // ---- Utilita' sul file system ----

    /// <summary>Scrittura atomica: file .tmp accanto alla destinazione, poi rinomina sovrascrivendo.</summary>
    private static void WriteAtomic(string path, byte[] data)
    {
        string tmp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(data);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Il .tmp residuo non blocca l'errore originale, che viene rilanciato.
            }
            throw;
        }
    }

    /// <summary>Vero per symlink e junction. Questi non vengono mai seguiti.</summary>
    private static bool IsLink(FileSystemInfo fsi)
    {
        return (fsi.Attributes & FileAttributes.ReparsePoint) != 0 || fsi.LinkTarget is not null;
    }

    /// <summary>Dimensione in byte dei file sotto una cartella, senza seguire i collegamenti.</summary>
    private static long DirectorySizeBytes(string dir)
    {
        long total = 0;
        foreach (FileSystemInfo fsi in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            if (IsLink(fsi))
            {
                continue;
            }

            total += fsi switch
            {
                FileInfo fi => fi.Length,
                DirectoryInfo sub => DirectorySizeBytes(sub.FullName),
                _ => 0,
            };
        }

        return total;
    }

    /// <summary>
    /// Cancella una cartella ricorsivamente. I collegamenti vengono rimossi come tali, senza seguire
    /// il bersaglio. Per i file dei symlink su Unix si usa File.Delete.
    /// </summary>
    private static void DeleteTree(string path)
    {
        foreach (FileSystemInfo fsi in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            if (IsLink(fsi))
            {
                if (fsi is DirectoryInfo && OperatingSystem.IsWindows())
                {
                    Directory.Delete(fsi.FullName, recursive: false);
                }
                else
                {
                    File.Delete(fsi.FullName);
                }
            }
            else if (fsi is DirectoryInfo sub)
            {
                DeleteTree(sub.FullName);
            }
            else
            {
                File.Delete(fsi.FullName);
            }
        }

        Directory.Delete(path, recursive: false);
    }
}
