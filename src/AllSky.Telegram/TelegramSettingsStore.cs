using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AllSky.Publish;

namespace AllSky.Telegram;

/// <summary>
/// Archivio JSON delle impostazioni Telegram. Il token va su disco solo come <c>botTokenProtected</c>
/// (base64 dell'output di <see cref="ISecretProtector.Protect"/>): nessun segreto in chiaro, nemmeno come ripiego.
/// Load non lancia mai: file assente = default; JSON corrotto = file spostato in .bad e default; token non decifrabile = vuoto.
/// Save scrive in modo atomico (file .tmp poi spostamento).
/// </summary>
public sealed class TelegramSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ISecretProtector _protector;

    public TelegramSettingsStore(string filePath, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        _filePath = filePath;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public string FilePath => _filePath;

    public TelegramSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new TelegramSettings();

            string text;
            try
            {
                text = File.ReadAllText(_filePath, Encoding.UTF8);
            }
            catch (Exception)
            {
                return new TelegramSettings();
            }

            TelegramSettingsFile? file;
            try
            {
                file = JsonSerializer.Deserialize<TelegramSettingsFile>(text, JsonOptions);
            }
            catch (JsonException)
            {
                file = null;
            }

            if (file is null)
            {
                QuarantineCorruptFile();
                return new TelegramSettings();
            }

            return ToSettings(file);
        }
        catch (Exception)
        {
            return new TelegramSettings();
        }
    }

    /// <exception cref="InvalidOperationException">La protezione dei segreti non e' disponibile: nulla viene salvato.</exception>
    public void Save(TelegramSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var file = new TelegramSettingsFile
        {
            Enabled = settings.Enabled,
            BotTokenProtected = ProtectToken(settings.BotToken),
            AuthorizedChatIds = settings.AuthorizedChatIds.ToList(),
            DailyLimit = settings.DailyLimit,
            OversizeVideoPolicy = settings.OversizeVideoPolicy,
        };

        WriteAtomic(JsonSerializer.Serialize(file, JsonOptions));
    }

    private string? ProtectToken(Secret token)
    {
        if (token.IsEmpty)
            return null;

        byte[] plain = Encoding.UTF8.GetBytes(token.Reveal());
        try
        {
            return Convert.ToBase64String(_protector.Protect(plain));
        }
        catch (PlatformNotSupportedException ex)
        {
            throw new InvalidOperationException(
                "La protezione dei segreti non e' disponibile su questo sistema: le impostazioni Telegram non sono state salvate.", ex);
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    private Secret UnprotectToken(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return default;

        byte[]? plain = null;
        try
        {
            plain = _protector.Unprotect(Convert.FromBase64String(stored));
            return new Secret(Encoding.UTF8.GetString(plain));
        }
        catch (Exception)
        {
            return default;
        }
        finally
        {
            if (plain is not null)
                Array.Clear(plain);
        }
    }

    private TelegramSettings ToSettings(TelegramSettingsFile f)
    {
        TelegramSettings defaults = new();
        return new TelegramSettings
        {
            Enabled = f.Enabled ?? defaults.Enabled,
            BotToken = UnprotectToken(f.BotTokenProtected),
            AuthorizedChatIds = (f.AuthorizedChatIds ?? new List<long>()).ToArray(),
            DailyLimit = f.DailyLimit is >= 1 ? f.DailyLimit.Value : defaults.DailyLimit,
            OversizeVideoPolicy = f.OversizeVideoPolicy ?? defaults.OversizeVideoPolicy,
        };
    }

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

/// <summary>Forma del file JSON. Campi assenti = default di <see cref="TelegramSettings"/>.</summary>
internal sealed class TelegramSettingsFile
{
    public bool? Enabled { get; set; }
    public string? BotTokenProtected { get; set; }
    public List<long>? AuthorizedChatIds { get; set; }
    public int? DailyLimit { get; set; }
    public OversizeVideoPolicy? OversizeVideoPolicy { get; set; }
}
