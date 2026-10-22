using System.Text.Json;

namespace AllSky.Core;

/// <summary>Lingua della UI. Serializzata come stringa camelCase ("it", "en").</summary>
public enum Language
{
    It,
    En
}

/// <summary>Modalità di bilanciamento del bianco ("auto", "manual").</summary>
public enum WhiteBalanceMode
{
    Auto,
    Manual
}

/// <summary>Curva di stretch ("mtf", "asinh").</summary>
public enum Stretch
{
    Mtf,
    Asinh
}

/// <summary>Tema della UI ("auto", "day", "night", "red").</summary>
public enum Theme
{
    Auto,
    Day,
    Night,
    Red
}

/// <summary>
/// Configurazione dell'applicazione. Rispecchia il JSON di docs/API.md (sezione «Configurazione»).
/// Serializzazione: camelCase, enum come stringhe. I valori di default sono quelli dell'esempio.
/// </summary>
public sealed record AppConfig
{
    public int SchemaVersion { get; init; } = 1;

    public Language Language { get; init; } = Language.It;

    public SiteConfig Site { get; init; } = new();

    public CameraConfig Camera { get; init; } = new();

    public ColorConfig Color { get; init; } = new();

    public MaskConfig Mask { get; init; } = new();

    public OverlayConfig Overlay { get; init; } = new();

    public CaptureConfig Capture { get; init; } = new();

    public ProductsConfig Products { get; init; } = new();

    public StorageConfig Storage { get; init; } = new();

    public UiConfig Ui { get; init; } = new();

    /// <summary>
    /// Calibrazione ottica accettata (P3.3). Null se la camera non e' ancora calibrata; le configurazioni precedenti
    /// non hanno la proprieta'. Il sotto-oggetto e' versionato, vedi <see cref="Calibration.OpticalCalibrationConfig"/>.
    /// </summary>
    public Calibration.OpticalCalibrationConfig? Optics { get; init; } = null;

    public sealed record SiteConfig
    {
        public string Name { get; init; } = "Osservatorio";

        public double Latitude { get; init; } = 45.07;

        public double Longitude { get; init; } = 7.69;

        public double ElevationM { get; init; } = 240;

        public string Timezone { get; init; } = "Europe/Rome";
    }

    public sealed record CameraConfig
    {
        public string Id { get; init; } = "sim-1";

        public int Gain { get; init; } = 250;

        public double MaxExposureMs { get; init; } = 30000;

        public double DayExposureMs { get; init; } = 2;

        public int Binning { get; init; } = 1;
    }

    public sealed record ColorConfig
    {
        public WhiteBalanceMode WhiteBalance { get; init; } = WhiteBalanceMode.Auto;

        public double[] ManualGains { get; init; } = [1.0, 1.0, 1.0];

        public double Smoothing { get; init; } = 0.85;

        public double GradientRemoval { get; init; } = 0.5;

        public Stretch Stretch { get; init; } = Stretch.Mtf;

        public double Saturation { get; init; } = 1.1;
    }

    public sealed record MaskConfig
    {
        public bool Enabled { get; init; } = false;

        public string? ImagePath { get; init; } = null;
    }

    /// <summary>
    /// Sovrapposizioni dell'immagine. Le proprieta' <c>enabled</c>, <c>showTime</c>, <c>showSensorTemp</c> e <c>label</c>
    /// sono la scritta testuale del flusso di elaborazione. Dal P2.4 il sotto-oggetto ospita anche la maschera informativa
    /// (<see cref="ApplyToTimelapse"/>, <see cref="Layout"/>): le configurazioni precedenti non le hanno e leggono i valori predefiniti.
    /// </summary>
    public sealed record OverlayConfig
    {
        /// <summary>Versione dello schema del sotto-oggetto. Le configurazioni precedenti non la hanno: si legge come 1.</summary>
        public const int CurrentVersion = 1;

        public int Version { get; init; } = CurrentVersion;

        public bool Enabled { get; init; } = true;

        public bool ShowTime { get; init; } = true;

        public bool ShowSensorTemp { get; init; } = true;

        public string Label { get; init; } = "";

        /// <summary>Applica la maschera informativa ai frame del timelapse, nel momento del montaggio. Predefinito: spento.</summary>
        public bool ApplyToTimelapse { get; init; } = false;

        /// <summary>Riservato al flusso dal vivo. Non ancora collegato: il valore non cambia nulla.</summary>
        public bool ApplyToLive { get; init; } = false;

        /// <summary>
        /// Riservato alle immagini pubblicate (Uplink). Lasciato falso, il contratto delle immagini gia' inviate non cambia.
        /// Non ancora collegato.
        /// </summary>
        public bool ApplyToPublish { get; init; } = false;

        /// <summary>
        /// Layout della maschera (<c>OverlayLayout</c> in AllSky.InfoOverlay), come JSON grezzo: Core non dipende da InfoOverlay.
        /// Null = layout predefinito. Il file viene letto da <c>OverlayLayoutCodec</c>, che tollera i valori errati.
        /// </summary>
        public JsonElement? Layout { get; init; } = null;
    }

    public sealed record CaptureConfig
    {
        public int IntervalSeconds { get; init; } = 30;

        public bool DayCapture { get; init; } = true;
    }

    public sealed record ProductsConfig
    {
        public bool Timelapse { get; init; } = true;

        public bool Keogram { get; init; } = true;

        public bool Startrails { get; init; } = true;

        public int TimelapseFps { get; init; } = 25;
    }

    public sealed record StorageConfig
    {
        public string Folder { get; init; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AllSky");

        public int KeepNights { get; init; } = 30;
    }

    public sealed record UiConfig
    {
        public Theme Theme { get; init; } = Theme.Auto;

        public int Port { get; init; } = 5810;

        public bool AllowLan { get; init; } = false;

        /// <summary>
        /// Interfaccia web (server locale per il browser). Spenta di default: l'app desktop non la usa.
        /// Con false l'Host non apre alcuna porta HTTP.
        /// </summary>
        public bool WebEnabled { get; init; } = false;

        /// <summary>
        /// Hash PBKDF2 della password per l'accesso dalla rete locale (vedi <c>LanPassword</c>). Null: nessuna password.
        /// Mai la password in chiaro.
        /// </summary>
        public string? LanPasswordHash { get; init; } = null;
    }
}
