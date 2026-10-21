using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllSky.Publish;

/// <summary>
/// Archivio delle impostazioni di pubblicazione in un file JSON (camelCase, indentato).
/// I campi non segreti sono in chiaro; token e valore dell'header extra sono salvati solo come
/// <c>tokenProtected</c> / <c>extraHeaderValueProtected</c> (base64 dell'output di ISecretProtector.Protect su UTF-8).
/// Il file non contiene mai i segreti in chiaro, nemmeno come ripiego.
/// Load non lancia mai: file assente = default; JSON corrotto = file spostato in .bad e default;
/// segreto non decifrabile = segreto vuoto.
/// Save scrive in modo atomico (file .tmp poi spostamento sul file definitivo).
/// </summary>
public sealed class PublishSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private readonly ISecretProtector _protector;

    /// <param name="filePath">Percorso del file JSON delle impostazioni.</param>
    /// <param name="protector">Cifratura dei segreti per il salvataggio su disco.</param>
    public PublishSettingsStore(string filePath, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        _filePath = filePath;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <summary>Percorso del file delle impostazioni.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Legge le impostazioni. Non lancia mai: in caso di problema restituisce i valori di default
    /// (Enabled=false) e, per un file corrotto, lo sposta in .bad.
    /// </summary>
    public PublishSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new PublishSettings();

            string text;
            try
            {
                text = File.ReadAllText(_filePath, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Lettura impossibile (file bloccato, permessi): niente spostamenti, default in memoria.
                return new PublishSettings();
            }

            SettingsFileDto? file;
            try
            {
                file = JsonSerializer.Deserialize<SettingsFileDto>(text, JsonOptions);
            }
            catch (JsonException)
            {
                file = null;
            }

            if (file is null)
            {
                QuarantineCorruptFile();
                return new PublishSettings();
            }

            return ToSettings(file);
        }
        catch (Exception)
        {
            return new PublishSettings();
        }
    }

    /// <summary>Salva le impostazioni in modo atomico, cifrando i segreti.</summary>
    /// <exception cref="InvalidOperationException">La protezione dei segreti non e' disponibile su questo sistema: nulla viene salvato.</exception>
    public void Save(PublishSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);

        var file = new SettingsFileDto
        {
            Enabled = s.Enabled,
            ServerUrl = s.ServerUrl,
            DeviceId = s.DeviceId,
            ProfileName = s.ProfileName,
            IntervalSeconds = s.IntervalSeconds,
            LongEdgePx = s.LongEdgePx,
            JpegQuality = s.JpegQuality,
            ExtraHeaderName = s.ExtraHeaderName,
            TokenProtected = ProtectSecret(s.Token),
            ExtraHeaderValueProtected = ProtectSecret(s.ExtraHeaderValue),
        };

        string json = JsonSerializer.Serialize(file, JsonOptions);
        WriteAtomic(json);
    }

    private string? ProtectSecret(Secret secret)
    {
        if (secret.IsEmpty)
            return null;

        byte[] plain = Encoding.UTF8.GetBytes(secret.Reveal());
        try
        {
            byte[] cipher = _protector.Protect(plain);
            return Convert.ToBase64String(cipher);
        }
        catch (PlatformNotSupportedException ex)
        {
            throw new InvalidOperationException(
                "La protezione dei segreti non e' disponibile su questo sistema: le impostazioni non sono state salvate.", ex);
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    private Secret UnprotectSecret(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return default;

        byte[]? plain = null;
        try
        {
            byte[] cipher = Convert.FromBase64String(stored);
            plain = _protector.Unprotect(cipher);
            return new Secret(Encoding.UTF8.GetString(plain));
        }
        catch (Exception)
        {
            // Segreto non decifrabile (altro utente, dati alterati, protezione non disponibile): vuoto, senza eccezioni.
            return default;
        }
        finally
        {
            if (plain is not null)
                Array.Clear(plain);
        }
    }

    private PublishSettings ToSettings(SettingsFileDto f)
    {
        PublishSettings defaults = new();
        return new PublishSettings
        {
            Enabled = f.Enabled ?? defaults.Enabled,
            ServerUrl = f.ServerUrl ?? defaults.ServerUrl,
            DeviceId = f.DeviceId ?? defaults.DeviceId,
            Token = UnprotectSecret(f.TokenProtected),
            ProfileName = f.ProfileName,
            IntervalSeconds = f.IntervalSeconds ?? defaults.IntervalSeconds,
            LongEdgePx = f.LongEdgePx ?? defaults.LongEdgePx,
            JpegQuality = f.JpegQuality ?? defaults.JpegQuality,
            ExtraHeaderName = f.ExtraHeaderName,
            ExtraHeaderValue = UnprotectSecret(f.ExtraHeaderValueProtected),
        };
    }

    /// <summary>Scrive su .tmp e poi sposta sul file definitivo (sostituzione del file esistente).</summary>
    private void WriteAtomic(string json)
    {
        string full = Path.GetFullPath(_filePath);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tmp = full + ".tmp";
        byte[] bytes = new UTF8Encoding(false).GetBytes(json);
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            File.Move(tmp, full, overwrite: true);
        }
        catch (Exception)
        {
            TryDelete(tmp);
            throw;
        }
    }

    private void QuarantineCorruptFile()
    {
        try
        {
            File.Move(_filePath, _filePath + ".bad", overwrite: true);
        }
        catch (Exception)
        {
            // Best effort: se lo spostamento fallisce il file resta com'e'.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}

/// <summary>Forma del file JSON delle impostazioni. Campi assenti = default di PublishSettings.</summary>
internal sealed class SettingsFileDto
{
    public bool? Enabled { get; set; }
    public string? ServerUrl { get; set; }
    public string? DeviceId { get; set; }
    public string? ProfileName { get; set; }
    public int? IntervalSeconds { get; set; }
    public int? LongEdgePx { get; set; }
    public int? JpegQuality { get; set; }
    public string? ExtraHeaderName { get; set; }
    public string? TokenProtected { get; set; }
    public string? ExtraHeaderValueProtected { get; set; }
}
