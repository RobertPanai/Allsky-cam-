namespace AllSky.Publish;

// Contratti condivisi del modulo di pubblicazione. File di progetto (scritto dall'orchestratore):
// gli agenti implementano contro questi tipi e NON li modificano.

/// <summary>Versione del modulo di pubblicazione.</summary>
public static class PublishInfo
{
    public const string Version = "0.1.0";
    public const int SchemaVersion = 1;
}

/// <summary>Valore segreto: ToString non rivela mai il contenuto (quindi nemmeno i record che lo contengono).</summary>
public readonly struct Secret : IEquatable<Secret>
{
    private readonly string? _value;
    public Secret(string? value) { _value = string.IsNullOrEmpty(value) ? null : value; }
    public bool IsEmpty => _value is null;
    /// <summary>Contenuto in chiaro: usarlo solo per l'header di invio e per cifrare su disco.</summary>
    public string Reveal() => _value ?? string.Empty;
    public override string ToString() => IsEmpty ? "(vuoto)" : "***";
    public bool Equals(Secret other) => string.Equals(_value, other._value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is Secret s && Equals(s);
    public override int GetHashCode() => _value is null ? 0 : _value.GetHashCode();
}

/// <summary>Impostazioni di pubblicazione. Il record non stampa mai i segreti.</summary>
public sealed record PublishSettings
{
    public bool Enabled { get; init; }
    /// <summary>URL base del server: https, oppure http solo per localhost/127.0.0.1/::1.</summary>
    public string ServerUrl { get; init; } = "";
    /// <summary>1-64 caratteri [A-Za-z0-9._-].</summary>
    public string DeviceId { get; init; } = "";
    public Secret Token { get; init; }
    /// <summary>Nome mostrato dal server (1-80 caratteri), facoltativo.</summary>
    public string? ProfileName { get; init; }
    /// <summary>Secondi tra due invii: default 60, minimo 15.</summary>
    public int IntervalSeconds { get; init; } = 60;
    /// <summary>Lato lungo massimo dell'immagine inviata, in pixel (200-2000, default 1200).</summary>
    public int LongEdgePx { get; init; } = 1200;
    /// <summary>Qualita' JPEG iniziale (30-95, default 75).</summary>
    public int JpegQuality { get; init; } = 75;
    /// <summary>Header HTTP aggiuntivo facoltativo (nome).</summary>
    public string? ExtraHeaderName { get; init; }
    /// <summary>Valore dell'header aggiuntivo: trattato come segreto.</summary>
    public Secret ExtraHeaderValue { get; init; }
}

/// <summary>Errore di validazione di un campo (path in camelCase, es. "serverUrl").</summary>
public sealed record SettingsError(string Path, string Message);

/// <summary>Cifra/decifra segreti per il salvataggio su disco. Su Windows: DPAPI utente corrente.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] protectedData);
}

/// <summary>Modalita' del cielo, serializzata come day | twilight | night.</summary>
public enum PublishMode { Day, Twilight, Night }

/// <summary>Frame offerto dal motore: RGB 8 bit pulito (senza scritte), righe contigue, 3 byte per pixel.</summary>
public sealed record PublishFrame(
    byte[] Rgb8,
    int Width,
    int Height,
    DateTimeOffset CapturedAtUtc,
    double? ExposureMs,
    double? Gain,
    PublishMode Mode,
    double? TemperatureC);

/// <summary>Stato del publisher.</summary>
public enum PublishState
{
    /// <summary>Pubblicazione spenta o non configurata.</summary>
    Disabled,
    /// <summary>Attivo, in attesa del prossimo intervallo.</summary>
    Idle,
    /// <summary>Invio in corso.</summary>
    Sending,
    /// <summary>Errore temporaneo: nuovo tentativo programmato (NextAttemptUtc).</summary>
    Backoff,
    /// <summary>Invio sospeso (token/dispositivo rifiutato): riprende quando l'utente salva impostazioni nuove.</summary>
    Suspended
}

/// <summary>Stato visibile all'utente. Nessun segreto, nessun URL con credenziali.</summary>
public sealed record PublishStatus(
    PublishState State,
    DateTimeOffset? LastSuccessUtc,
    string? LastErrorCode,
    string? LastError,
    DateTimeOffset? LastErrorUtc,
    int QueueCount,
    int SentTotal,
    int DiscardedTotal,
    DateTimeOffset? NextAttemptUtc);

/// <summary>Esito di "Test connessione".</summary>
public sealed record TestResult(bool Ok, string Code, string Message, int? StatusCode);

/// <summary>Cosa fare dopo un tentativo di invio (classificazione dell'esito HTTP).</summary>
public enum SendOutcome
{
    /// <summary>200 con corpo valido: stored o duplicate, entrambi vanno bene.</summary>
    Acknowledged,
    /// <summary>Rete, timeout, 408, 5xx, risposta illeggibile: riprovare con backoff.</summary>
    Transient,
    /// <summary>429: riprovare dopo Retry-After.</summary>
    RateLimited,
    /// <summary>401/403: sospendere finche' l'utente non salva credenziali nuove.</summary>
    AuthError,
    /// <summary>400, 413, 415 e altri 4xx: scartare il frame, mai riprovare.</summary>
    Permanent
}

/// <summary>Esito di un invio. Reason e' una frase gia' sanificata (mai segreti).</summary>
public sealed record SendResult(SendOutcome Outcome, TimeSpan? RetryAfter, string Code, string Reason, int? StatusCode);

/// <summary>Elemento della coda: un'immagine pronta con i suoi metadati.</summary>
public sealed record QueuedItem(
    string CaptureId,
    DateTimeOffset CapturedAtUtc,
    double? ExposureMs,
    double? Gain,
    PublishMode Mode,
    double? TemperatureC,
    string ImagePath);

/// <summary>Invia un elemento al server. Non lancia: ogni errore e' un SendResult.</summary>
public interface IUplinkSender
{
    Task<SendResult> SendAsync(QueuedItem item, byte[] jpeg, PublishSettings settings, CancellationToken ct);
    Task<TestResult> TestConnectionAsync(PublishSettings settings, CancellationToken ct);
}

/// <summary>Attesa astratta per poter testare backoff e intervalli con un orologio finto.</summary>
public interface IPublishDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

/// <summary>Sorgente di casualita' 0..1 (jitter del backoff), iniettabile nei test.</summary>
public interface IJitter
{
    double NextDouble();
}

/// <summary>Il publisher usato dal motore. Offer non blocca e non lancia mai.</summary>
public interface IPublisher : IAsyncDisposable
{
    /// <summary>Offre l'ultimo frame: il publisher tiene solo il piu' recente e decide quando e se inviarlo.</summary>
    void Offer(PublishFrame frame);
    PublishStatus GetStatus();
    event Action<PublishStatus>? StatusChanged;
    /// <summary>Applica nuove impostazioni (riprende da Suspended se le credenziali cambiano).</summary>
    void ApplySettings(PublishSettings settings);
    Task<TestResult> TestConnectionAsync(PublishSettings? overrideSettings, CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
}
