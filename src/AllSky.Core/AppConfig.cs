using System.Text.Json;

namespace AllSky.Core;

/// <summary>Lingua della UI. Serializzata come stringa camelCase ("it", "en").</summary>
public enum Language
{
    It,
    En
}

/// <summary>Modalità di esposizione della camera ("auto", "manual").</summary>
public enum CameraExposureMode
{
    Auto,
    Manual
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
    /// Meteo per il sidecar di ogni frame. Spento di default (<c>source</c> "none"): nessuna lettura, nessuna rete.
    /// Le configurazioni precedenti non hanno la sezione e leggono i valori predefiniti.
    /// </summary>
    public WeatherConfig Weather { get; init; } = new();

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

    /// <summary>
    /// Rilevamento di meteore, bolidi e transiti nell'Engine (P5.5). Spento di default: con <c>enabled</c> falso
    /// il comportamento dell'Engine non cambia. Le configurazioni precedenti non hanno la sezione.
    /// </summary>
    public DetectionConfig Detection { get; init; } = new();

    /// <summary>
    /// Collegamento a NINA (sezione <c>nina</c>). Spento di default: con <c>enabled</c> falso nessuna connessione e nessuna lettura.
    /// Le configurazioni precedenti non hanno la sezione e leggono i valori predefiniti.
    /// </summary>
    public NinaConfig Nina { get; init; } = new();

    /// <summary>
    /// Pubblicazione del monitoraggio (sezione <c>monitor</c>, P12.1). Spenta di default: con <c>enabled</c> falso
    /// non viene costruito alcun payload. Ogni dato ha la sua voce, tutte spente di default. La posizione
    /// dell'osservatorio non può essere pubblicata: non esiste né una voce né un'opzione per essa.
    /// Le configurazioni precedenti non hanno la sezione e leggono i valori predefiniti.
    /// </summary>
    public MonitorConfig Monitor { get; init; } = new();

    /// <summary>
    /// Server MCP locale di sola lettura (sezione <c>mcp</c>, P17.4). Spento di default: con <c>enabled</c> falso non
    /// viene aperto alcun listener. Ascolta solo su 127.0.0.1. Le configurazioni precedenti non hanno la sezione e
    /// leggono i valori predefiniti (spento).
    /// </summary>
    public McpConfig Mcp { get; init; } = new();

    /// <summary>Sezione <c>mcp</c>: interruttore e porta del server MCP locale. Vincoli in <see cref="ConfigStore"/>.</summary>
    public sealed record McpConfig
    {
        /// <summary>Versione dello schema di questo sotto-oggetto.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Versione dello schema. Le configurazioni precedenti non la hanno: si legge come 1.</summary>
        public int Version { get; init; } = CurrentVersion;

        /// <summary>Interruttore del server MCP. Predefinito: spento, nessuna porta aperta.</summary>
        public bool Enabled { get; init; } = false;

        /// <summary>Porta TCP su 127.0.0.1, da 1024 a 65535. Predefinito 8765.</summary>
        public int Port { get; init; } = 8765;
    }

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

        /// <summary>Guadagno. In entrambe le modalità è quello configurato: il controllo automatico regola solo l'esposizione.</summary>
        public int Gain { get; init; } = 250;

        public double MaxExposureMs { get; init; } = 30000;

        public double DayExposureMs { get; init; } = 2;

        /// <summary>Automatica (il controllo regola l'esposizione) o manuale (esposizione fissa a <see cref="ManualExposureMs"/>).</summary>
        public CameraExposureMode ExposureMode { get; init; } = CameraExposureMode.Auto;

        /// <summary>Esposizione fissa in millisecondi, usata solo in modalità manuale. Predefinito scelto [S-scelta]: 5 s, tipico per il cielo notturno.</summary>
        public double ManualExposureMs { get; init; } = 5000;

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

        /// <summary>Correzioni temporali del colore (P14.2). Assenti nei file precedenti: si leggono come tutto spento.</summary>
        public ColorCorrections? Corrections { get; init; } = new();
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

    /// <summary>
    /// Sorgente meteo (sezione <c>weather</c>). Versionata: le configurazioni precedenti non hanno la sezione.
    /// Un valore di <c>source</c> non riconosciuto non attiva nessuna sorgente: la cattura non si ferma.
    /// </summary>
    public sealed record WeatherConfig
    {
        /// <summary>Versione dello schema di questo sotto-oggetto.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Valori ammessi per <see cref="Source"/>.</summary>
        public static class Sources
        {
            /// <summary>Nessuna sorgente. Predefinito.</summary>
            public const string None = "none";

            /// <summary>Driver ASCOM ObservingConditions locale, per ProgID.</summary>
            public const string Ascom = "ascom";

            /// <summary>Open-Meteo, via rete. Richiede <see cref="OpenMeteoAcknowledgedNonCommercial"/>.</summary>
            public const string OpenMeteo = "openmeteo";
        }

        public int Version { get; init; } = CurrentVersion;

        /// <summary>"none" (predefinito), "ascom" o "openmeteo".</summary>
        public string Source { get; init; } = Sources.None;

        /// <summary>ProgID COM del driver ObservingConditions, per la sorgente "ascom". Obbligatorio con quella sorgente.</summary>
        public string? AscomProgId { get; init; } = null;

        /// <summary>Attesa massima per una lettura della sorgente, in secondi. Predefinito 5.</summary>
        public int ReadTimeoutSeconds { get; init; } = 5;

        /// <summary>Età massima di un valore usato quando la sorgente non risponde, in minuti. Predefinito 30.</summary>
        public int MaxAgeMinutes { get; init; } = 30;

        /// <summary>
        /// Conferma dell'uso non commerciale di Open-Meteo (gratuito solo per uso non commerciale, meno di 10.000 chiamate al giorno).
        /// Predefinito falso: senza conferma la sorgente "openmeteo" non parte. Non è un errore di validazione.
        /// </summary>
        public bool OpenMeteoAcknowledgedNonCommercial { get; init; } = false;
    }

    /// <summary>
    /// Collegamento a NINA (sezione <c>nina</c>). Versionato: le configurazioni precedenti non hanno la sezione.
    /// La API di NINA (ninaAPI) non ha autenticazione: per questo l'host e' limitato al loopback. Nessun segreto.
    /// I vincoli di dominio sono in <see cref="ConfigStore"/>: con <c>enabled</c> falso diventano avvisi, non errori.
    /// </summary>
    public sealed record NinaConfig
    {
        /// <summary>Versione dello schema di questo sotto-oggetto.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Valori ammessi per <see cref="Source"/>.</summary>
        public static class Sources
        {
            /// <summary>ninaAPI (HTTP locale di NINA). Predefinito.</summary>
            public const string Api = "api";

            /// <summary>Lettura dei file di log di NINA.</summary>
            public const string Log = "log";
        }

        /// <summary>Host ammessi in questa versione: solo il loopback, perche' la API non ha autenticazione.</summary>
        public static class Hosts
        {
            public const string Loopback = "127.0.0.1";

            public const string Localhost = "localhost";
        }

        /// <summary>Versione dello schema. Le configurazioni precedenti non la hanno: si legge come 1.</summary>
        public int Version { get; init; } = CurrentVersion;

        /// <summary>Interruttore generale. Predefinito: spento.</summary>
        public bool Enabled { get; init; } = false;

        /// <summary>Host della ninaAPI. Ammessi solo "127.0.0.1" e "localhost". Predefinito "127.0.0.1".</summary>
        public string Host { get; init; } = Hosts.Loopback;

        /// <summary>Porta della ninaAPI, da 1 a 65535. Predefinito 1888.</summary>
        public int Port { get; init; } = 1888;

        /// <summary>Attesa massima per la connessione, in secondi, da 1 a 60. Predefinito 5.</summary>
        public int ConnectTimeoutSeconds { get; init; } = 5;

        /// <summary>Ritardo massimo tra due tentativi di riconnessione, in secondi, da 5 a 600. Predefinito 60.</summary>
        public int ReconnectMaxBackoffSeconds { get; init; } = 60;

        /// <summary>Sorgente dei dati: "api" (ninaAPI, predefinito) o "log" (file di log di NINA).</summary>
        public string Source { get; init; } = Sources.Api;

        /// <summary>
        /// Cartella dei log di NINA, per la sorgente "log". Null: la cartella predefinita di NINA
        /// (%localappdata%\NINA\logs), calcolata altrove. Se presente non puo' essere vuota.
        /// </summary>
        public string? LogFolder { get; init; } = null;

        /// <summary>
        /// Correzione manuale dell'orologio tra il PC di NINA e All Sky Brain, in secondi, da -600 a 600. Predefinito 0.
        /// </summary>
        public double ClockOffsetSeconds { get; init; } = 0;
    }

    /// <summary>
    /// Sezione <c>monitor</c>: quali dati il monitoraggio pubblico può inviare (strada A, sola lettura).
    /// Elenco chiuso: le voci sono quelle della classe <see cref="MonitorVoices"/>, nessun campo libero.
    /// Nessun segreto e nessuna posizione. Vincoli in <see cref="ConfigStore"/>.
    /// </summary>
    public sealed record MonitorConfig
    {
        /// <summary>Versione dello schema di questo sotto-oggetto.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Versione dello schema. Le configurazioni precedenti non la hanno: si legge come 1.</summary>
        public int Version { get; init; } = CurrentVersion;

        /// <summary>Interruttore generale «Pubblica il monitoraggio». Predefinito: spento.</summary>
        public bool Enabled { get; init; } = false;

        /// <summary>Voci pubblicabili, una per dato. Tutte spente di default.</summary>
        public MonitorVoices Voices { get; init; } = new();

        /// <summary>Interruttori delle singole voci del monitoraggio. Ogni proprietà è un dato pubblicabile.</summary>
        public sealed record MonitorVoices
        {
            /// <summary>Ultima immagine all-sky.</summary>
            public bool AllSkyImage { get; init; } = false;

            /// <summary>Fase della notte (giorno, crepuscolo, notte).</summary>
            public bool NightPhase { get; init; } = false;

            /// <summary>Altezza del Sole e della Luna.</summary>
            public bool SunMoonAltitude { get; init; } = false;

            /// <summary>Nome del soggetto in ripresa in NINA.</summary>
            public bool NinaTarget { get; init; } = false;

            /// <summary>Stato di park del telescopio.</summary>
            public bool NinaParkState { get; init; } = false;

            /// <summary>Grafico dell'altezza del soggetto nel tempo.</summary>
            public bool TargetAltitudeChart { get; init; } = false;

            /// <summary>Distanza angolare tra il soggetto e la Luna.</summary>
            public bool MoonDistance { get; init; } = false;

            /// <summary>Ultimo timelapse (solo nome file).</summary>
            public bool LatestTimelapse { get; init; } = false;

            /// <summary>Keogram (solo nome file).</summary>
            public bool Keogram { get; init; } = false;

            /// <summary>Start trails (solo nome file).</summary>
            public bool StartTrails { get; init; } = false;

            /// <summary>True se almeno una voce è attiva. Metodo, non proprietà: la serializzazione non deve vederlo.</summary>
            public bool AnyEnabled() =>
                AllSkyImage || NightPhase || SunMoonAltitude || NinaTarget || NinaParkState
                || TargetAltitudeChart || MoonDistance || LatestTimelapse || Keogram || StartTrails;
        }
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

    /// <summary>
    /// Rilevamento di meteore, bolidi e transiti (P5.5). Versionato: le configurazioni precedenti non hanno la sezione
    /// e leggono i valori predefiniti, cioe' rilevamento spento. Le soglie sono quelle dei moduli di AllSky.Detection,
    /// AllSky.Transits ed AllSky.Events; i valori iniziali sono da tarare su cielo reale.
    /// </summary>
    public sealed record DetectionConfig
    {
        /// <summary>Versione dello schema di questo sotto-oggetto.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Versione dello schema. Le configurazioni precedenti non la hanno: si legge come 1.</summary>
        public int Version { get; init; } = CurrentVersion;

        /// <summary>Interruttore generale. Predefinito: spento.</summary>
        public bool Enabled { get; init; } = false;

        /// <summary>
        /// Salva anche i passaggi non identificati (con il verdetto registrato), non solo quelli confermati nelle immagini.
        /// Predefinito: spento.
        /// </summary>
        public bool SaveAllVisiblePasses { get; init; } = false;

        /// <summary>Lato lungo del frame ridotto usato per il rilevamento, in pixel. Predefinito 960.</summary>
        public int AnalysisLongSidePx { get; init; } = 960;

        /// <summary>Frame al secondo delle mini-clip degli eventi. Predefinito 25.</summary>
        public int ClipFps { get; init; } = 25;

        /// <summary>Soglie del rilevatore di scie e del classificatore.</summary>
        public StreakSettings Streak { get; init; } = new();

        /// <summary>Soglie della conferma dei transiti nelle immagini.</summary>
        public TransitSettings Transit { get; init; } = new();

        /// <summary>Soglie di <c>StreakDetector</c> e <c>StreakClassifier</c>, con i valori iniziali dei moduli.</summary>
        public sealed record StreakSettings
        {
            /// <summary>Frame su cui il fondo adattivo fa la media. Predefinito 8.</summary>
            public int BackgroundFrames { get; init; } = 8;

            /// <summary>Moltiplicatore sigma della soglia. Predefinito 3.</summary>
            public double ThresholdSigma { get; init; } = 3.0;

            /// <summary>Soglia minima assoluta della differenza, in livelli 8 bit. Predefinito 6.</summary>
            public double MinDifferenceLuma { get; init; } = 6.0;

            /// <summary>Area minima di una componente, in pixel. Predefinito 15.</summary>
            public int MinAreaPx { get; init; } = 15;

            /// <summary>Lunghezza minima di una scia, in pixel del frame ridotto. Predefinito 25.</summary>
            public double MinLengthPx { get; init; } = 25.0;

            /// <summary>Spessore massimo di una scia, in pixel. Predefinito 6.</summary>
            public double MaxWidthPx { get; init; } = 6.0;

            /// <summary>Elongazione minima. Predefinito 6.</summary>
            public double MinElongation { get; init; } = 6.0;

            /// <summary>Frazione di pixel cambiati oltre la quale il frame e' scartato. Predefinito 0,02.</summary>
            public double MaxChangedFraction { get; init; } = 0.02;

            /// <summary>Distanza massima dalla traccia prevista, in pixel. Predefinito 12.</summary>
            public double TrackDistancePx { get; init; } = 12.0;

            /// <summary>Scarto angolare massimo dalla traccia prevista, in gradi. Predefinito 8.</summary>
            public double TrackAngleDeg { get; init; } = 8.0;

            /// <summary>Spostamento massimo tra frame consecutivi perche' due scie siano la stessa, in pixel. Predefinito 30.</summary>
            public double RepeatShiftPx { get; init; } = 30.0;

            /// <summary>Scarto angolare massimo tra frame consecutivi, in gradi. Predefinito 8.</summary>
            public double RepeatAngleDeg { get; init; } = 8.0;

            /// <summary>
            /// Intervallo massimo, in secondi, tra due frame consecutivi. Predefinito 30. Di fatto non scende sotto
            /// 2,5 volte l'intervallo di cattura, altrimenti ogni frame sarebbe il primo di una sequenza.
            /// </summary>
            public double MaxFrameGapSeconds { get; init; } = 30.0;

            /// <summary>Picco sopra il fondo oltre il quale la scia e' un bolide. Predefinito 200.</summary>
            public double BolideSaturatedPeakLuma { get; init; } = 200.0;

            /// <summary>Luminosita' integrata oltre la quale la scia e' un bolide. Predefinito 4000.</summary>
            public double BolideIntegratedLuma { get; init; } = 4000.0;

            /// <summary>Lunghezza oltre la quale la scia e' un bolide, in pixel del frame ridotto. Predefinito 300.</summary>
            public double BolideLengthPx { get; init; } = 300.0;
        }

        /// <summary>Soglie di <c>TransitConfirmer</c>, con i valori iniziali del modulo.</summary>
        public sealed record TransitSettings
        {
            /// <summary>Semiampiezza del corridoio attorno alla traccia, in pixel. Predefinito 4.</summary>
            public double CorridorHalfWidthPx { get; init; } = 4.0;

            /// <summary>Larghezza di ciascuna fascia di fondo, in pixel. Predefinito 6.</summary>
            public double FlankWidthPx { get; init; } = 6.0;

            /// <summary>Distanza tra due campioni lungo la traccia, in pixel. Predefinito 1.</summary>
            public double SampleSpacingPx { get; init; } = 1.0;

            /// <summary>Soglia in sigma robuste del fondo. Predefinito 5.</summary>
            public double SigmaMultiplier { get; init; } = 5.0;

            /// <summary>Soglia minima assoluta di eccesso, in livelli 8 bit. Predefinito 10.</summary>
            public double MinExcessLevels { get; init; } = 10.0;

            /// <summary>Frazione della traccia sopra soglia perche' un frame sia identificato. Predefinito 0,6.</summary>
            public double ScoreThreshold { get; init; } = 0.6;

            /// <summary>Lunghezza minima del tratto visibile, in pixel. Predefinito 40.</summary>
            public double MinVisibleTrackPx { get; init; } = 40.0;
        }
    }
}
