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

        if (config.Color.Corrections is { } corrections)
        {
            ValidateColorCorrections(corrections, errors);
        }

        ValidateSatellites(config.Satellites, errors);
        ValidateDetection(config.Detection, errors);
        ValidateWeather(config.Weather ?? new(), errors);
        ValidateNina(config.Nina ?? new(), errors);
        ValidateMonitor(config.Monitor ?? new(), errors);
        ValidateMcp(config.Mcp ?? new(), errors);

        return errors;
    }

    /// <summary>
    /// Avviso che non blocca il salvataggio: <c>monitor.enabled</c> vero senza alcuna voce attiva. Il monitoraggio
    /// non pubblicherebbe nulla. Non restituisce errori. Il chiamante lo scrive nel log.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidateMonitorWarnings(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        AppConfig.MonitorConfig monitor = config.Monitor ?? new();
        var warnings = new List<ValidationError>();
        if (monitor.Enabled && !(monitor.Voices ?? new()).AnyEnabled())
        {
            warnings.Add(new ValidationError("monitor.voices", "Il monitoraggio è acceso ma nessuna voce è attiva: non verrà pubblicato nulla."));
        }

        return warnings;
    }

    /// <summary>
    /// Vincoli della sezione <c>monitor</c>. Solo la versione è un errore: senza voci attive il caso è un avviso
    /// (vedi <see cref="ValidateMonitorWarnings"/>), come per le altre sezioni con interruttore.
    /// </summary>
    private static void ValidateMonitor(AppConfig.MonitorConfig monitor, List<ValidationError> errors)
    {
        const string root = "monitor";
        if (monitor.Version < 1 || monitor.Version > AppConfig.MonitorConfig.CurrentVersion)
        {
            errors.Add(new ValidationError(root + ".version", "Versione della sezione monitoraggio non supportata."));
        }
    }

    /// <summary>
    /// Vincoli della sezione <c>mcp</c>. Versione e porta sono sempre controllate, come <c>ui.port</c>: la porta
    /// fuori intervallo non viene salvata anche con il server spento. Con <c>enabled</c> falso nessun listener parte.
    /// </summary>
    private static void ValidateMcp(AppConfig.McpConfig mcp, List<ValidationError> errors)
    {
        const string root = "mcp";
        if (mcp.Version < 1 || mcp.Version > AppConfig.McpConfig.CurrentVersion)
        {
            errors.Add(new ValidationError(root + ".version", "Versione della sezione MCP non supportata."));
        }

        if (mcp.Port < 1024 || mcp.Port > 65535)
        {
            errors.Add(new ValidationError(root + ".port", "La porta del server MCP deve essere compresa tra 1024 e 65535."));
        }
    }

    /// <summary>
    /// Avvisi che non bloccano il salvataggio: i valori di dominio della sezione <c>nina</c> fuori dai limiti quando
    /// <c>enabled</c> è falso. Con la sezione attiva gli stessi controlli sono errori in <see cref="Validate"/>.
    /// Il chiamante li scrive nel log; non restituisce errori.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidateNinaWarnings(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        AppConfig.NinaConfig nina = config.Nina ?? new();
        var warnings = new List<ValidationError>();
        if (!nina.Enabled)
        {
            ValidateNinaDomain(nina, warnings);
        }

        return warnings;
    }

    /// <summary>
    /// Vincoli della sezione <c>nina</c>. La versione si controlla sempre, come nelle altre sezioni. I valori di dominio
    /// (host, porta, sorgente, tempi, orologio) sono errori solo con <c>enabled</c> vero: con la sezione spenta sono avvisi
    /// (vedi <see cref="ValidateNinaWarnings"/>), così una configurazione di prova con valori sbagliati non blocca il salvataggio.
    /// </summary>
    private static void ValidateNina(AppConfig.NinaConfig nina, List<ValidationError> errors)
    {
        const string root = "nina";
        if (nina.Version < 1 || nina.Version > AppConfig.NinaConfig.CurrentVersion)
        {
            errors.Add(new ValidationError(root + ".version", "Versione della sezione NINA non supportata."));
        }

        if (nina.Enabled)
        {
            ValidateNinaDomain(nina, errors);
        }
    }

    /// <summary>Regole di dominio di <c>nina</c>. Chiamata con la sezione attiva (errori) o spenta (avvisi).</summary>
    private static void ValidateNinaDomain(AppConfig.NinaConfig nina, List<ValidationError> errors)
    {
        const string root = "nina";
        if (nina.Host is not (AppConfig.NinaConfig.Hosts.Loopback or AppConfig.NinaConfig.Hosts.Localhost))
        {
            errors.Add(new ValidationError(root + ".host", "L'host di NINA deve essere \"127.0.0.1\" o \"localhost\": la API non ha autenticazione."));
        }

        if (nina.Port < 1 || nina.Port > 65535)
        {
            errors.Add(new ValidationError(root + ".port", "La porta di NINA deve essere compresa tra 1 e 65535."));
        }

        if (nina.ConnectTimeoutSeconds < 1 || nina.ConnectTimeoutSeconds > 60)
        {
            errors.Add(new ValidationError(root + ".connectTimeoutSeconds", "Il tempo massimo di connessione deve essere compreso tra 1 e 60 secondi."));
        }

        if (nina.ReconnectMaxBackoffSeconds < 5 || nina.ReconnectMaxBackoffSeconds > 600)
        {
            errors.Add(new ValidationError(root + ".reconnectMaxBackoffSeconds", "Il ritardo massimo di riconnessione deve essere compreso tra 5 e 600 secondi."));
        }

        if (nina.Source is not (AppConfig.NinaConfig.Sources.Api or AppConfig.NinaConfig.Sources.Log))
        {
            errors.Add(new ValidationError(root + ".source", "La sorgente NINA deve essere \"api\" o \"log\"."));
        }

        if (nina.LogFolder is not null && string.IsNullOrWhiteSpace(nina.LogFolder))
        {
            errors.Add(new ValidationError(root + ".logFolder", "La cartella dei log di NINA non può essere vuota: usare null per la predefinita."));
        }

        if (!InRange(nina.ClockOffsetSeconds, -600, 600))
        {
            errors.Add(new ValidationError(root + ".clockOffsetSeconds", "La correzione dell'orologio deve essere compresa tra -600 e 600 secondi."));
        }
    }

    /// <summary>Vincoli della sorgente meteo. Il consenso a Open-Meteo non è un errore: senza di esso la sorgente non parte.</summary>
    private static void ValidateWeather(AppConfig.WeatherConfig weather, List<ValidationError> errors)
    {
        const string root = "weather";
        if (weather.Version < 1 || weather.Version > AppConfig.WeatherConfig.CurrentVersion)
        {
            errors.Add(new ValidationError(root + ".version", "Versione della sezione meteo non supportata."));
        }

        bool sorgenteValida = weather.Source is AppConfig.WeatherConfig.Sources.None
            or AppConfig.WeatherConfig.Sources.Ascom
            or AppConfig.WeatherConfig.Sources.OpenMeteo;
        if (!sorgenteValida)
        {
            errors.Add(new ValidationError(root + ".source", "La sorgente meteo deve essere \"none\", \"ascom\" o \"openmeteo\"."));
        }

        if (weather.Source == AppConfig.WeatherConfig.Sources.Ascom && string.IsNullOrWhiteSpace(weather.AscomProgId))
        {
            errors.Add(new ValidationError(root + ".ascomProgId", "Con la sorgente ASCOM indicare il ProgID del driver ObservingConditions."));
        }

        if (weather.ReadTimeoutSeconds < 1 || weather.ReadTimeoutSeconds > 60)
        {
            errors.Add(new ValidationError(root + ".readTimeoutSeconds", "Il tempo massimo di lettura deve essere compreso tra 1 e 60 secondi."));
        }

        if (weather.MaxAgeMinutes < 1 || weather.MaxAgeMinutes > 1440)
        {
            errors.Add(new ValidationError(root + ".maxAgeMinutes", "L'età massima del dato meteo deve essere compresa tra 1 e 1440 minuti."));
        }
    }

    /// <summary>Vincoli del rilevamento (P5.5). Gli intervalli ricalcano le validazioni dei moduli, che lanciano eccezioni.</summary>
    private static void ValidateDetection(AppConfig.DetectionConfig detection, List<ValidationError> errors)
    {
        const string root = "detection";
        if (detection.Version < 1 || detection.Version > AppConfig.DetectionConfig.CurrentVersion)
        {
            errors.Add(new ValidationError(root + ".version", "Versione della sezione rilevamento non supportata."));
        }

        if (!InRange(detection.AnalysisLongSidePx, 240, 4096))
        {
            errors.Add(new ValidationError(root + ".analysisLongSidePx", "Il lato lungo di analisi deve essere compreso tra 240 e 4096 pixel."));
        }

        if (!InRange(detection.ClipFps, 1, 120))
        {
            errors.Add(new ValidationError(root + ".clipFps", "Il fps delle mini-clip deve essere compreso tra 1 e 120."));
        }

        AppConfig.DetectionConfig.StreakSettings s = detection.Streak ?? new();
        if (!InRange(s.BackgroundFrames, 1, 120))
        {
            errors.Add(new ValidationError(root + ".streak.backgroundFrames", "I frame del fondo devono essere compresi tra 1 e 120."));
        }

        if (!InRange(s.ThresholdSigma, 0, 20))
        {
            errors.Add(new ValidationError(root + ".streak.thresholdSigma", "La soglia in sigma deve essere compresa tra 0 e 20."));
        }

        if (!InRange(s.MinDifferenceLuma, 0.5, 255))
        {
            errors.Add(new ValidationError(root + ".streak.minDifferenceLuma", "La differenza minima deve essere compresa tra 0,5 e 255 livelli."));
        }

        if (!InRange(s.MinAreaPx, 1, 100000))
        {
            errors.Add(new ValidationError(root + ".streak.minAreaPx", "L'area minima deve essere compresa tra 1 e 100000 pixel."));
        }

        if (!InRange(s.MinLengthPx, 1, 10000) || !InRange(s.MaxWidthPx, 0.5, 1000) || !InRange(s.MinElongation, 0, 1000))
        {
            errors.Add(new ValidationError(root + ".streak", "Lunghezza, spessore o elongazione minimi fuori dai limiti."));
        }

        if (!(s.MaxChangedFraction > 0 && s.MaxChangedFraction <= 1))
        {
            errors.Add(new ValidationError(root + ".streak.maxChangedFraction", "La frazione massima di pixel cambiati deve stare in (0, 1]."));
        }

        if (!InRange(s.TrackDistancePx, 0, 10000) || !InRange(s.RepeatShiftPx, 0, 10000))
        {
            errors.Add(new ValidationError(root + ".streak", "Le distanze di confronto con tracce e frame successivi non sono valide."));
        }

        if (!InRange(s.TrackAngleDeg, 0, 90) || !InRange(s.RepeatAngleDeg, 0, 90))
        {
            errors.Add(new ValidationError(root + ".streak", "Gli angoli di confronto devono stare tra 0 e 90 gradi."));
        }

        if (!InRange(s.MaxFrameGapSeconds, 1, 3600))
        {
            errors.Add(new ValidationError(root + ".streak.maxFrameGapSeconds", "L'intervallo massimo tra frame consecutivi deve essere tra 1 e 3600 secondi."));
        }

        if (!(s.BolideSaturatedPeakLuma > 0) || !(s.BolideIntegratedLuma > 0) || !(s.BolideLengthPx > 0))
        {
            errors.Add(new ValidationError(root + ".streak", "Le soglie del bolide devono essere positive."));
        }

        AppConfig.DetectionConfig.TransitSettings t = detection.Transit ?? new();
        if (!InRange(t.CorridorHalfWidthPx, 0, 50) || !InRange(t.FlankWidthPx, 1, 50))
        {
            errors.Add(new ValidationError(root + ".transit", "Corridoio e fasce di fondo devono stare tra 0 e 50 pixel (fascia almeno 1)."));
        }

        if (!InRange(t.SampleSpacingPx, 0.1, 10) || !InRange(t.SigmaMultiplier, 0, 100) || !InRange(t.MinExcessLevels, 0, 255))
        {
            errors.Add(new ValidationError(root + ".transit", "Passo di campionamento o soglie di eccesso fuori dai limiti."));
        }

        if (!InRange(t.ScoreThreshold, 0, 1))
        {
            errors.Add(new ValidationError(root + ".transit.scoreThreshold", "La soglia di punteggio deve essere compresa tra 0 e 1."));
        }

        if (!(t.MinVisibleTrackPx >= 0) || double.IsInfinity(t.MinVisibleTrackPx))
        {
            errors.Add(new ValidationError(root + ".transit.minVisibleTrackPx", "La lunghezza minima del tratto deve essere un numero non negativo."));
        }
    }

    /// <summary>
    /// Vincoli di <c>color.corrections</c> (P14.2). Le costanti di tempo devono essere in (0, 3600] secondi; il valore
    /// NaN non supera i confronti. Il preset deve essere uno dei valori noti.
    /// </summary>
    private static void ValidateColorCorrections(ColorCorrections corrections, List<ValidationError> errors)
    {
        const string root = "color.corrections";
        if (!(corrections.TauNightSeconds > 0 && corrections.TauNightSeconds <= 3600))
        {
            errors.Add(new ValidationError(root + ".tauNightSeconds", "La costante di tempo di notte deve essere maggiore di 0 e al massimo 3600 secondi."));
        }

        if (!(corrections.TauDaySeconds > 0 && corrections.TauDaySeconds <= 3600))
        {
            errors.Add(new ValidationError(root + ".tauDaySeconds", "La costante di tempo di giorno deve essere maggiore di 0 e al massimo 3600 secondi."));
        }

        if (!InRange(corrections.GainDbPerUnit, 0, 1))
        {
            errors.Add(new ValidationError(root + ".gainDbPerUnit", "I decibel per unità di guadagno devono essere compresi tra 0 e 1."));
        }

        if (!ColorCorrectionPresets.KnownNames.Contains(corrections.Preset, StringComparer.Ordinal))
        {
            errors.Add(new ValidationError(root + ".preset", "Preset sconosciuto: usare naturale, neutro, profondo o personalizzato."));
        }
    }

    /// <summary>Vincoli della previsione dei passaggi (P4.3). Con la sezione spenta si validano comunque i valori.</summary>
    private static void ValidateSatellites(AppConfig.SatellitesConfig satellites, List<ValidationError> errors)
    {
        if (!InRange(satellites.WindowHours, 1, 72))
        {
            errors.Add(new ValidationError("satellites.windowHours", "La finestra di previsione deve essere compresa tra 1 e 72 ore."));
        }

        if (!(satellites.MinElevationDeg >= 0 && satellites.MinElevationDeg < 90))
        {
            errors.Add(new ValidationError("satellites.minElevationDeg", "L'elevazione minima deve essere compresa tra 0 e 90 gradi."));
        }

        if (!InRange(satellites.SunMaxAltDeg, -90, 0))
        {
            errors.Add(new ValidationError("satellites.sunMaxAltDeg", "L'altezza massima del Sole deve essere compresa tra -90 e 0 gradi."));
        }

        AppConfig.SatellitesConfig.SatelliteTargetConfig[]? targets = satellites.Targets;
        if (targets is null)
        {
            errors.Add(new ValidationError("satellites.targets", "L'elenco dei satelliti non è leggibile: per nessun satellite usare []."));
            return;
        }

        if (targets.Length > 20)
        {
            errors.Add(new ValidationError("satellites.targets", "Al massimo 20 satelliti da seguire."));
        }

        var seen = new HashSet<int>();
        foreach (AppConfig.SatellitesConfig.SatelliteTargetConfig? target in targets)
        {
            if (target is null || target.NoradId < 1 || target.NoradId > 99999)
            {
                errors.Add(new ValidationError("satellites.targets", "Il numero NORAD deve essere compreso tra 1 e 99999: i TLE non coprono numeri a 6 cifre."));
                continue;
            }

            if (!seen.Add(target.NoradId))
            {
                errors.Add(new ValidationError("satellites.targets", $"Numero NORAD duplicato: {target.NoradId}."));
            }
        }
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
