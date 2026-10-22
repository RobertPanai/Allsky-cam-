using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllSky.YouTube;

/// <summary>
/// Cifratura dei segreti su disco. Su Windows l'Host la collega al protettore DPAPI della pubblicazione
/// (<c>AllSky.Host.YouTubeSecretProtectorAdapter</c>). Questo progetto non dipende da Windows.
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] protectedData);
}

/// <summary>Credenziali YouTube salvate: client ID in chiaro, client secret e refresh token solo cifrati nel file.</summary>
public sealed record StoredYouTubeAuth(string ClientId, OAuthSecret ClientSecret, OAuthSecret RefreshToken);

/// <summary>
/// Archivio JSON delle credenziali YouTube. Il file non contiene mai segreti in chiaro. Load non lancia: file assente,
/// JSON corrotto o segreto non decifrabile danno null. Save scrive in modo atomico (.tmp, poi spostamento).
/// </summary>
public sealed class YouTubeAuthStore
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

    public YouTubeAuthStore(string filePath, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        _filePath = filePath;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    /// <summary>Percorso del file.</summary>
    public string FilePath => _filePath;

    /// <summary>Legge le credenziali. Null se assenti o non utilizzabili.</summary>
    public StoredYouTubeAuth? Load()
    {
        string text;
        try
        {
            if (!File.Exists(_filePath))
                return null;
            text = File.ReadAllText(_filePath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        AuthFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<AuthFileDto>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.ClientId))
            return null;

        OAuthSecret refreshToken = Unprotect(dto.RefreshTokenProtected);
        if (refreshToken.IsEmpty)
            return null;

        return new StoredYouTubeAuth(dto.ClientId, Unprotect(dto.ClientSecretProtected), refreshToken);
    }

    /// <summary>Salva le credenziali in modo atomico, cifrando i segreti.</summary>
    /// <exception cref="InvalidOperationException">La protezione dei segreti non e' disponibile: nulla viene salvato.</exception>
    public void Save(StoredYouTubeAuth auth)
    {
        ArgumentNullException.ThrowIfNull(auth);
        if (auth.RefreshToken.IsEmpty)
            throw new ArgumentException("Il refresh token e' vuoto.", nameof(auth));

        var dto = new AuthFileDto
        {
            ClientId = auth.ClientId,
            ClientSecretProtected = Protect(auth.ClientSecret),
            RefreshTokenProtected = Protect(auth.RefreshToken),
        };
        WriteAtomic(JsonSerializer.Serialize(dto, JsonOptions));
    }

    /// <summary>Rimuove il file delle credenziali, se esiste.</summary>
    public void Delete()
    {
        TryDelete(_filePath);
        TryDelete(_filePath + ".tmp");
    }

    private string? Protect(OAuthSecret secret)
    {
        if (secret.IsEmpty)
            return null;

        byte[] plain = Encoding.UTF8.GetBytes(secret.Reveal());
        try
        {
            return Convert.ToBase64String(_protector.Protect(plain));
        }
        catch (PlatformNotSupportedException ex)
        {
            throw new InvalidOperationException(
                "La protezione dei segreti non e' disponibile su questo sistema: le credenziali YouTube non sono state salvate.", ex);
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    private OAuthSecret Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return default;

        byte[]? plain = null;
        try
        {
            plain = _protector.Unprotect(Convert.FromBase64String(stored));
            return new OAuthSecret(Encoding.UTF8.GetString(plain));
        }
        catch (Exception)
        {
            // Dati alterati, altro utente o protezione assente: il segreto risulta vuoto, senza eccezioni.
            return default;
        }
        finally
        {
            if (plain is not null)
                Array.Clear(plain);
        }
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File bloccato: il logout resta valido perche' il refresh token e' stato revocato.
        }
    }

    private sealed class AuthFileDto
    {
        public string? ClientId { get; set; }
        public string? ClientSecretProtected { get; set; }
        public string? RefreshTokenProtected { get; set; }
    }
}
