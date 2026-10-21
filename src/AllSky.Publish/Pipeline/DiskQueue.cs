using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AllSky.Core;

namespace AllSky.Publish;

/// <summary>
/// Coda su disco degli elementi da inviare. Per ogni elemento due file nella cartella:
/// <c>&lt;captureId&gt;.jpg</c> (immagine) e <c>&lt;captureId&gt;.json</c> (metadati, tutti i campi di <see cref="QueuedItem"/>
/// tranne <see cref="QueuedItem.ImagePath"/>, che viene ricostruito).
/// <list type="bullet">
/// <item>Scritture atomiche: file temporaneo <c>.tmp</c> poi rinomina. Il jpg viene scritto prima del json.</item>
/// <item>Capacita' limitata: oltre <c>maxItems</c> si eliminano gli elementi piu' vecchi per <see cref="QueuedItem.CapturedAtUtc"/>.</item>
/// <item>Riavvio: al caricamento jpg senza json, json illeggibile, json senza jpg e <c>.tmp</c> residui vengono cancellati
/// (sono orfani). Il limite viene applicato anche al caricamento.</item>
/// <item>Idempotenza: un captureId gia' presente viene sovrascritto.</item>
/// <item>Sicurezza: il captureId deve essere un UUID minuscolo; ogni percorso e' costruito solo da quel nome dentro la cartella,
/// e si toccano solo file <c>&lt;uuid&gt;.jpg</c>, <c>&lt;uuid&gt;.json</c> e i rispettivi <c>.tmp</c>. Gli altri file della cartella non vengono mai modificati.</item>
/// </list>
/// Thread-safe (un lock per istanza). La cartella non va condivisa fra piu' istanze contemporanee.
/// </summary>
public sealed class DiskQueue
{
    private static readonly Regex CaptureIdRegex = new(
        "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\z",
        RegexOptions.CultureInvariant);

    // Nomi dei file della coda: <uuid>.jpg, <uuid>.json, oppure i rispettivi .tmp. Qualsiasi altro nome e' ignorato.
    private static readonly Regex QueueFileRegex = new(
        "^(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\\.(?<ext>jpg|json)(?<tmp>\\.tmp)?\\z",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions MetaOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _folder;
    private readonly IClock _clock;
    private readonly int _maxItems;
    private readonly object _sync = new();
    private readonly Dictionary<string, QueuedItem> _items = new(StringComparer.Ordinal);
    private DateTimeOffset? _lastChangedUtc;

    /// <summary>
    /// Apre la coda nella cartella indicata (la crea se manca) e carica gli elementi presenti, eliminando gli orfani.
    /// </summary>
    /// <param name="folder">Cartella della coda.</param>
    /// <param name="clock">Orologio per <see cref="LastChangedUtc"/>.</param>
    /// <param name="maxItems">Numero massimo di elementi, almeno 1 (default 20).</param>
    public DiskQueue(string folder, IClock clock, int maxItems = 20)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(clock);
        if (maxItems < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems), maxItems, "Il limite della coda deve essere almeno 1.");
        }

        _folder = Path.GetFullPath(folder);
        _clock = clock;
        _maxItems = maxItems;

        Directory.CreateDirectory(_folder);
        LoadFromDisk();
        TrimLocked(_maxItems);
    }

    /// <summary>Numero di elementi in coda.</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>Istante (secondo <see cref="IClock"/>) dell'ultima modifica della coda fatta da questa istanza; null se non ce ne sono state.</summary>
    public DateTimeOffset? LastChangedUtc
    {
        get
        {
            lock (_sync)
            {
                return _lastChangedUtc;
            }
        }
    }

    /// <summary>
    /// Aggiunge o sovrascrive un elemento. Il campo <see cref="QueuedItem.ImagePath"/> dell'argomento viene ignorato.
    /// Se la coda supera il limite, elimina i piu' vecchi per <see cref="QueuedItem.CapturedAtUtc"/>.
    /// </summary>
    /// <param name="item">Metadati dell'elemento.</param>
    /// <param name="jpeg">Immagine JPEG.</param>
    /// <exception cref="ArgumentException">Se il captureId non e' un UUID minuscolo con trattini.</exception>
    /// <exception cref="IOException">Se la scrittura su disco fallisce.</exception>
    public void Enqueue(QueuedItem item, byte[] jpeg)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(jpeg);
        string id = ValidateCaptureId(item.CaptureId);

        lock (_sync)
        {
            string imagePath = ImagePathFor(id);
            string metaPath = MetaPathFor(id);

            WriteAtomic(imagePath, jpeg);

            var meta = new QueueMeta(id, item.CapturedAtUtc, item.ExposureMs, item.Gain, item.Mode, item.TemperatureC);
            WriteAtomic(metaPath, JsonSerializer.SerializeToUtf8Bytes(meta, MetaOptions));

            _items[id] = item with { CaptureId = id, ImagePath = imagePath };
            Touch();
            TrimLocked(_maxItems);
        }
    }

    /// <summary>Elementi in coda, dal piu' vecchio al piu' recente per <see cref="QueuedItem.CapturedAtUtc"/>.</summary>
    public IReadOnlyList<QueuedItem> List()
    {
        lock (_sync)
        {
            return SortOldestFirst(_items.Values).ToList();
        }
    }

    /// <summary>
    /// Legge il JPEG di un elemento in coda, identificato dal captureId.
    /// </summary>
    /// <returns>I byte del JPEG, oppure null se l'elemento non e' in coda o il file manca.</returns>
    /// <exception cref="ArgumentException">Se il captureId non e' valido.</exception>
    public byte[]? ReadImage(QueuedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        string id = ValidateCaptureId(item.CaptureId);

        lock (_sync)
        {
            if (!_items.ContainsKey(id))
            {
                return null;
            }

            try
            {
                return File.ReadAllBytes(ImagePathFor(id));
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }

    /// <summary>Rimuove un elemento (json prima, poi jpg). Restituisce false se non era in coda o se il json non si puo' eliminare.</summary>
    /// <exception cref="ArgumentException">Se il captureId non e' valido.</exception>
    public bool Remove(string captureId)
    {
        string id = ValidateCaptureId(captureId);
        lock (_sync)
        {
            bool removed = RemoveEntry(id);
            if (removed)
            {
                Touch();
            }

            return removed;
        }
    }

    /// <summary>Elimina i piu' vecchi finche' restano al massimo <paramref name="keep"/> elementi.</summary>
    /// <param name="keep">Numero di elementi da conservare, almeno 0.</param>
    /// <returns>Quanti elementi sono stati eliminati.</returns>
    public int TrimToNewest(int keep)
    {
        if (keep < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), keep, "Il numero di elementi da conservare non puo' essere negativo.");
        }

        lock (_sync)
        {
            return TrimLocked(keep);
        }
    }

    /// <summary>Carica dal disco gli elementi validi ed elimina gli orfani. Chiamato solo dal costruttore.</summary>
    private void LoadFromDisk()
    {
        var jpgIds = new HashSet<string>(StringComparer.Ordinal);
        var metas = new Dictionary<string, QueueMeta>(StringComparer.Ordinal);
        var brokenMetaPaths = new List<string>();
        var unreadableIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in Directory.EnumerateFiles(_folder))
        {
            Match match = QueueFileRegex.Match(Path.GetFileName(path));
            if (!match.Success)
            {
                continue; // file estraneo: non lo tocchiamo
            }

            string id = match.Groups["id"].Value;
            if (match.Groups["tmp"].Success)
            {
                TryDelete(path);
                continue;
            }

            if (match.Groups["ext"].Value == "jpg")
            {
                jpgIds.Add(id);
                continue;
            }

            try
            {
                QueueMeta? meta = JsonSerializer.Deserialize<QueueMeta>(File.ReadAllText(path), MetaOptions);
                if (meta is not null && string.Equals(meta.CaptureId, id, StringComparison.Ordinal))
                {
                    metas[id] = meta;
                }
                else
                {
                    brokenMetaPaths.Add(path);
                }
            }
            catch (JsonException)
            {
                brokenMetaPaths.Add(path);
            }
            catch (IOException)
            {
                unreadableIds.Add(id); // non sappiamo se e' orfano: lo lasciamo stare
            }
            catch (UnauthorizedAccessException)
            {
                unreadableIds.Add(id);
            }
        }

        foreach (var (id, meta) in metas)
        {
            if (jpgIds.Contains(id))
            {
                _items[id] = new QueuedItem(id, meta.CapturedAtUtc, meta.ExposureMs, meta.Gain, meta.Mode, meta.TemperatureC, ImagePathFor(id));
            }
            else
            {
                TryDelete(MetaPathFor(id)); // json senza jpg
            }
        }

        foreach (string path in brokenMetaPaths)
        {
            TryDelete(path);
        }

        foreach (string id in jpgIds)
        {
            if (!_items.ContainsKey(id) && !unreadableIds.Contains(id))
            {
                TryDelete(ImagePathFor(id)); // jpg senza json valido
            }
        }
    }

    /// <summary>Elimina i piu' vecchi oltre <paramref name="keep"/>. Chiamato con il lock gia' preso.</summary>
    private int TrimLocked(int keep)
    {
        int excess = _items.Count - keep;
        if (excess <= 0)
        {
            return 0;
        }

        int removed = 0;
        foreach (QueuedItem oldest in SortOldestFirst(_items.Values).Take(excess).ToList())
        {
            if (RemoveEntry(oldest.CaptureId))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            Touch();
        }

        return removed;
    }

    /// <summary>Rimuove un elemento: prima il json (cosi' l'elemento scompare), poi il jpg. Chiamato con il lock gia' preso.</summary>
    private bool RemoveEntry(string id)
    {
        if (!_items.ContainsKey(id))
        {
            return false;
        }

        try
        {
            File.Delete(MetaPathFor(id));
        }
        catch (IOException)
        {
            return false; // resta in coda, si riprovera' piu' tardi
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        _items.Remove(id);
        TryDelete(ImagePathFor(id));
        return true;
    }

    private void Touch()
    {
        _lastChangedUtc = _clock.UtcNow;
    }

    private string ImagePathFor(string id) => Path.Combine(_folder, id + ".jpg");

    private string MetaPathFor(string id) => Path.Combine(_folder, id + ".json");

    private static IEnumerable<QueuedItem> SortOldestFirst(IEnumerable<QueuedItem> items) =>
        items.OrderBy(i => i.CapturedAtUtc).ThenBy(i => i.CaptureId, StringComparer.Ordinal);

    /// <summary>Scrittura atomica: file .tmp, flush su disco, rinomina sopra il file finale. Il .tmp residuo viene sempre rimosso.</summary>
    private static void WriteAtomic(string path, byte[] bytes)
    {
        string tmp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Resta sul disco: sara' eliminato come orfano al prossimo caricamento.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ValidateCaptureId(string? captureId)
    {
        if (captureId is null || !CaptureIdRegex.IsMatch(captureId))
        {
            throw new ArgumentException("Il captureId deve essere un UUID minuscolo con trattini.", nameof(captureId));
        }

        return captureId;
    }

    /// <summary>Metadati su disco: tutti i campi di QueuedItem tranne ImagePath.</summary>
    private sealed record QueueMeta(
        string CaptureId,
        DateTimeOffset CapturedAtUtc,
        double? ExposureMs,
        double? Gain,
        PublishMode Mode,
        double? TemperatureC);
}
