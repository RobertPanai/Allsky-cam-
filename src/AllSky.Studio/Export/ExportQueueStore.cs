using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllSky.Studio;

/// <summary>
/// Lettura e scrittura del file di stato della coda (JSON, versione 1). La scrittura è atomica: si scrive un
/// temporaneo nella stessa cartella e poi si rinomina. Gli errori usano solo il nome del file.
/// </summary>
public static class ExportQueueStore
{
    /// <summary>Versione del formato di coda.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Dimensione massima accettata per il file di coda.</summary>
    public const int MaxFileBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Legge i lavori. Un file mancante dà una lista vuota. Un file illeggibile lancia <see cref="StudioException"/>.</summary>
    public static IReadOnlyList<ExportJob> Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            return [];
        }

        byte[] bytes;
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes)
            {
                throw new StudioException($"il file di coda «{name}» è troppo grande.");
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StudioException($"impossibile leggere la coda «{name}».", ex);
        }

        ExportQueueFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ExportQueueFile>(bytes, Options);
        }
        catch (JsonException ex)
        {
            throw new StudioException($"il file di coda «{name}» non è leggibile.", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new StudioException($"il file di coda «{name}» non è leggibile.", ex);
        }

        if (file is null)
        {
            throw new StudioException($"il file di coda «{name}» è vuoto.");
        }

        if (file.Version > CurrentVersion)
        {
            throw new StudioException(
                $"il file di coda «{name}» è stato creato da una versione più recente dell'app: aggiornare l'app.");
        }

        return file.Jobs ?? [];
    }

    /// <summary>Scrive i lavori in modo atomico.</summary>
    public static void Write(string path, IReadOnlyList<ExportJob> jobs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(jobs);
        string name = Path.GetFileName(path);
        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new StudioException($"cartella della coda non trovata per «{name}».");
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new ExportQueueFile(CurrentVersion, jobs.ToList()), Options);
        string temp = Path.Combine(directory, $".{name}.{Guid.NewGuid():N}.tmp");
        bool moved = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, full, overwrite: true);
            moved = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StudioException($"impossibile salvare la coda «{name}».", ex);
        }
        finally
        {
            if (!moved)
            {
                TryDelete(temp);
            }
        }
    }

    /// <summary>Contenitore JSON del file di coda.</summary>
    /// <param name="Version">Versione del formato.</param>
    /// <param name="Jobs">Lavori, nell'ordine di creazione.</param>
    private sealed record ExportQueueFile(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("jobs")] List<ExportJob>? Jobs);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
