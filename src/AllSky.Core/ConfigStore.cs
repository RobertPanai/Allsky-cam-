using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllSky.Core;

/// <summary>Caricamento, salvataggio atomico e validazione di <see cref="AppConfig"/>.</summary>
public sealed class ConfigStore
{
    private const string BadSuffix = ".bad";
    private const string TempSuffix = ".tmp";

    /// <summary>Opzioni JSON usate per leggere e scrivere la configurazione.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    /// <summary>
    /// Restituisce la configurazione dal file. Se il file manca restituisce i default (senza crearlo).
    /// Se il JSON non è valido, rinomina il file in "&lt;path&gt;.bad" e restituisce i default.
    /// </summary>
    public AppConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        string text = File.ReadAllText(path);

        AppConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(text, SerializerOptions);
        }
        catch (JsonException)
        {
            config = null;
        }

        if (config is not null)
        {
            return config;
        }

        File.Move(path, path + BadSuffix, overwrite: true);
        return new AppConfig();
    }

    /// <summary>
    /// Salva la configurazione in modo atomico: scrive su un file temporaneo e poi lo sposta sul percorso finale.
    /// </summary>
    public void Save(string path, AppConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(config);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = fullPath + TempSuffix;
        try
        {
            string json = JsonSerializer.Serialize(config, SerializerOptions);
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    /// <summary>Controlla i vincoli della configurazione e restituisce l'elenco degli errori (vuoto se valida).</summary>
    public IReadOnlyList<ValidationError> Validate(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<ValidationError>();

        // InRange con confronti positivi: un NaN non supera mai il controllo.
        if (!InRange(config.Site.Latitude, -90, 90))
        {
            errors.Add(new ValidationError("site.latitude", "La latitudine deve essere compresa tra -90 e 90."));
        }

        if (!InRange(config.Site.Longitude, -180, 180))
        {
            errors.Add(new ValidationError("site.longitude", "La longitudine deve essere compresa tra -180 e 180."));
        }

        if (config.Capture.IntervalSeconds < 1)
        {
            errors.Add(new ValidationError("capture.intervalSeconds", "L'intervallo di cattura deve essere di almeno 1 secondo."));
        }

        if (config.Storage.KeepNights < 1)
        {
            errors.Add(new ValidationError("storage.keepNights", "Il numero di notti da conservare deve essere di almeno 1."));
        }

        if (config.Ui.Port < 1024 || config.Ui.Port > 65535)
        {
            errors.Add(new ValidationError("ui.port", "La porta deve essere compresa tra 1024 e 65535."));
        }

        if (!InRange(config.Color.Smoothing, 0, 1))
        {
            errors.Add(new ValidationError("color.smoothing", "Lo smoothing deve essere compreso tra 0 e 1."));
        }

        return errors;
    }

    private static bool InRange(double value, double min, double max)
    {
        return value >= min && value <= max;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
