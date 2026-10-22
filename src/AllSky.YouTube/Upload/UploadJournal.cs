using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllSky.YouTube;

public enum NightUploadState
{
    /// <summary>Caricamento avviato e non ancora concluso: va ripreso, mai riavviato da zero.</summary>
    InProgress,

    Completed,

    /// <summary>Errore permanente (es. 400): nessun tentativo automatico per questa notte.</summary>
    Failed,

    /// <summary>Stato ambiguo (sessione scaduta o non decifrabile): non si ricarica senza conferma dell'utente.</summary>
    NeedsReview,
}

/// <summary>
/// Stato di un caricamento notturno. <see cref="SessionUri"/> e' un segreto (permette di inviare byte al video):
/// nel file e' cifrato, e il ToString non lo mostra.
/// </summary>
public sealed record NightUploadRecord(
    string NightId,
    NightUploadState State,
    string FilePath,
    long TotalBytes,
    OAuthSecret SessionUri,
    string? VideoId,
    int Attempts,
    string? LastError,
    DateTimeOffset? NextAttemptUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>Stato complessivo del pianificatore: un record per notte e i contatori del giorno.</summary>
public sealed record UploadJournalState(
    IReadOnlyDictionary<string, NightUploadRecord> Nights,
    string? LastSlotDate,
    string? StartsDate,
    int StartsCount,
    DateTimeOffset? QuotaBlockedUntilUtc)
{
    public static UploadJournalState Empty => new(
        new Dictionary<string, NightUploadRecord>(StringComparer.Ordinal),
        null,
        null,
        0,
        null);
}

/// <summary>
/// Registro su file (JSON, scrittura atomica) dei caricamenti. Ogni modifica viene salvata prima di diventare visibile:
/// il chiamante la applica prima di ogni chiamata di rete, così un crash lascia uno stato ripristinabile.
/// Un file illeggibile o corrotto lancia <see cref="InvalidDataException"/>: il pianificatore non carica nulla finche'
/// il file non viene ripristinato, per non rischiare doppi upload.
/// </summary>
public sealed class UploadJournal
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
    private UploadJournalState _state;

    public UploadJournal(string filePath, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        _filePath = filePath;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _state = Read();
    }

    public string FilePath => _filePath;

    public UploadJournalState State => _state;

    /// <summary>Applica <paramref name="change"/> e salva. Se il salvataggio fallisce lo stato resta invariato e l'eccezione si propaga.</summary>
    public void Update(Func<UploadJournalState, UploadJournalState> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        UploadJournalState next = change(_state);
        Write(next);
        _state = next;
    }

    private UploadJournalState Read()
    {
        if (!File.Exists(_filePath))
            return UploadJournalState.Empty;

        string text = File.ReadAllText(_filePath, Encoding.UTF8);
        JournalDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<JournalDto>(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Il registro dei caricamenti YouTube e' danneggiato: il caricamento automatico resta fermo.", ex);
        }

        if (dto is null)
            throw new InvalidDataException("Il registro dei caricamenti YouTube e' vuoto o non valido.");

        var nights = new Dictionary<string, NightUploadRecord>(StringComparer.Ordinal);
        foreach (NightDto night in dto.Nights ?? new List<NightDto>())
        {
            if (string.IsNullOrEmpty(night.NightId) || !Enum.TryParse(night.State, ignoreCase: false, out NightUploadState state))
                throw new InvalidDataException("Il registro dei caricamenti YouTube contiene una notte non valida.");

            OAuthSecret session = default;
            string? error = night.LastError;
            if (!string.IsNullOrEmpty(night.SessionProtected))
            {
                session = Unprotect(night.SessionProtected);
                if (session.IsEmpty)
                {
                    // Senza la sessione non si puo' riprendere: lo stato resta da verificare.
                    state = NightUploadState.NeedsReview;
                    error = "Sessione di caricamento non decifrabile: verificare su YouTube prima di ricaricare.";
                }
            }

            nights[night.NightId] = new NightUploadRecord(
                night.NightId,
                state,
                night.FilePath ?? string.Empty,
                night.TotalBytes,
                session,
                night.VideoId,
                night.Attempts,
                error,
                night.NextAttemptUtc,
                night.UpdatedUtc);
        }

        return new UploadJournalState(
            nights,
            dto.LastSlotDate,
            dto.StartsDate,
            dto.StartsCount,
            dto.QuotaBlockedUntilUtc);
    }

    private void Write(UploadJournalState state)
    {
        var dto = new JournalDto
        {
            LastSlotDate = state.LastSlotDate,
            StartsDate = state.StartsDate,
            StartsCount = state.StartsCount,
            QuotaBlockedUntilUtc = state.QuotaBlockedUntilUtc,
            Nights = state.Nights.Values.Select(record => new NightDto
            {
                NightId = record.NightId,
                State = record.State.ToString(),
                FilePath = record.FilePath,
                TotalBytes = record.TotalBytes,
                SessionProtected = Protect(record.SessionUri),
                VideoId = record.VideoId,
                Attempts = record.Attempts,
                LastError = record.LastError,
                NextAttemptUtc = record.NextAttemptUtc,
                UpdatedUtc = record.UpdatedUtc,
            }).ToList(),
        };

        byte[] bytes = new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(dto, JsonOptions));
        AtomicFile.WriteAllBytes(_filePath, bytes);
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
            throw new InvalidOperationException("La protezione dei segreti non e' disponibile: il caricamento non puo' essere salvato.", ex);
        }
        finally
        {
            Array.Clear(plain);
        }
    }

    private OAuthSecret Unprotect(string stored)
    {
        byte[]? plain = null;
        try
        {
            plain = _protector.Unprotect(Convert.FromBase64String(stored));
            return new OAuthSecret(Encoding.UTF8.GetString(plain));
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

    private sealed class JournalDto
    {
        public string? LastSlotDate { get; set; }
        public string? StartsDate { get; set; }
        public int StartsCount { get; set; }
        public DateTimeOffset? QuotaBlockedUntilUtc { get; set; }
        public List<NightDto>? Nights { get; set; }
    }

    private sealed class NightDto
    {
        public string NightId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? FilePath { get; set; }
        public long TotalBytes { get; set; }
        public string? SessionProtected { get; set; }
        public string? VideoId { get; set; }
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset? NextAttemptUtc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
