using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllSky.Studio;

/// <summary>
/// Caricamento e salvataggio dei progetti <c>.asbproject</c>.
/// <list type="bullet">
/// <item>Il salvataggio è atomico: si scrive un file temporaneo nella stessa cartella e poi si rinomina.</item>
/// <item>Una versione più recente di quella supportata viene rifiutata con un messaggio chiaro.</item>
/// <item>JSON corrotto o campi non validi producono <see cref="StudioException"/>, mai altre eccezioni.</item>
/// <item>I messaggi contengono solo il nome del file, mai il percorso completo.</item>
/// </list>
/// </summary>
public static class StudioProjectStore
{
    /// <summary>Versione del formato scritta e accettata da questa versione dell'app.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Dimensione massima accettata per un file di progetto.</summary>
    public const int MaxFileBytes = 8 * 1024 * 1024;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Legge e valida un progetto.
    /// </summary>
    /// <param name="path">Percorso del file <c>.asbproject</c>.</param>
    /// <exception cref="StudioException">File mancante, illeggibile, JSON corrotto, versione non supportata o campi non validi.</exception>
    public static StudioProject Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string name = Path.GetFileName(path);

        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                throw new StudioException($"file di progetto non trovato: {name}.");
            }

            if (new FileInfo(path).Length > MaxFileBytes)
            {
                throw new StudioException($"il file «{name}» è troppo grande per essere un progetto.");
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StudioException($"impossibile leggere il progetto «{name}».", ex);
        }

        return Parse(bytes, name);
    }

    /// <summary>
    /// Valida e salva un progetto in modo atomico. Se la validazione fallisce, il file esistente non viene toccato.
    /// </summary>
    /// <param name="path">Percorso di destinazione del file <c>.asbproject</c>.</param>
    /// <param name="project">Progetto da salvare.</param>
    /// <exception cref="StudioException">Progetto non valido o scrittura non riuscita.</exception>
    public static void Save(string path, StudioProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(project);
        string name = Path.GetFileName(path);

        StudioProjectValidator.Validate(project, name);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(project, JsonOptions);

        string full = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(full);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new StudioException($"cartella di destinazione non trovata per «{name}».");
        }

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
            throw new StudioException($"impossibile salvare il progetto «{name}».", ex);
        }
        finally
        {
            if (!moved)
            {
                TryDelete(temp);
            }
        }
    }

    /// <summary>Interpreta i byte di un progetto. Usato da <see cref="Load"/> e dai test.</summary>
    internal static StudioProject Parse(byte[] bytes, string name)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        }
        catch (JsonException ex)
        {
            throw new StudioException($"il file «{name}» non è un progetto valido: JSON corrotto o incompleto.", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new StudioException($"il file «{name}» non è un progetto valido.");
            }

            if (!document.RootElement.TryGetProperty("version", out JsonElement versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out int version))
            {
                throw new StudioException($"il progetto «{name}» non ha un numero di versione valido.");
            }

            if (version > CurrentVersion)
            {
                throw new StudioException(
                    $"il progetto «{name}» è stato creato da una versione più recente dell'app (formato {version}): aggiornare l'app per aprirlo.");
            }

            if (version < 1)
            {
                throw new StudioException($"il progetto «{name}» ha un numero di versione non valido.");
            }
        }

        StudioProject? project;
        try
        {
            project = JsonSerializer.Deserialize<StudioProject>(bytes, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            throw new StudioException($"il progetto «{name}» contiene campi non validi.", ex);
        }

        if (project is null)
        {
            throw new StudioException($"il file «{name}» non è un progetto valido.");
        }

        StudioProjectValidator.Validate(project, name);
        return project;
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
        catch (IOException)
        {
            // Il temporaneo resta: non è un errore del progetto.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
