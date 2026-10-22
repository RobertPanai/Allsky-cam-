using System.Globalization;
using AllSky.Core.Shell;

namespace AllSky.Core;

/// <summary>
/// Testi dell'app desktop: finestra, icona nell'area di notifica e messaggi. Due istanze fisse, <see cref="Italian"/>
/// (predefinita) e <see cref="English"/>. Classe pura: nessuna dipendenza da WPF o WinForms, quindi testabile ovunque.
/// </summary>
/// <remarks>
/// Le stringhe con segnaposto {0}, {1}... vanno passate a <c>string.Format</c>. Ogni segnaposto deve comparire in
/// entrambe le lingue, lo verifica il test DesktopTextsTests.
/// </remarks>
public sealed record DesktopTexts
{
    /// <summary>Titolo della finestra e dei messaggi.</summary>
    public required string WindowTitle { get; init; }

    /// <summary>Voce di menu "Apri All Sky Brain".</summary>
    public required string OpenWindow { get; init; }

    /// <summary>Voce di menu "Apri pannello nel browser".</summary>
    public required string OpenPanel { get; init; }

    /// <summary>Voce di menu "Avvia con Windows".</summary>
    public required string StartWithWindows { get; init; }

    /// <summary>Voce di menu "Controlla aggiornamenti".</summary>
    public required string CheckForUpdates { get; init; }

    /// <summary>Voce di menu "Esci".</summary>
    public required string Exit { get; init; }

    /// <summary>Etichetta dello stato del servizio nella finestra.</summary>
    public required string StateLabel { get; init; }

    /// <summary>Etichetta della camera nella finestra.</summary>
    public required string CameraLabel { get; init; }

    /// <summary>Testo mostrato quando la camera non è collegata.</summary>
    public required string CameraNotConnected { get; init; }

    /// <summary>Etichetta dell'ultimo frame nella finestra.</summary>
    public required string LastFrameLabel { get; init; }

    /// <summary>Testo mostrato quando non è ancora stato archiviato alcun frame.</summary>
    public required string NoFrameYet { get; init; }

    /// <summary>Etichetta dell'errore nella finestra.</summary>
    public required string ErrorLabel { get; init; }

    /// <summary>Stato "idle" del servizio.</summary>
    public required string StateIdle { get; init; }

    /// <summary>Stato "capturing" del servizio.</summary>
    public required string StateCapturing { get; init; }

    /// <summary>Stato "error" del servizio.</summary>
    public required string StateError { get; init; }

    /// <summary>Stato non riconosciuto.</summary>
    public required string StateUnknown { get; init; }

    /// <summary>Messaggio quando il browser non si apre; {0} è l'indirizzo.</summary>
    public required string BrowserError { get; init; }

    /// <summary>Messaggio quando non si modifica l'avvio automatico; {0} è il dettaglio.</summary>
    public required string AutostartError { get; init; }

    /// <summary>Messaggio "già in esecuzione" (seconda istanza).</summary>
    public required string AlreadyRunning { get; init; }

    /// <summary>Messaggio di avvio fallito; {0} è la porta, {1} il dettaglio, {2} il percorso del registro.</summary>
    public required string StartupError { get; init; }

    /// <summary>Messaggio di errore imprevisto; {0} è il dettaglio, {1} il percorso del registro.</summary>
    public required string CrashMessage { get; init; }

    /// <summary>Esito "aggiornato"; {0} è la versione installata.</summary>
    public required string UpdateUpToDate { get; init; }

    /// <summary>Domanda sul download; {0} è la versione disponibile.</summary>
    public required string UpdateAvailable { get; init; }

    /// <summary>Domanda sul riavvio; {0} è la versione scaricata.</summary>
    public required string UpdateReady { get; init; }

    /// <summary>Aggiornamenti non disponibili in questa installazione.</summary>
    public required string UpdateDisabled { get; init; }

    /// <summary>Errore di rete durante l'aggiornamento.</summary>
    public required string UpdateNetworkError { get; init; }

    /// <summary>Errore di aggiornamento non classificato.</summary>
    public required string UpdateGenericError { get; init; }

    /// <summary>Pulsante "Scarica".</summary>
    public required string Download { get; init; }

    /// <summary>Pulsante "Più tardi".</summary>
    public required string Later { get; init; }

    /// <summary>Pulsante "Riavvia per aggiornare".</summary>
    public required string Restart { get; init; }

    /// <summary>Cultura per numeri e orari della barra di stato (non è un testo).</summary>
    public required CultureInfo Culture { get; init; }

    /// <summary>Valore di un dato non disponibile.</summary>
    public required string NotAvailable { get; init; }

    /// <summary>Spiegazione al passaggio del mouse su un dato non disponibile.</summary>
    public required string NotAvailableTooltip { get; init; }

    /// <summary>Etichetta della fase del cielo nella barra di stato.</summary>
    public required string PhaseLabel { get; init; }

    /// <summary>Etichetta dell'altezza del Sole nella barra di stato.</summary>
    public required string SunLabel { get; init; }

    /// <summary>Etichetta dell'altezza della Luna nella barra di stato.</summary>
    public required string MoonLabel { get; init; }

    /// <summary>Etichetta dello spazio disco nella barra di stato.</summary>
    public required string DiskLabel { get; init; }

    /// <summary>Etichetta delle pubblicazioni nella barra di stato.</summary>
    public required string PublicationsLabel { get; init; }

    /// <summary>Valore della camera collegata quando il nome non è noto.</summary>
    public required string CameraConnected { get; init; }

    /// <summary>Fase "giorno".</summary>
    public required string PhaseDay { get; init; }

    /// <summary>Fase "crepuscolo".</summary>
    public required string PhaseTwilight { get; init; }

    /// <summary>Fase "notte".</summary>
    public required string PhaseNight { get; init; }

    /// <summary>Spazio libero sul disco; {0} è la quantità in GB.</summary>
    public required string DiskFree { get; init; }

    /// <summary>Pubblicazioni aggiornate.</summary>
    public required string PublicationOk { get; init; }

    /// <summary>Pubblicazione in coda.</summary>
    public required string PublicationPending { get; init; }

    /// <summary>Pubblicazione fallita.</summary>
    public required string PublicationError { get; init; }

    /// <summary>Pulsante della modalità notte rossa.</summary>
    public required string NightMode { get; init; }

    /// <summary>Titolo accessibile della barra laterale.</summary>
    public required string SidebarLabel { get; init; }

    /// <summary>Suggerimento di una voce della barra laterale; {0} è il nome della vista, {1} il numero della scorciatoia.</summary>
    public required string NavTooltip { get; init; }

    /// <summary>Voce "Stanotte".</summary>
    public required string NavTonight { get; init; }

    /// <summary>Voce "Live".</summary>
    public required string NavLive { get; init; }

    /// <summary>Voce "Camera".</summary>
    public required string NavCamera { get; init; }

    /// <summary>Voce "Notti".</summary>
    public required string NavNights { get; init; }

    /// <summary>Voce "Pubblicazione".</summary>
    public required string NavPublication { get; init; }

    /// <summary>Voce "Impostazioni".</summary>
    public required string NavSettings { get; init; }

    /// <summary>Voce "Log".</summary>
    public required string NavLog { get; init; }

    /// <summary>Stato vuoto della vista "Stanotte".</summary>
    public required string EmptyTonight { get; init; }

    /// <summary>Stato vuoto della vista "Live".</summary>
    public required string EmptyLive { get; init; }

    /// <summary>Stato vuoto della vista "Camera".</summary>
    public required string EmptyCamera { get; init; }

    /// <summary>Stato vuoto della vista "Notti".</summary>
    public required string EmptyNights { get; init; }

    /// <summary>Stato vuoto della vista "Pubblicazione".</summary>
    public required string EmptyPublication { get; init; }

    /// <summary>Stato vuoto della vista "Impostazioni".</summary>
    public required string EmptySettings { get; init; }

    /// <summary>Stato vuoto della vista "Log".</summary>
    public required string EmptyLog { get; init; }

    /// <summary>Interruttore "Grezzo" della vista Live (spento = elaborato).</summary>
    public required string LiveRaw { get; init; }

    /// <summary>Spiegazione dell'interruttore "Grezzo" della vista Live.</summary>
    public required string LiveRawTooltip { get; init; }

    /// <summary>Pulsante di zoom avanti della vista Live.</summary>
    public required string LiveZoomIn { get; init; }

    /// <summary>Pulsante di zoom indietro della vista Live.</summary>
    public required string LiveZoomOut { get; init; }

    /// <summary>Pulsante che riporta la vista Live a zoom 1 e centrata.</summary>
    public required string LiveZoomReset { get; init; }

    /// <summary>Suggerimento sui comandi di zoom e trascinamento della vista Live.</summary>
    public required string LiveZoomHint { get; init; }

    /// <summary>Nome accessibile dell'immagine corrente del cielo.</summary>
    public required string LiveImageName { get; init; }

    /// <summary>Titolo del pannello dati del frame.</summary>
    public required string LiveDataTitle { get; init; }

    /// <summary>Etichetta dell'ora del frame.</summary>
    public required string LiveDataTime { get; init; }

    /// <summary>Tooltip dell'ora del frame; {0} è l'identificativo UTC del frame.</summary>
    public required string LiveDataTimeTooltip { get; init; }

    /// <summary>Etichetta dell'esposizione.</summary>
    public required string LiveDataExposure { get; init; }

    /// <summary>Etichetta del gain.</summary>
    public required string LiveDataGain { get; init; }

    /// <summary>Etichetta della mediana ADU.</summary>
    public required string LiveDataMedian { get; init; }

    /// <summary>Etichetta della temperatura del sensore.</summary>
    public required string LiveDataTemperature { get; init; }

    /// <summary>Titolo del pannello degli overlay.</summary>
    public required string LiveOverlaysTitle { get; init; }

    /// <summary>Overlay griglia alt/az (non ancora disponibile).</summary>
    public required string LiveOverlayGrid { get; init; }

    /// <summary>Overlay punti cardinali (non ancora disponibile).</summary>
    public required string LiveOverlayCardinal { get; init; }

    /// <summary>Overlay tracce satelliti (non ancora disponibile).</summary>
    public required string LiveOverlaySatellites { get; init; }

    /// <summary>Spiegazione degli overlay non ancora disponibili.</summary>
    public required string LiveOverlayPending { get; init; }

    /// <summary>Messaggio quando l'immagine non si può mostrare; {0} è il dettaglio.</summary>
    public required string LiveLoadError { get; init; }

    /// <summary>Etichetta del filtro per sorgente nella vista "Camera".</summary>
    public required string CameraSourceLabel { get; init; }

    /// <summary>Sorgente "tutte" nel filtro della vista "Camera".</summary>
    public required string CameraSourceAll { get; init; }

    /// <summary>Sorgente "simulatore" (camera integrata senza hardware).</summary>
    public required string CameraSourceSimulator { get; init; }

    /// <summary>Sorgente "ASCOM COM" (driver registrati su Windows).</summary>
    public required string CameraSourceAscom { get; init; }

    /// <summary>Sorgente "ASCOM Alpaca" (camere trovate in rete).</summary>
    public required string CameraSourceAlpaca { get; init; }

    /// <summary>Titolo del pannello di ricerca.</summary>
    public required string CameraSearchTitle { get; init; }

    /// <summary>Pulsante "Cerca camere".</summary>
    public required string CameraSearch { get; init; }

    /// <summary>Testo mentre la ricerca è in corso.</summary>
    public required string CameraSearching { get; init; }

    /// <summary>Intestazione dell'elenco delle camere trovate.</summary>
    public required string CameraFoundLabel { get; init; }

    /// <summary>Elenco vuoto dopo una ricerca.</summary>
    public required string CameraNoneFound { get; init; }

    /// <summary>Suggerimento quando la sorgente ASCOM COM non ha driver; visibile con filtro ASCOM.</summary>
    public required string CameraHintAscom { get; init; }

    /// <summary>Suggerimento quando la discovery Alpaca non trova camere; visibile con filtro Alpaca.</summary>
    public required string CameraHintAlpaca { get; init; }

    /// <summary>Suggerimento quando non c'è nessuna camera; il simulatore è sempre disponibile.</summary>
    public required string CameraHintSimulator { get; init; }

    /// <summary>Titolo del pannello di collegamento.</summary>
    public required string CameraConnectionTitle { get; init; }

    /// <summary>Pulsante "Collega camera".</summary>
    public required string CameraConnect { get; init; }

    /// <summary>Testo mentre la connessione è in corso.</summary>
    public required string CameraConnecting { get; init; }

    /// <summary>Stato con camera collegata; {0} è il nome, {1} il tipo di sorgente.</summary>
    public required string CameraConnectedAs { get; init; }

    /// <summary>Stato senza camera collegata.</summary>
    public required string CameraNoneConnected { get; init; }

    /// <summary>Messaggio per chi cerca di disconnettere: la disconnessione non è ancora disponibile.</summary>
    public required string CameraDisconnectUnavailable { get; init; }

    /// <summary>Invito a selezionare una camera prima di collegarla.</summary>
    public required string CameraSelectFirst { get; init; }

    /// <summary>Titolo del pannello delle impostazioni di cattura.</summary>
    public required string CameraSettingsTitle { get; init; }

    /// <summary>Etichetta del guadagno.</summary>
    public required string CameraGainLabel { get; init; }

    /// <summary>Suggerimento sotto il campo guadagno.</summary>
    public required string CameraGainHint { get; init; }

    /// <summary>Etichetta dell'esposizione massima.</summary>
    public required string CameraMaxExposureLabel { get; init; }

    /// <summary>Suggerimento sotto il campo dell'esposizione massima.</summary>
    public required string CameraMaxExposureHint { get; init; }

    /// <summary>Pulsante "Salva e applica".</summary>
    public required string CameraSave { get; init; }

    /// <summary>Esito di un salvataggio riuscito.</summary>
    public required string CameraSaved { get; init; }

    /// <summary>Salvataggio senza modifiche.</summary>
    public required string CameraNoChanges { get; init; }

    /// <summary>Problema di un campo vuoto; in fondo al campo.</summary>
    public required string CameraProblemEmpty { get; init; }

    /// <summary>Problema di un campo che non è un numero.</summary>
    public required string CameraProblemNotNumber { get; init; }

    /// <summary>Problema del guadagno che non è un numero intero.</summary>
    public required string CameraProblemNotInteger { get; init; }

    /// <summary>Problema del guadagno negativo.</summary>
    public required string CameraProblemNegative { get; init; }

    /// <summary>Problema di un valore che deve essere maggiore di zero.</summary>
    public required string CameraProblemNotPositive { get; init; }

    /// <summary>Titolo del segnaposto della calibrazione ottica.</summary>
    public required string CameraCalibrationTitle { get; init; }

    /// <summary>Testo del segnaposto della calibrazione ottica (la vista arriva in P3.3).</summary>
    public required string CameraCalibrationEmpty { get; init; }

    /// <summary>Errore: il servizio non è avviato.</summary>
    public required string CameraErrorServiceNotStarted { get; init; }

    /// <summary>Errore: la camera è monocromatica (non supportata).</summary>
    public required string CameraErrorMonochrome { get; init; }

    /// <summary>Errore: sensore non supportato diverso dal monocromatico.</summary>
    public required string CameraErrorUnsupportedSensor { get; init; }

    /// <summary>Errore: il driver riporta limiti di esposizione non validi; {0} e {1} sono i valori del driver, {2} e {3} quelli usati.</summary>
    public required string CameraErrorDriverExposureLimits { get; init; }

    /// <summary>Errore: il driver riporta limiti di esposizione non validi, senza valori leggibili.</summary>
    public required string CameraErrorDriverExposureLimitsUnknown { get; init; }

    /// <summary>Errore: la cattura non è partita con la camera collegata.</summary>
    public required string CameraErrorCaptureStart { get; init; }

    /// <summary>Errore: la ricerca delle camere non è riuscita.</summary>
    public required string CameraErrorDiscovery { get; init; }

    /// <summary>Errore: la camera non risponde alla connessione.</summary>
    public required string CameraErrorConnection { get; init; }

    /// <summary>Errore: impostazioni non valide.</summary>
    public required string CameraErrorConfigInvalid { get; init; }

    /// <summary>Errore: impossibile salvare la configurazione.</summary>
    public required string CameraErrorSaveFailed { get; init; }

    /// <summary>Errore non classificato.</summary>
    public required string CameraErrorUnknown { get; init; }

    /// <summary>Riga di dettaglio sotto un errore; {0} è il testo tecnico, già ridotto a una riga.</summary>
    public required string CameraErrorDetail { get; init; }

    /// <summary>Testi in italiano (lingua predefinita).</summary>
    public static DesktopTexts Italian { get; } = new()
    {
        WindowTitle = "All Sky Brain",
        OpenWindow = "Apri All Sky Brain",
        OpenPanel = "Apri pannello nel browser",
        StartWithWindows = "Avvia con Windows",
        CheckForUpdates = "Controlla aggiornamenti",
        Exit = "Esci",
        StateLabel = "Stato",
        CameraLabel = "Camera",
        CameraNotConnected = "non collegata",
        LastFrameLabel = "Ultimo frame",
        NoFrameYet = "nessuno ancora",
        ErrorLabel = "Errore",
        StateIdle = "in attesa",
        StateCapturing = "cattura in corso",
        StateError = "errore",
        StateUnknown = "sconosciuto",
        BrowserError = "Impossibile aprire il browser. Apri questo indirizzo a mano: {0}",
        AutostartError = "Impossibile modificare l'avvio automatico con Windows.\n\n{0}",
        AlreadyRunning = "All Sky Brain è già in esecuzione (icona nella barra in basso).",
        StartupError = "Impossibile avviare All Sky Brain: la porta {0} potrebbe essere occupata. "
            + "Chiudi l'altro programma o cambia la porta in Impostazioni.\n\nDettagli: {1}\n\nRegistro: {2}",
        CrashMessage = "All Sky Brain ha riscontrato un errore imprevisto e potrebbe chiudersi.\n\nDettagli: {0}\n\nRegistro: {1}",
        UpdateUpToDate = "All Sky Brain è aggiornato (versione {0}).",
        UpdateAvailable = "È disponibile la versione {0} di All Sky Brain. Scaricarla ora?",
        UpdateReady = "La versione {0} è pronta. Riavviare All Sky Brain per installarla?",
        UpdateDisabled = "Gli aggiornamenti automatici non sono disponibili in questa installazione.",
        UpdateNetworkError = "Impossibile contattare il server degli aggiornamenti. Controllare la connessione e riprovare.",
        UpdateGenericError = "Aggiornamento non riuscito. Riprovare più tardi.",
        Download = "Scarica",
        Later = "Più tardi",
        Restart = "Riavvia per aggiornare",
        Culture = new CultureInfo("it-IT"),
        NotAvailable = "n/d",
        NotAvailableTooltip = "Dato non disponibile in questo momento.",
        PhaseLabel = "Fase",
        SunLabel = "Sole",
        MoonLabel = "Luna",
        DiskLabel = "Disco",
        PublicationsLabel = "Pubblicazioni",
        CameraConnected = "collegata",
        PhaseDay = "giorno",
        PhaseTwilight = "crepuscolo",
        PhaseNight = "notte",
        DiskFree = "{0} GB liberi",
        PublicationOk = "aggiornate",
        PublicationPending = "in coda",
        PublicationError = "errore",
        NightMode = "Notte rossa",
        SidebarLabel = "Navigazione",
        NavTooltip = "{0} (Ctrl+{1})",
        NavTonight = "Stanotte",
        NavLive = "Live",
        NavCamera = "Camera",
        NavNights = "Notti",
        NavPublication = "Pubblicazione",
        NavSettings = "Impostazioni",
        NavLog = "Log",
        EmptyTonight = "Nessuna notte osservativa in corso. Il riepilogo di stanotte comparirà qui.",
        EmptyLive = "Nessun frame ancora. L'immagine del cielo comparirà qui al primo frame archiviato.",
        EmptyCamera = "Nessuna camera collegata. La ricerca e la connessione saranno disponibili in questa vista.",
        EmptyNights = "Nessuna notte archiviata. Le notti con frame compariranno qui, con keogram e timelapse.",
        EmptyPublication = "Nessuna pubblicazione ancora. Uplink e YouTube si gestiscono da questa vista.",
        EmptySettings = "Posizione dell'osservatorio, lingua e avvio con Windows si trovano in questa vista.",
        EmptyLog = "Nessuna riga di registro da mostrare.",
        LiveRaw = "Grezzo",
        LiveRawTooltip = "Mostra la versione grezza ricalcolata dall'ultimo frame, senza elaborazione.",
        LiveZoomIn = "Ingrandisci",
        LiveZoomOut = "Riduci",
        LiveZoomReset = "Ripristina vista",
        LiveZoomHint = "Rotella per ingrandire, trascinamento per spostare, doppio clic per ripristinare.",
        LiveImageName = "Immagine corrente del cielo",
        LiveDataTitle = "Dati del frame",
        LiveDataTime = "Ora frame",
        LiveDataTimeTooltip = "Identificativo UTC: {0}",
        LiveDataExposure = "Esposizione",
        LiveDataGain = "Gain",
        LiveDataMedian = "Mediana ADU",
        LiveDataTemperature = "Temperatura sensore",
        LiveOverlaysTitle = "Overlay",
        LiveOverlayGrid = "Griglia alt/az",
        LiveOverlayCardinal = "Punti cardinali",
        LiveOverlaySatellites = "Tracce satelliti",
        LiveOverlayPending = "Disponibile dopo la calibrazione.",
        LiveLoadError = "Impossibile mostrare l'immagine: {0}",
        CameraSourceLabel = "Sorgente",
        CameraSourceAll = "Tutte le sorgenti",
        CameraSourceSimulator = "Simulatore",
        CameraSourceAscom = "ASCOM (driver COM)",
        CameraSourceAlpaca = "ASCOM Alpaca (rete)",
        CameraSearchTitle = "Ricerca",
        CameraSearch = "Cerca camere",
        CameraSearching = "Ricerca in corso…",
        CameraFoundLabel = "Camere trovate",
        CameraNoneFound = "Nessuna camera trovata con questo filtro.",
        CameraHintAscom = "Nessun driver ASCOM registrato. Installare la piattaforma ASCOM e il driver della camera, poi cercare di nuovo.",
        CameraHintAlpaca = "Nessuna camera Alpaca sulla rete locale. Controllare che il server Alpaca sia avviato e che il firewall lasci passare la ricerca UDP sulla porta 32227.",
        CameraHintSimulator = "Il simulatore è sempre disponibile per le prove.",
        CameraConnectionTitle = "Collegamento",
        CameraConnect = "Collega camera",
        CameraConnecting = "Connessione in corso…",
        CameraConnectedAs = "Collegata: {0} ({1})",
        CameraNoneConnected = "Nessuna camera collegata.",
        CameraDisconnectUnavailable = "Per cambiare camera, collegarne un'altra: la disconnessione non è ancora disponibile.",
        CameraSelectFirst = "Selezionare una camera dall'elenco.",
        CameraSettingsTitle = "Impostazioni di cattura",
        CameraGainLabel = "Guadagno",
        CameraGainHint = "Numero intero, 0 o più.",
        CameraMaxExposureLabel = "Esposizione massima (ms)",
        CameraMaxExposureHint = "Millisecondi, maggiore di zero.",
        CameraSave = "Salva e applica",
        CameraSaved = "Impostazioni salvate e applicate.",
        CameraNoChanges = "Nessuna modifica da salvare.",
        CameraProblemEmpty = "Inserire un valore.",
        CameraProblemNotNumber = "Inserire un numero.",
        CameraProblemNotInteger = "Il guadagno deve essere un numero intero.",
        CameraProblemNegative = "Il guadagno non può essere negativo.",
        CameraProblemNotPositive = "Il valore deve essere maggiore di zero.",
        CameraCalibrationTitle = "Calibrazione ottica",
        CameraCalibrationEmpty = "Disponibile in una versione successiva: qui compariranno la raccolta dei punti e l'anteprima della griglia.",
        CameraErrorServiceNotStarted = "Il servizio non è avviato. Avviarlo, poi collegare la camera.",
        CameraErrorMonochrome = "La camera è monocromatica. All Sky Brain supporta solo camere a colori con matrice di Bayer (RGGB), quindi con questa camera non si può fare il timelapse a colori. Scegliere un'altra camera.",
        CameraErrorUnsupportedSensor = "Il sensore di questa camera non è supportato. All Sky Brain richiede una matrice di Bayer RGGB. Scegliere un'altra camera.",
        CameraErrorDriverExposureLimits = "Il driver riporta limiti di esposizione non validi (min {0} ms, max {1} ms). All Sky Brain usa invece min {2} ms e max {3} ms. Se le immagini sono sempre troppo scure o troppo chiare, controllare l'esposizione massima qui sotto.",
        CameraErrorDriverExposureLimitsUnknown = "Il driver riporta limiti di esposizione non validi. All Sky Brain usa valori di sicurezza. Se le immagini sono sempre troppo scure o troppo chiare, controllare l'esposizione massima qui sotto.",
        CameraErrorCaptureStart = "La cattura non è partita con questa camera. Controllare guadagno ed esposizione massima, poi riprovare.",
        CameraErrorDiscovery = "La ricerca delle camere non è riuscita. Controllare che il driver ASCOM o il server Alpaca siano installati e attivi, poi riprovare.",
        CameraErrorConnection = "La camera non risponde. Controllare che sia accesa e collegata (USB o rete), poi riprovare.",
        CameraErrorConfigInvalid = "Impostazioni non valide: correggere i campi evidenziati e salvare di nuovo.",
        CameraErrorSaveFailed = "Impossibile salvare le impostazioni. Controllare i permessi della cartella dati e riprovare.",
        CameraErrorUnknown = "Operazione non riuscita. Riprovare; se il problema continua, consultare il log.",
        CameraErrorDetail = "Dettaglio: {0}",
    };

    /// <summary>Testi in inglese.</summary>
    public static DesktopTexts English { get; } = new()
    {
        WindowTitle = "All Sky Brain",
        OpenWindow = "Open All Sky Brain",
        OpenPanel = "Open panel in browser",
        StartWithWindows = "Start with Windows",
        CheckForUpdates = "Check for updates",
        Exit = "Exit",
        StateLabel = "State",
        CameraLabel = "Camera",
        CameraNotConnected = "not connected",
        LastFrameLabel = "Last frame",
        NoFrameYet = "none yet",
        ErrorLabel = "Error",
        StateIdle = "idle",
        StateCapturing = "capturing",
        StateError = "error",
        StateUnknown = "unknown",
        BrowserError = "Could not open the browser. Open this address manually: {0}",
        AutostartError = "Could not change the start-with-Windows setting.\n\n{0}",
        AlreadyRunning = "All Sky Brain is already running (icon in the taskbar tray).",
        StartupError = "Could not start All Sky Brain: port {0} may be in use. "
            + "Close the other program or change the port in Settings.\n\nDetails: {1}\n\nLog: {2}",
        CrashMessage = "All Sky Brain ran into an unexpected error and may close.\n\nDetails: {0}\n\nLog: {1}",
        UpdateUpToDate = "All Sky Brain is up to date (version {0}).",
        UpdateAvailable = "Version {0} of All Sky Brain is available. Download it now?",
        UpdateReady = "Version {0} is ready. Restart All Sky Brain to install it?",
        UpdateDisabled = "Automatic updates are not available in this installation.",
        UpdateNetworkError = "Could not reach the update server. Check your connection and try again.",
        UpdateGenericError = "Update failed. Try again later.",
        Download = "Download",
        Later = "Later",
        Restart = "Restart to update",
        Culture = new CultureInfo("en-US"),
        NotAvailable = "n/a",
        NotAvailableTooltip = "Data not available right now.",
        PhaseLabel = "Phase",
        SunLabel = "Sun",
        MoonLabel = "Moon",
        DiskLabel = "Disk",
        PublicationsLabel = "Publishing",
        CameraConnected = "connected",
        PhaseDay = "day",
        PhaseTwilight = "twilight",
        PhaseNight = "night",
        DiskFree = "{0} GB free",
        PublicationOk = "up to date",
        PublicationPending = "queued",
        PublicationError = "error",
        NightMode = "Red night",
        SidebarLabel = "Navigation",
        NavTooltip = "{0} (Ctrl+{1})",
        NavTonight = "Tonight",
        NavLive = "Live",
        NavCamera = "Camera",
        NavNights = "Nights",
        NavPublication = "Publishing",
        NavSettings = "Settings",
        NavLog = "Log",
        EmptyTonight = "No observing night in progress. Tonight's summary will appear here.",
        EmptyLive = "No frame yet. The sky image will appear here with the first archived frame.",
        EmptyCamera = "No camera connected. Search and connection will be available in this view.",
        EmptyNights = "No archived night yet. Nights with frames will appear here, with keogram and timelapse.",
        EmptyPublication = "Nothing published yet. Uplink and YouTube are managed from this view.",
        EmptySettings = "Observatory location, language and start with Windows are in this view.",
        EmptyLog = "No log lines to show.",
        LiveRaw = "Raw",
        LiveRawTooltip = "Shows the raw version recomputed from the latest frame, without processing.",
        LiveZoomIn = "Zoom in",
        LiveZoomOut = "Zoom out",
        LiveZoomReset = "Reset view",
        LiveZoomHint = "Wheel to zoom, drag to pan, double-click to reset.",
        LiveImageName = "Current sky image",
        LiveDataTitle = "Frame data",
        LiveDataTime = "Frame time",
        LiveDataTimeTooltip = "UTC identifier: {0}",
        LiveDataExposure = "Exposure",
        LiveDataGain = "Gain",
        LiveDataMedian = "Median ADU",
        LiveDataTemperature = "Sensor temperature",
        LiveOverlaysTitle = "Overlays",
        LiveOverlayGrid = "Alt/az grid",
        LiveOverlayCardinal = "Cardinal points",
        LiveOverlaySatellites = "Satellite tracks",
        LiveOverlayPending = "Available after calibration.",
        LiveLoadError = "Cannot show the image: {0}",
        CameraSourceLabel = "Source",
        CameraSourceAll = "All sources",
        CameraSourceSimulator = "Simulator",
        CameraSourceAscom = "ASCOM (COM driver)",
        CameraSourceAlpaca = "ASCOM Alpaca (network)",
        CameraSearchTitle = "Search",
        CameraSearch = "Search cameras",
        CameraSearching = "Searching…",
        CameraFoundLabel = "Cameras found",
        CameraNoneFound = "No camera found with this filter.",
        CameraHintAscom = "No ASCOM driver is registered. Install the ASCOM platform and the camera driver, then search again.",
        CameraHintAlpaca = "No Alpaca camera on the local network. Check that the Alpaca server is running and that the firewall allows UDP discovery on port 32227.",
        CameraHintSimulator = "The simulator is always available for tests.",
        CameraConnectionTitle = "Connection",
        CameraConnect = "Connect camera",
        CameraConnecting = "Connecting…",
        CameraConnectedAs = "Connected: {0} ({1})",
        CameraNoneConnected = "No camera connected.",
        CameraDisconnectUnavailable = "To change camera, connect another one: disconnecting is not available yet.",
        CameraSelectFirst = "Select a camera from the list.",
        CameraSettingsTitle = "Capture settings",
        CameraGainLabel = "Gain",
        CameraGainHint = "Whole number, 0 or more.",
        CameraMaxExposureLabel = "Maximum exposure (ms)",
        CameraMaxExposureHint = "Milliseconds, greater than zero.",
        CameraSave = "Save and apply",
        CameraSaved = "Settings saved and applied.",
        CameraNoChanges = "No changes to save.",
        CameraProblemEmpty = "Enter a value.",
        CameraProblemNotNumber = "Enter a number.",
        CameraProblemNotInteger = "Gain must be a whole number.",
        CameraProblemNegative = "Gain cannot be negative.",
        CameraProblemNotPositive = "The value must be greater than zero.",
        CameraCalibrationTitle = "Optical calibration",
        CameraCalibrationEmpty = "Coming in a later version: point collection and the grid preview will appear here.",
        CameraErrorServiceNotStarted = "The service is not running. Start it, then connect the camera.",
        CameraErrorMonochrome = "This camera is monochrome. All Sky Brain only supports colour cameras with a Bayer matrix (RGGB), so a colour timelapse is not possible with this one. Choose another camera.",
        CameraErrorUnsupportedSensor = "This camera's sensor is not supported. All Sky Brain requires an RGGB Bayer matrix. Choose another camera.",
        CameraErrorDriverExposureLimits = "The driver reports invalid exposure limits (min {0} ms, max {1} ms). All Sky Brain uses min {2} ms and max {3} ms instead. If images are always too dark or too bright, check the maximum exposure below.",
        CameraErrorDriverExposureLimitsUnknown = "The driver reports invalid exposure limits. All Sky Brain uses safe values. If images are always too dark or too bright, check the maximum exposure below.",
        CameraErrorCaptureStart = "Capture did not start with this camera. Check gain and maximum exposure, then try again.",
        CameraErrorDiscovery = "Camera search failed. Check that the ASCOM driver or the Alpaca server is installed and running, then try again.",
        CameraErrorConnection = "The camera is not responding. Check that it is powered on and connected (USB or network), then try again.",
        CameraErrorConfigInvalid = "Settings are not valid: fix the highlighted fields and save again.",
        CameraErrorSaveFailed = "Could not save the settings. Check the permissions of the data folder and try again.",
        CameraErrorUnknown = "Operation failed. Try again; if the problem persists, check the log.",
        CameraErrorDetail = "Detail: {0}",
    };

    /// <summary>Nome della vista nella barra laterale e come titolo della schermata.</summary>
    /// <param name="view">Vista da nominare.</param>
    public string ViewName(ShellView view) => view switch
    {
        ShellView.Tonight => NavTonight,
        ShellView.Live => NavLive,
        ShellView.Camera => NavCamera,
        ShellView.Nights => NavNights,
        ShellView.Publication => NavPublication,
        ShellView.Settings => NavSettings,
        ShellView.Log => NavLog,
    };

    /// <summary>Stato vuoto curato di una vista segnaposto.</summary>
    /// <param name="view">Vista di cui serve il testo.</param>
    public string EmptyText(ShellView view) => view switch
    {
        ShellView.Tonight => EmptyTonight,
        ShellView.Live => EmptyLive,
        ShellView.Camera => EmptyCamera,
        ShellView.Nights => EmptyNights,
        ShellView.Publication => EmptyPublication,
        ShellView.Settings => EmptySettings,
        ShellView.Log => EmptyLog,
    };

    /// <summary>Traduce lo stato del servizio ("idle", "capturing", "error") nel testo della lingua corrente.</summary>
    /// <param name="state">Valore di <c>StatusDto.State</c>.</param>
    public string StateName(string state) => state switch
    {
        "idle" => StateIdle,
        "capturing" => StateCapturing,
        "error" => StateError,
        _ => StateUnknown,
    };

    /// <summary>
    /// Testo dell'ultimo frame: identificativo e ora locale (HH:mm:ss). Senza frame restituisce <see cref="NoFrameYet"/>.
    /// </summary>
    /// <param name="frameId">Identificativo del frame; null o vuoto se non c'è.</param>
    /// <param name="timestamp">Istante di cattura in UTC; null se sconosciuto.</param>
    public string FormatLastFrame(string? frameId, DateTimeOffset? timestamp)
    {
        if (string.IsNullOrEmpty(frameId))
        {
            return NoFrameYet;
        }

        if (timestamp is null)
        {
            return frameId;
        }

        return $"{frameId} ({timestamp.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)})";
    }
}
