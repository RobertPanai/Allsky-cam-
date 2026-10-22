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

    /// <summary>
    /// Caricamento automatico su YouTube (P7). Spento di default. Il client secret e i token non stanno qui: sono nello
    /// store cifrato <c>youtube-auth.json</c>. Assente nelle configurazioni precedenti: si legge con i valori predefiniti.
    /// </summary>
    public YouTubeConfig YouTube { get; init; } = new();

    /// <summary>
    /// Previsione dei passaggi di satelliti (P4.3). Spenta di default: con <c>enabled</c> falso non parte alcuna
    /// richiesta a CelesTrak. Le configurazioni precedenti non hanno la sezione e leggono i valori predefiniti.
    /// </summary>
    public SatellitesConfig Satellites { get; init; } = new();

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

        /// <summary>Modalità transito (P4.6): intervallo ridotto durante un passaggio previsto. Spenta di default.</summary>
        public TransitModeConfig TransitMode { get; init; } = new();

        /// <summary>
        /// Modalità transito. Il sotto-oggetto e' versionato: le configurazioni precedenti non lo hanno e leggono i valori predefiniti.
        /// Con <see cref="Enabled"/> falso il loop di cattura non cambia comportamento.
        /// </summary>
        public sealed record TransitModeConfig
        {
            /// <summary>Versione dello schema del sotto-oggetto. Le configurazioni precedenti non la hanno: si legge come 1.</summary>
            public const int CurrentVersion = 1;

            public int Version { get; init; } = CurrentVersion;

            /// <summary>Attiva la modalità transito. Predefinito: spenta.</summary>
            public bool Enabled { get; init; } = false;

            /// <summary>Frazione dell'intervallo normale usata durante il passaggio (0,05 - 1). Predefinito 0,5 (metà). Non scende sotto il minimo fisico della camera.</summary>
            public double IntervalFraction { get; init; } = 0.5;

            /// <summary>Margine, in secondi, oltre la fine del passaggio prima di ripristinare l'intervallo normale. Predefinito 5.</summary>
            public double RestoreMarginSeconds { get; init; } = 5;

            /// <summary>Tempo di lettura e scarico dichiarato della camera, in secondi. Il minimo fisico e' esposizione + questo valore. Predefinito 2.</summary>
            public double ReadoutSeconds { get; init; } = 2;
        }
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

    /// <summary>
    /// Sotto-oggetto <c>youtube</c>. Privacy, ora e tag sono testo, non enumerazioni: un valore di una versione futura
    /// non fa fallire la lettura dell'intero file (vedi <c>YouTubeSettingsRules</c> per la validazione).
    /// </summary>
    public sealed record YouTubeConfig
    {
        /// <summary>Versione dello schema del sotto-oggetto.</summary>
        public const int CurrentVersion = 1;

        public int Version { get; init; } = CurrentVersion;

        /// <summary>Caricamento automatico attivo. Lo impostano il collegamento e lo scollegamento dell'account, non il modulo.</summary>
        public bool Enabled { get; init; } = false;

        /// <summary>Client ID delle credenziali "Desktop app" dell'utente. Non segreto. Null se non collegato.</summary>
        public string? ClientId { get; init; } = null;

        /// <summary>Ora locale del caricamento giornaliero, "HH:mm". Predefinito 12:30: la notte osservativa finisce a mezzogiorno.</summary>
        public string DailyTime { get; init; } = "12:30";

        /// <summary>Se il PC era spento all'ora prevista, il caricamento parte entro queste ore dall'avvio. Da 0 a 24.</summary>
        public int CatchUpWindowHours { get; init; } = 12;

        /// <summary>Privacy del video: "private" (predefinito), "unlisted" o "public".</summary>
        public string Privacy { get; init; } = "private";

        public string[] Tags { get; init; } = ["timelapse", "allsky", "astrofotografia"];

        /// <summary>ID numerico della categoria YouTube. Predefinito "28": da verificare sulla documentazione YouTube.</summary>
        public string CategoryId { get; init; } = "28";

        /// <summary>Luogo di ripresa, usato da {luogo} nei modelli di titolo e descrizione.</summary>
        public string Location { get; init; } = "Osservatorio";

        public string TitleTemplate { get; init; } = "Timelapse notte del {notte:yyyy-MM-dd}";

        public string DescriptionTemplate { get; init; } =
            "Timelapse della notte del {notte:yyyy-MM-dd} da {luogo}: {fotogrammi} fotogrammi, durata {durata}, meteore {meteore}, transiti {transiti}.";
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

    /// <summary>
    /// Previsione dei passaggi (P4.3). Versionata: le versioni precedenti non hanno la proprieta' <c>version</c>.
    /// Numeri NORAD e vincoli sono controllati da <see cref="ConfigStore.Validate"/>.
    /// </summary>
    public sealed record SatellitesConfig
    {
        /// <summary>Versione dello schema di questo sotto-oggetto.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Versione dello schema.</summary>
        public int Version { get; init; } = CurrentVersion;

        /// <summary>Interruttore generale. Predefinito: spento.</summary>
        public bool Enabled { get; init; } = false;

        /// <summary>
        /// Bersagli da seguire, per numero NORAD. Predefinito: solo la ISS (NORAD 25544, verificato su CelesTrak).
        /// Il nome serve solo alla visualizzazione.
        /// </summary>
        public SatelliteTargetConfig[] Targets { get; init; } =
        [
            new SatelliteTargetConfig { NoradId = 25544, Name = "ISS (ZARYA)" },
        ];

        /// <summary>Finestra della previsione, in ore a partire da ora. Predefinito 24.</summary>
        public double WindowHours { get; init; } = 24;

        /// <summary>Elevazione minima perche' un passaggio conti, in gradi. Predefinito 10.</summary>
        public double MinElevationDeg { get; init; } = 10;

        /// <summary>Altezza massima del Sole per il cielo "scuro", in gradi. Predefinito -6 (crepuscolo civile).</summary>
        public double SunMaxAltDeg { get; init; } = -6;

        /// <summary>Un bersaglio: numero NORAD e nome per la visualizzazione.</summary>
        public sealed record SatelliteTargetConfig
        {
            /// <summary>Numero di catalogo NORAD, da 1 a 99999 (i TLE non coprono numeri a 6 cifre).</summary>
            public int NoradId { get; init; }

            /// <summary>Nome leggibile. Vuoto: si mostra il numero NORAD.</summary>
            public string Name { get; init; } = string.Empty;
        }
    }
}
