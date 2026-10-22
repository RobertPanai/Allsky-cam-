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

    /// <summary>Pulsante per scollegare la camera collegata.</summary>
    public required string CameraDiscButton { get; init; }

    /// <summary>Titolo della vista Impostazioni.</summary>
    public required string SettingsTitle { get; init; }

    /// <summary>Sezione posizione dell'osservatorio.</summary>
    public required string SettingsObservatoryTitle { get; init; }

    /// <summary>Campo latitudine.</summary>
    public required string SettingsLatitude { get; init; }

    /// <summary>Campo longitudine.</summary>
    public required string SettingsLongitude { get; init; }

    /// <summary>Campo quota.</summary>
    public required string SettingsElevation { get; init; }

    /// <summary>Intervalli ammessi per latitudine, longitudine e quota.</summary>
    public required string SettingsSiteHint { get; init; }

    /// <summary>Campo di posizione vuoto.</summary>
    public required string SettingsProblemEmpty { get; init; }

    /// <summary>Campo di posizione non numerico.</summary>
    public required string SettingsProblemNotNumber { get; init; }

    /// <summary>Latitudine fuori intervallo.</summary>
    public required string SettingsProblemLatitude { get; init; }

    /// <summary>Longitudine fuori intervallo.</summary>
    public required string SettingsProblemLongitude { get; init; }

    /// <summary>Quota fuori intervallo.</summary>
    public required string SettingsProblemElevation { get; init; }

    /// <summary>Pulsante di salvataggio della posizione.</summary>
    public required string SettingsSiteSave { get; init; }

    /// <summary>Conferma di posizione salvata.</summary>
    public required string SettingsSiteSaved { get; init; }

    /// <summary>Salvataggio richiesto senza modifiche.</summary>
    public required string SettingsNoChanges { get; init; }

    /// <summary>Salvataggio della configurazione non riuscito.</summary>
    public required string SettingsSaveError { get; init; }

    /// <summary>Alcuni campi della configurazione non sono validi.</summary>
    public required string SettingsInvalidValues { get; init; }

    /// <summary>Sezione generali (lingua e tema).</summary>
    public required string SettingsGeneralTitle { get; init; }

    /// <summary>Etichetta della lingua.</summary>
    public required string SettingsLanguageLabel { get; init; }

    /// <summary>Avviso: il cambio di lingua richiede un riavvio.</summary>
    public required string SettingsLanguageRestart { get; init; }

    /// <summary>Conferma di lingua salvata.</summary>
    public required string SettingsLanguageSaved { get; init; }

    /// <summary>Etichetta del tema.</summary>
    public required string SettingsThemeLabel { get; init; }

    /// <summary>Tema normale.</summary>
    public required string SettingsThemeNormal { get; init; }

    /// <summary>Tema notte rossa.</summary>
    public required string SettingsThemeNightRed { get; init; }

    /// <summary>Spiegazione della notte rossa.</summary>
    public required string SettingsThemeHint { get; init; }

    /// <summary>Sezione cartella dati.</summary>
    public required string SettingsDataFolderTitle { get; init; }

    /// <summary>Percorso della cartella dati; {0} è il percorso.</summary>
    public required string SettingsDataFolderPath { get; init; }

    /// <summary>Cartella dati in sola lettura.</summary>
    public required string SettingsDataFolderReadOnly { get; init; }

    /// <summary>Pulsante "Apri cartella".</summary>
    public required string SettingsOpenFolder { get; init; }

    /// <summary>Apertura della cartella non riuscita; {0} è il percorso.</summary>
    public required string SettingsOpenFolderError { get; init; }

    /// <summary>Sezione avvio.</summary>
    public required string SettingsStartupTitle { get; init; }

    /// <summary>Casella "Avvia con Windows".</summary>
    public required string SettingsStartWithWindows { get; init; }

    /// <summary>Spiegazione dell'avvio con Windows.</summary>
    public required string SettingsStartWithWindowsHint { get; init; }

    /// <summary>Modifica dell'avvio con Windows non riuscita; {0} è il motivo.</summary>
    public required string SettingsAutostartError { get; init; }

    /// <summary>Sezione aggiornamenti.</summary>
    public required string SettingsUpdatesTitle { get; init; }

    /// <summary>Versione installata; {0} è la versione.</summary>
    public required string SettingsCurrentVersion { get; init; }

    /// <summary>Casella del controllo automatico degli aggiornamenti.</summary>
    public required string SettingsAutoCheck { get; init; }

    /// <summary>Spiegazione del controllo automatico.</summary>
    public required string SettingsAutoCheckHint { get; init; }

    /// <summary>Pulsante "Controlla ora".</summary>
    public required string SettingsCheckNow { get; init; }

    /// <summary>Controllo degli aggiornamenti in corso.</summary>
    public required string SettingsChecking { get; init; }

    /// <summary>Nessun aggiornamento disponibile.</summary>
    public required string SettingsUpToDate { get; init; }

    /// <summary>Versione disponibile; {0} è la versione.</summary>
    public required string SettingsUpdateAvailable { get; init; }

    /// <summary>Aggiornamenti non disponibili per questa copia.</summary>
    public required string SettingsUpdateDisabled { get; init; }

    /// <summary>Errore di rete durante il controllo.</summary>
    public required string SettingsUpdateNetworkError { get; init; }

    /// <summary>Errore generico durante il controllo.</summary>
    public required string SettingsUpdateGenericError { get; init; }

    /// <summary>Data dell'ultimo controllo; {0} è la data locale.</summary>
    public required string SettingsLastCheck { get; init; }

    /// <summary>Nessun controllo ancora eseguito.</summary>
    public required string SettingsNeverChecked { get; init; }

    /// <summary>Sezione accesso dal browser.</summary>
    public required string SettingsAccessTitle { get; init; }

    /// <summary>Casella per abilitare l'interfaccia web.</summary>
    public required string SettingsWebEnabled { get; init; }

    /// <summary>Spiegazione dell'interfaccia web.</summary>
    public required string SettingsWebHint { get; init; }

    /// <summary>Modifica dell'interfaccia web valida al riavvio.</summary>
    public required string SettingsWebRestart { get; init; }

    /// <summary>Interfaccia web attiva; {0} è l'indirizzo.</summary>
    public required string SettingsWebRunning { get; init; }

    /// <summary>Interfaccia web non attiva in questa sessione.</summary>
    public required string SettingsWebNotRunning { get; init; }

    /// <summary>Casella per l'accesso dalla rete locale.</summary>
    public required string SettingsLanEnabled { get; init; }

    /// <summary>Spiegazione dell'accesso dalla rete locale.</summary>
    public required string SettingsLanHint { get; init; }

    /// <summary>Campo della password per la rete locale.</summary>
    public required string SettingsLanPasswordLabel { get; init; }

    /// <summary>Spiegazione della password per la rete locale.</summary>
    public required string SettingsLanPasswordHint { get; init; }

    /// <summary>Password per la rete locale impostata.</summary>
    public required string SettingsLanPasswordSet { get; init; }

    /// <summary>Nessuna password per la rete locale.</summary>
    public required string SettingsLanPasswordNotSet { get; init; }

    /// <summary>Password troppo corta.</summary>
    public required string SettingsPasswordTooShort { get; init; }

    /// <summary>Accesso dalla rete locale senza password.</summary>
    public required string SettingsPasswordRequired { get; init; }

    /// <summary>Pulsante "Salva accesso".</summary>
    public required string SettingsAccessSave { get; init; }

    /// <summary>Accesso salvato: serve il riavvio.</summary>
    public required string SettingsAccessSaved { get; init; }

    /// <summary>Salvataggio in corso.</summary>
    public required string SettingsSaving { get; init; }

    /// <summary>Impostazioni salvate.</summary>
    public required string SettingsSaved { get; init; }

    /// <summary>Titolo della vista Log.</summary>
    public required string LogViewTitle { get; init; }

    /// <summary>Etichetta del filtro per gravità.</summary>
    public required string LogViewLevelLabel { get; init; }

    /// <summary>Filtro: tutte le voci.</summary>
    public required string LogViewLevelAll { get; init; }

    /// <summary>Filtro: avvisi ed errori.</summary>
    public required string LogViewLevelWarnings { get; init; }

    /// <summary>Filtro: solo errori.</summary>
    public required string LogViewLevelErrors { get; init; }

    /// <summary>Etichetta della ricerca nel testo.</summary>
    public required string LogViewSearchLabel { get; init; }

    /// <summary>Casella "Scorri in fondo".</summary>
    public required string LogViewAutoScroll { get; init; }

    /// <summary>Pulsante "Aggiorna".</summary>
    public required string LogViewRefresh { get; init; }

    /// <summary>Pulsante "Copia".</summary>
    public required string LogViewCopy { get; init; }

    /// <summary>Pulsante "Apri file".</summary>
    public required string LogViewOpenFile { get; init; }

    /// <summary>Nessuna voce con il filtro scelto.</summary>
    public required string LogViewEmpty { get; init; }

    /// <summary>Nessun file di log ancora presente.</summary>
    public required string LogViewNoFile { get; init; }

    /// <summary>Testo copiato negli appunti.</summary>
    public required string LogViewCopied { get; init; }

    /// <summary>Apertura del file di log non riuscita; {0} è il percorso.</summary>
    public required string LogViewOpenError { get; init; }

    /// <summary>Lettura del log non riuscita.</summary>
    public required string LogViewReadError { get; init; }

    /// <summary>Nota sulla mascheratura dei segreti.</summary>
    public required string LogViewSecretsNote { get; init; }

    /// <summary>Numero di voci mostrate; {0} è il numero.</summary>
    public required string LogViewCount { get; init; }

    /// <summary>Nome del file di log; {0} è il nome.</summary>
    public required string LogViewFile { get; init; }

    /// <summary>Etichetta di gravità: informazione.</summary>
    public required string LogViewTagInfo { get; init; }

    /// <summary>Etichetta di gravità: avviso.</summary>
    public required string LogViewTagWarning { get; init; }

    /// <summary>Etichetta di gravità: errore.</summary>
    public required string LogViewTagError { get; init; }

    /// <summary>Stato mentre la camera si scollega.</summary>
    public required string CameraDiscBusy { get; init; }

    /// <summary>Conferma dopo la disconnessione.</summary>
    public required string CameraDiscDone { get; init; }

    /// <summary>Titolo della sezione impostazioni Uplink nella vista Pubblicazione.</summary>
    public required string PublicationSettingsTitle { get; init; }

    /// <summary>Interruttore "pubblica sul server".</summary>
    public required string PublicationEnabled { get; init; }

    /// <summary>Etichetta dell'indirizzo del server.</summary>
    public required string PublicationServerUrl { get; init; }

    /// <summary>Suggerimento sotto l'indirizzo del server.</summary>
    public required string PublicationServerUrlHint { get; init; }

    /// <summary>Etichetta dell'identificativo del dispositivo.</summary>
    public required string PublicationDeviceId { get; init; }

    /// <summary>Suggerimento sotto l'identificativo del dispositivo.</summary>
    public required string PublicationDeviceIdHint { get; init; }

    /// <summary>Etichetta del nome del profilo.</summary>
    public required string PublicationProfileName { get; init; }

    /// <summary>Etichetta dell'intervallo tra gli invii.</summary>
    public required string PublicationInterval { get; init; }

    /// <summary>Intervallo di valori ammessi: {0} minimo, {1} massimo, {2} predefinito.</summary>
    public required string PublicationRangeHint { get; init; }

    /// <summary>Etichetta del lato lungo dell'immagine.</summary>
    public required string PublicationLongEdge { get; init; }

    /// <summary>Etichetta della qualità JPEG.</summary>
    public required string PublicationJpegQuality { get; init; }

    /// <summary>Etichetta del token.</summary>
    public required string PublicationTokenLabel { get; init; }

    /// <summary>Riga dello stato di un segreto: {0} = impostato o non impostato.</summary>
    public required string PublicationSecretStatus { get; init; }

    /// <summary>Stato di un segreto: impostato.</summary>
    public required string PublicationSecretSet { get; init; }

    /// <summary>Stato di un segreto: non impostato.</summary>
    public required string PublicationSecretNotSet { get; init; }

    /// <summary>Etichetta del campo per sostituire un segreto (il valore attuale non viene mai riletto).</summary>
    public required string PublicationSecretReplace { get; init; }

    /// <summary>Spiegazione della gestione dei segreti (cifratura, nessuna visualizzazione).</summary>
    public required string PublicationSecretHint { get; init; }

    /// <summary>Interruttore per rimuovere il token salvato.</summary>
    public required string PublicationClearToken { get; init; }

    /// <summary>Etichetta del nome dell'header HTTP aggiuntivo.</summary>
    public required string PublicationExtraHeaderName { get; init; }

    /// <summary>Etichetta del valore dell'header HTTP aggiuntivo.</summary>
    public required string PublicationExtraHeaderValue { get; init; }

    /// <summary>Interruttore per rimuovere l'header HTTP aggiuntivo salvato.</summary>
    public required string PublicationClearExtraHeader { get; init; }

    /// <summary>Pulsante "Salva impostazioni".</summary>
    public required string PublicationSave { get; init; }

    /// <summary>Stato mentre le impostazioni si salvano.</summary>
    public required string PublicationSaving { get; init; }

    /// <summary>Conferma di salvataggio riuscito.</summary>
    public required string PublicationSaved { get; init; }

    /// <summary>Messaggio quando il salvataggio non cambierebbe nulla.</summary>
    public required string PublicationNoChanges { get; init; }

    /// <summary>Messaggio quando il salvataggio viene rifiutato per errori di campo.</summary>
    public required string PublicationSaveFailed { get; init; }

    /// <summary>Riga di un errore di campo: {0} = nome del campo, {1} = messaggio.</summary>
    public required string PublicationIssueLine { get; init; }

    /// <summary>Nome generico per un errore che non riguarda un campo preciso.</summary>
    public required string PublicationFieldGeneral { get; init; }

    /// <summary>Errore quando il modulo di pubblicazione non è partito.</summary>
    public required string PublicationUnavailable { get; init; }

    /// <summary>Problema di campo: campo vuoto.</summary>
    public required string PublicationProblemEmpty { get; init; }

    /// <summary>Problema di campo: il testo non è un numero intero.</summary>
    public required string PublicationProblemNotInteger { get; init; }

    /// <summary>Problema di campo: fuori dall'intervallo. {0} minimo, {1} massimo.</summary>
    public required string PublicationProblemOutOfRange { get; init; }

    /// <summary>Pulsante "Prova connessione".</summary>
    public required string PublicationTest { get; init; }

    /// <summary>Stato mentre la prova di connessione è in corso.</summary>
    public required string PublicationTesting { get; init; }

    /// <summary>Nota sotto il pulsante di prova: usa i valori del modulo, non invia immagini.</summary>
    public required string PublicationTestNote { get; init; }

    /// <summary>Esito positivo della prova di connessione.</summary>
    public required string PublicationTestOk { get; init; }

    /// <summary>Esito negativo della prova di connessione: {0} = motivo.</summary>
    public required string PublicationTestFailed { get; init; }

    /// <summary>Titolo della sezione stato della pubblicazione.</summary>
    public required string PublicationStatusTitle { get; init; }

    /// <summary>Stato della pubblicazione non ancora noto.</summary>
    public required string PublicationStateUnknown { get; init; }

    /// <summary>Stato "disabled": pubblicazione spenta.</summary>
    public required string PublicationStateDisabled { get; init; }

    /// <summary>Suggerimento quando la pubblicazione è spenta.</summary>
    public required string PublicationStateDisabledHint { get; init; }

    /// <summary>Stato "idle": attiva, in attesa del prossimo invio.</summary>
    public required string PublicationStateIdle { get; init; }

    /// <summary>Stato "sending": invio in corso.</summary>
    public required string PublicationStateSending { get; init; }

    /// <summary>Stato "backoff": errore temporaneo, nuovo tentativo programmato.</summary>
    public required string PublicationStateBackoff { get; init; }

    /// <summary>Stato "suspended": invio sospeso per credenziali rifiutate.</summary>
    public required string PublicationStateSuspended { get; init; }

    /// <summary>Contatori della coda: {0} in coda, {1} inviate, {2} scartate.</summary>
    public required string PublicationCounters { get; init; }

    /// <summary>Nessun invio riuscito finora.</summary>
    public required string PublicationNoSuccessYet { get; init; }

    /// <summary>Ultimo invio riuscito: {0} = orario.</summary>
    public required string PublicationLastSuccess { get; init; }

    /// <summary>Prossimo tentativo: {0} = orario.</summary>
    public required string PublicationNextAttempt { get; init; }

    /// <summary>Ultimo errore: {0} = orario, {1} = motivo.</summary>
    public required string PublicationLastErrorLine { get; init; }

    /// <summary>Motivo: credenziali rifiutate (auth).</summary>
    public required string PublicationReasonAuth { get; init; }

    /// <summary>Motivo: immagine rifiutata dal server (rejected).</summary>
    public required string PublicationReasonRejected { get; init; }

    /// <summary>Motivo: immagine troppo grande (too_large).</summary>
    public required string PublicationReasonTooLarge { get; init; }

    /// <summary>Motivo: risposta del server illeggibile (bad_response).</summary>
    public required string PublicationReasonBadResponse { get; init; }

    /// <summary>Motivo: errore del server con codice HTTP. {0} = codice.</summary>
    public required string PublicationReasonServerCode { get; init; }

    /// <summary>Motivo: errore temporaneo del server senza codice.</summary>
    public required string PublicationReasonServer { get; init; }

    /// <summary>Motivo: il server chiede di rallentare (rate_limited).</summary>
    public required string PublicationReasonRateLimited { get; init; }

    /// <summary>Motivo: rete non raggiungibile (network).</summary>
    public required string PublicationReasonNetwork { get; init; }

    /// <summary>Motivo: nessuna risposta in tempo (timeout).</summary>
    public required string PublicationReasonTimeout { get; init; }

    /// <summary>Motivo: impostazioni non valide (invalid_settings).</summary>
    public required string PublicationReasonInvalidSettings { get; init; }

    /// <summary>Motivo: il server non espone la pagina di verifica (no_health_endpoint).</summary>
    public required string PublicationReasonNoHealth { get; init; }

    /// <summary>Motivo: errore non classificato.</summary>
    public required string PublicationReasonOther { get; init; }

    /// <summary>Titolo della zona YouTube (segnaposto fino a P7.4).</summary>
    public required string PublicationYouTubeTitle { get; init; }

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
        NightsFramesCount = "{0} frame",
        NightsTimelapseNone = "Timelapse non creato",
        NightsTimelapseReady = "Timelapse pronto",
        NightsTimelapseGenerating = "In lavorazione",
        NightsTimelapseFailed = "Timelapse non riuscito",
        NightsTimelapseUnknown = "Stato sconosciuto",
        NightsTimelapseQueued = "In coda o in avvio…",
        NightsTimelapseProgress = "In lavorazione: {0}%",
        NightsFramePosition = "Frame {0} di {1}",
        NightsFrameTimeTooltip = "Istante UTC: {0}",
        NightsListName = "Elenco delle notti",
        NightsLoading = "Lettura dell'archivio in corso…",
        NightsLoadError = "Impossibile leggere l'archivio delle notti: {0}. Controllare la cartella dati e riprovare.",
        NightsSelectNight = "Selezionare una notte dall'elenco per vederne keogram, startrails e timelapse.",
        NightsTimelapseTitle = "Timelapse",
        NightsTimelapsePlay = "Riproduci",
        NightsTimelapsePause = "Pausa",
        NightsTimelapseStop = "Stop",
        NightsTimelapseRegenerate = "Rigenera timelapse",
        NightsTimelapseRegenerateTooltip = "Ricrea il video dai frame archiviati. Il file attuale viene sostituito al termine.",
        NightsTimelapseRefused = "Impossibile accodare il timelapse: la notte non ha frame oppure è già in coda.",
        NightsTimelapseRequested = "Timelapse accodato. Comparirà qui quando sarà pronto.",
        NightsTimelapseRegenerateError = "Impossibile avviare la rigenerazione: {0}",
        NightsTimelapseMissingFile = "Il file del timelapse non è presente. Rigenerarlo per crearlo di nuovo.",
        NightsPlaybackError = "Impossibile riprodurre il timelapse. Verificare che sia installato il codec H.264 (Estensioni video di Windows), poi riprovare.",
        NightsPlayerName = "Riproduzione del timelapse",
        NightsKeogramTitle = "Keogram",
        NightsKeogramName = "Keogram della notte, barra di scorrimento temporale",
        NightsKeogramHint = "Clic o trascinamento sul keogram per vedere il frame di quel momento. Con il keogram selezionato, le frecce ← e → spostano il frame di uno alla volta.",
        NightsKeogramMissing = "Keogram non disponibile per questa notte.",
        NightsStartrailsTitle = "Startrails",
        NightsStartrailsName = "Startrails della notte",
        NightsStartrailsMissing = "Startrails non disponibile per questa notte.",
        NightsFrameTitle = "Frame selezionato",
        NightsFrameName = "Frame selezionato della notte",
        NightsFrameMissing = "Il file di questo frame non è più disponibile.",
        NightsNoFrames = "Nessun frame archiviato in questa notte.",
        IllusNoNightTitle = "Nessuna notte ancora",
        IllusNoNightBody = "Le immagini compariranno qui dopo il tramonto, quando inizia la cattura notturna.",
        IllusNoEventsTitle = "Nessun evento",
        IllusNoEventsBody = "Meteore, bolidi e transiti rilevati compariranno qui. Per ora non ce n'è nessuno.",
        IllusCameraTitle = "Camera non collegata",
        IllusCameraBody = "Collega una camera dalla vista Camera per vedere le immagini dal vivo.",
        IllusYouTubeTitle = "YouTube non collegato",
        IllusYouTubeBody = "Collega un account YouTube dalla vista Pubblicazione per caricare il timelapse in automatico.",
        TonightNav = "Stanotte",
        TonightTitle = "Stanotte",
        TonightImageName = "Immagine del cielo di questa notte",
        TonightNoImage = "Nessuna immagine ancora. Il primo frame comparirà qui.",
        TonightComputing = "Calcolo in corso…",
        TonightLoadError = "Impossibile calcolare la notte: {0}",
        TonightSiteInvalid = "Posizione del sito non valida: controllare latitudine e longitudine nelle impostazioni.",
        TonightTimelineTitle = "Linea temporale della notte",
        TonightTimelineName = "Linea temporale da tramonto ad alba",
        TonightSunset = "Tramonto",
        TonightSunrise = "Alba",
        TonightNoWindow = "Questa notte non ha tramonto né alba: il Sole resta sempre sopra o sempre sotto l'orizzonte.",
        TonightNowMarker = "Adesso: {0}",
        TonightNotInWindow = "Adesso siamo fuori dalla finestra tra tramonto e alba.",
        TonightGeometricNote = "Altezze geometriche, senza rifrazione atmosferica.",
        TonightSunCurve = "Sole",
        TonightMoonCurve = "Luna",
        TonightHorizon = "Orizzonte",
        TonightCurveName = "Altezza di Sole e Luna durante la notte",
        TonightSegmentLine = "{0}: da {1} a {2}",
        TonightPhaseDay = "Giorno",
        TonightPhaseCivil = "Crepuscolo civile",
        TonightPhaseNautical = "Crepuscolo nautico",
        TonightPhaseAstro = "Crepuscolo astronomico",
        TonightPhaseNight = "Notte astronomica",
        TonightConditionsTitle = "Condizioni",
        TonightMoonGlyphName = "Fase lunare: {0}, illuminata al {1}",
        TonightMoonNew = "Luna nuova",
        TonightMoonWaxingCrescent = "Falce crescente",
        TonightMoonFirstQuarter = "Primo quarto",
        TonightMoonWaxingGibbous = "Gibbosa crescente",
        TonightMoonFull = "Luna piena",
        TonightMoonWaningGibbous = "Gibbosa calante",
        TonightMoonLastQuarter = "Ultimo quarto",
        TonightMoonWaningCrescent = "Falce calante",
        TonightMoonAltitude = "Altezza Luna",
        TonightSunAltitude = "Altezza Sole",
        TonightSkyClarity = "Indice di cielo sereno",
        TonightSkyClarityPending = "Non disponibile: arriverà con il rilevamento delle stelle.",
        TonightWeather = "Meteo",
        TonightWeatherPending = "Non disponibile: arriverà con la sorgente meteo.",
        TonightEventsTitle = "Prossimi eventi",
        TonightNextChange = "Prossimo cambio di fase",
        TonightNextTransit = "Prossimo transito",
        TonightNoTransit = "Nessun transito previsto",
        TonightTransitPending = "Il calcolo dei transiti non è ancora attivo.",
        TonightLastMeteor = "Ultima meteora",
        TonightNoMeteor = "Nessuna meteora registrata",
        TonightMeteorPending = "Il rilevamento delle meteore non è ancora attivo.",
        TonightCaptureTitle = "Stato della cattura",
        TonightCountdownIn = "tra {0}",
        TonightCountdownMinutes = "{0} min",
        TonightCountdownHoursMinutes = "{0} h {1} min",
        TonightCountdownLessThanMinute = "meno di 1 min",
        TonightNow = "adesso",
        TonightAgo = "{0} fa",
        CameraDiscButton = "Scollega",
        SettingsTitle = "Impostazioni",
        SettingsObservatoryTitle = "Osservatorio",
        SettingsLatitude = "Latitudine (°)",
        SettingsLongitude = "Longitudine (°)",
        SettingsElevation = "Quota (m)",
        SettingsSiteHint = "Latitudine da -90 a 90, longitudine da -180 a 180 (positiva verso est), quota da -500 a 9000 metri.",
        SettingsProblemEmpty = "Inserire un valore.",
        SettingsProblemNotNumber = "Inserire un numero.",
        SettingsProblemLatitude = "La latitudine deve essere tra -90 e 90.",
        SettingsProblemLongitude = "La longitudine deve essere tra -180 e 180.",
        SettingsProblemElevation = "La quota deve essere tra -500 e 9000 metri.",
        SettingsSiteSave = "Salva posizione",
        SettingsSiteSaved = "Posizione salvata. Il cielo e le fasi usano i nuovi valori.",
        SettingsNoChanges = "Nessuna modifica da salvare.",
        SettingsSaveError = "Impossibile salvare le impostazioni. Controllare i permessi della cartella dati e riprovare.",
        SettingsInvalidValues = "Alcuni valori non sono validi: correggerli e salvare di nuovo.",
        SettingsGeneralTitle = "Generali",
        SettingsLanguageLabel = "Lingua",
        SettingsLanguageRestart = "Il cambio di lingua si applica al prossimo avvio di All Sky Brain.",
        SettingsLanguageSaved = "Lingua salvata. Riavviare l'app per applicarla.",
        SettingsThemeLabel = "Tema",
        SettingsThemeNormal = "Normale",
        SettingsThemeNightRed = "Notte rossa",
        SettingsThemeHint = "La notte rossa non usa blu né verde, per non disturbare l'adattamento al buio. Si attiva anche con Ctrl+R.",
        SettingsDataFolderTitle = "Cartella dati",
        SettingsDataFolderPath = "Percorso: {0}",
        SettingsDataFolderReadOnly = "Sola lettura. La cartella si sceglie all'avvio con --data-folder.",
        SettingsOpenFolder = "Apri cartella",
        SettingsOpenFolderError = "Impossibile aprire la cartella. Percorso: {0}",
        SettingsStartupTitle = "Avvio",
        SettingsStartWithWindows = "Avvia All Sky Brain con Windows",
        SettingsStartWithWindowsHint = "Registra l'app nell'avvio dell'utente corrente. Con --data-folder l'avvio automatico usa la cartella predefinita.",
        SettingsAutostartError = "Impossibile cambiare l'avvio con Windows: {0}",
        SettingsUpdatesTitle = "Aggiornamenti",
        SettingsCurrentVersion = "Versione installata: {0}",
        SettingsAutoCheck = "Controlla e scarica gli aggiornamenti automaticamente",
        SettingsAutoCheckHint = "Il primo controllo parte un minuto dopo l'avvio, poi ogni 24 ore. L'installazione richiede sempre una tua conferma.",
        SettingsCheckNow = "Controlla ora",
        SettingsChecking = "Controllo in corso…",
        SettingsUpToDate = "Hai già l'ultima versione.",
        SettingsUpdateAvailable = "È disponibile la versione {0}. Usa «Controlla aggiornamenti» dall'icona nell'area di notifica per scaricarla.",
        SettingsUpdateDisabled = "Gli aggiornamenti sono disponibili solo per l'installazione, non per la copia portatile o di sviluppo.",
        SettingsUpdateNetworkError = "Server degli aggiornamenti non raggiungibile. Controllare la connessione e riprovare.",
        SettingsUpdateGenericError = "Controllo non riuscito. Riprovare più tardi.",
        SettingsLastCheck = "Ultimo controllo: {0}",
        SettingsNeverChecked = "Mai controllato.",
        SettingsAccessTitle = "Accesso dal browser",
        SettingsWebEnabled = "Abilita l'interfaccia web",
        SettingsWebHint = "Avvia un piccolo server locale per il pannello nel browser. È spento per impostazione predefinita: l'app desktop non ne ha bisogno.",
        SettingsWebRestart = "La modifica si applica al prossimo avvio di All Sky Brain.",
        SettingsWebRunning = "In esecuzione su {0}.",
        SettingsWebNotRunning = "Non attiva in questa sessione.",
        SettingsLanEnabled = "Consenti l'accesso dalla rete locale",
        SettingsLanHint = "Ascolta su tutte le interfacce e chiede una password. Il traffico non è cifrato: usarlo solo su reti di fiducia.",
        SettingsLanPasswordLabel = "Password per la rete locale",
        SettingsLanPasswordHint = "Almeno 8 caratteri. Non viene mai mostrata: si può solo impostarne una nuova.",
        SettingsLanPasswordSet = "Password impostata.",
        SettingsLanPasswordNotSet = "Nessuna password impostata.",
        SettingsPasswordTooShort = "Almeno 8 caratteri.",
        SettingsPasswordRequired = "Per la rete locale serve una password.",
        SettingsAccessSave = "Salva accesso",
        SettingsAccessSaved = "Impostazioni di accesso salvate. Riavviare l'app per applicarle.",
        SettingsSaving = "Salvataggio…",
        SettingsSaved = "Impostazioni salvate.",
        LogViewTitle = "Log",
        LogViewLevelLabel = "Livello",
        LogViewLevelAll = "Tutti",
        LogViewLevelWarnings = "Avvisi ed errori",
        LogViewLevelErrors = "Solo errori",
        LogViewSearchLabel = "Cerca nel testo",
        LogViewAutoScroll = "Scorri in fondo",
        LogViewRefresh = "Aggiorna",
        LogViewCopy = "Copia",
        LogViewOpenFile = "Apri file",
        LogViewEmpty = "Nessuna voce corrisponde al filtro.",
        LogViewNoFile = "Nessun file di log. Compare al primo avvio di All Sky Brain.",
        LogViewCopied = "Testo copiato negli appunti.",
        LogViewOpenError = "Impossibile aprire il file di log. File: {0}",
        LogViewReadError = "Impossibile leggere il log. Riprovare tra poco.",
        LogViewSecretsNote = "Token, password e client secret sono mascherati.",
        LogViewCount = "{0} voci",
        LogViewFile = "File: {0}",
        LogViewTagInfo = "Info",
        LogViewTagWarning = "Avviso",
        LogViewTagError = "Errore",
        CameraDiscBusy = "Scollegamento in corso…",
        CameraDiscDone = "Camera scollegata.",
        PublicationSettingsTitle = "Impostazioni Uplink",
        PublicationEnabled = "Pubblica sul server",
        PublicationServerUrl = "Indirizzo del server",
        PublicationServerUrlHint = "Solo l'indirizzo base, per esempio https://nome-server. Con http sono ammessi solo localhost e 127.0.0.1.",
        PublicationDeviceId = "Identificativo del dispositivo",
        PublicationDeviceIdHint = "Lettere, numeri, punto, trattino o underscore. Massimo 64 caratteri.",
        PublicationProfileName = "Nome del profilo (facoltativo)",
        PublicationInterval = "Intervallo tra gli invii, in secondi",
        PublicationRangeHint = "Da {0} a {1}. Predefinito: {2}.",
        PublicationLongEdge = "Lato lungo dell'immagine, in pixel",
        PublicationJpegQuality = "Qualità JPEG",
        PublicationTokenLabel = "Token",
        PublicationSecretStatus = "Stato: {0}",
        PublicationSecretSet = "impostato",
        PublicationSecretNotSet = "non impostato",
        PublicationSecretReplace = "Sostituisci con un nuovo valore (lascia vuoto per non cambiarlo)",
        PublicationSecretHint = "Il valore viene cifrato su disco e non viene mai mostrato, nemmeno in parte.",
        PublicationClearToken = "Rimuovi il token salvato",
        PublicationExtraHeaderName = "Header HTTP aggiuntivo: nome (facoltativo)",
        PublicationExtraHeaderValue = "Header HTTP aggiuntivo: valore",
        PublicationClearExtraHeader = "Rimuovi l'header aggiuntivo salvato",
        PublicationSave = "Salva impostazioni",
        PublicationSaving = "Salvataggio in corso…",
        PublicationSaved = "Impostazioni salvate.",
        PublicationNoChanges = "Nessuna modifica da salvare.",
        PublicationSaveFailed = "Impostazioni non salvate. Correggi i campi indicati.",
        PublicationIssueLine = "{0}: {1}",
        PublicationFieldGeneral = "Pubblicazione",
        PublicationUnavailable = "La pubblicazione non è disponibile: il modulo non è partito. Controllare il log dell'applicazione.",
        PublicationProblemEmpty = "Campo obbligatorio.",
        PublicationProblemNotInteger = "Inserire un numero intero.",
        PublicationProblemOutOfRange = "Il valore deve essere compreso tra {0} e {1}.",
        PublicationTest = "Prova connessione",
        PublicationTesting = "Prova in corso…",
        PublicationTestNote = "La prova usa i valori del modulo, anche se non ancora salvati. Non invia immagini.",
        PublicationTestOk = "Connessione riuscita: il server accetta token e identificativo.",
        PublicationTestFailed = "Prova non riuscita: {0}",
        PublicationStatusTitle = "Stato",
        PublicationStateUnknown = "Stato non disponibile",
        PublicationStateDisabled = "Spenta",
        PublicationStateDisabledHint = "Attiva «Pubblica sul server», inserisci indirizzo, identificativo e token, poi salva.",
        PublicationStateIdle = "Attiva, in attesa del prossimo invio",
        PublicationStateSending = "Invio in corso",
        PublicationStateBackoff = "Errore temporaneo: nuovo tentativo programmato",
        PublicationStateSuspended = "Sospesa: credenziali rifiutate. Salva un token nuovo per riprendere.",
        PublicationCounters = "In coda: {0} · Inviate: {1} · Scartate: {2}",
        PublicationNoSuccessYet = "Nessun invio riuscito finora",
        PublicationLastSuccess = "Ultimo invio riuscito: {0}",
        PublicationNextAttempt = "Prossimo tentativo: {0}",
        PublicationLastErrorLine = "Ultimo errore alle {0}: {1}",
        PublicationReasonAuth = "Il server ha rifiutato token o identificativo.",
        PublicationReasonRejected = "Il server ha rifiutato l'immagine: scartata, non verrà ritentata.",
        PublicationReasonTooLarge = "Immagine troppo grande per il server: scartata.",
        PublicationReasonBadResponse = "Risposta del server non valida: nuovo tentativo più tardi.",
        PublicationReasonServerCode = "Errore del server (codice {0}).",
        PublicationReasonServer = "Errore temporaneo del server.",
        PublicationReasonRateLimited = "Il server chiede di rallentare: nuovo tentativo dopo l'attesa indicata.",
        PublicationReasonNetwork = "Server non raggiungibile: controllare indirizzo e rete.",
        PublicationReasonTimeout = "Il server non ha risposto in tempo.",
        PublicationReasonInvalidSettings = "Indirizzo o impostazioni non validi: controllare il modulo.",
        PublicationReasonNoHealth = "Il server non ha la pagina di verifica: controllare l'indirizzo.",
        PublicationReasonOther = "Errore non classificato: vedere il log della pubblicazione.",
        PublicationYouTubeTitle = "YouTube",
        OvlSun = "Sole",
        OvlMoon = "Luna",
        OvlPhase = "fase",
        OvlSatellites = "Satelliti",
        OvlNone = "nessuno",
        OvlMissing = "n/d",
        OvlExposure = "esp",
        OvlGain = "gain",
        CalibTitle = "Calibrazione ottica",
        CalibStatusCollecting = "Punti dal Sole raccolti: {0} su {1} necessari.",
        CalibStatusCollectingHint = "Il Sole si cerca di giorno, quando è sopra i 10°. Servono punti in direzioni diverse del cielo.",
        CalibFailureReason = "Ultimo tentativo: {0}",
        CalibLastPoint = "Ultimo punto: {0}",
        CalibStatusReview = "Proposta pronta: controllare la griglia sul frame, poi accettare o rifiutare.",
        CalibStatusReviewManual = "Valori manuali in attesa: accettare per salvarli.",
        CalibStatusReviewNotAcceptable = "Errore di {0} px, sopra la soglia dell'{1}% del raggio: la proposta non si può accettare. Raccogliere altri punti o usare la modalità manuale.",
        CalibStatusCalibrated = "Calibrazione attiva: errore {0} px, {1}, del {2}.",
        CalibSourceSun = "stimata dal Sole",
        CalibSourceManual = "inserita a mano",
        CalibResidualSummary = "Errore medio {0} px su {1} punti; {2} esclusi come outlier.",
        CalibFrameTitle = "Anteprima",
        CalibFrameEmpty = "Nessun frame ancora: la griglia compare al primo frame.",
        CalibGridLegend = "Griglia alt/az: cerchi a 0°, 30° e 60°, meridiani ogni 30°. N, E, S, O sull'orizzonte.",
        CalibObservedName = "Punti del Sole osservati",
        CalibAccept = "Accetta",
        CalibReject = "Rifiuta",
        CalibAcceptedNotice = "Calibrazione salvata e applicata.",
        CalibRejectedNotice = "Proposta scartata: la raccolta continua.",
        CalibPointsTitle = "Punti raccolti",
        CalibNoPoints = "Nessun punto ancora.",
        CalibManualTitle = "Modalità manuale",
        CalibManualHint = "I valori aggiornano l'anteprima subito. Il raggio è quello dell'orizzonte, in pixel del frame.",
        CalibManualCenterX = "Centro X (px)",
        CalibManualCenterY = "Centro Y (px)",
        CalibManualRadius = "Raggio orizzonte (px)",
        CalibManualK2 = "Correzione radiale K2",
        CalibManualRotation = "Rotazione del Nord (°)",
        CalibManualMirrored = "Est a destra (specchio)",
        CalibManualPreviewOn = "Anteprima manuale: non ancora salvata.",
        CalibManualUse = "Usa come proposta",
        CalibManualReset = "Valori del frame",
        CalibManualNoFrame = "Nessun frame: la modalità manuale richiede almeno un frame.",
        CalibManualInvalid = "Correggere i campi evidenziati.",
        CalibProblemEmpty = "Inserire un valore.",
        CalibProblemNotNumber = "Inserire un numero.",
        CalibProblemNotPositive = "Il raggio deve essere maggiore di zero.",
        CalibProblemOutOfRange = "K2 deve essere maggiore di -0,33.",
        CalibActionNoProposal = "Non c'è nessuna proposta da accettare.",
        CalibActionNotAcceptable = "La proposta non rispetta la soglia di precisione.",
        CalibActionNoFrame = "Nessun frame ricevuto: impossibile impostare la modalità manuale.",
        CalibActionSaveFailed = "Salvataggio non riuscito. Controllare i permessi della cartella dati e riprovare.",
        CalibCardinalNorth = "N",
        CalibCardinalEast = "E",
        CalibCardinalSouth = "S",
        CalibCardinalWest = "O",
        PassListTitle = "Prossimi passaggi",
        PassStatusDisabled = "Previsioni satelliti disattivate. Si attivano con satellites.enabled nel file di configurazione.",
        PassStatusWaiting = "In attesa dei dati orbitali (CelesTrak).",
        PassStatusBlocked = "richieste a CelesTrak sospese: {0}",
        PassStatusFailed = "dati orbitali non disponibili: {0}",
        PassNoneInWindow = "Nessun passaggio nella finestra di previsione.",
        PassInProgress = "in corso",
        PassTimes = "inizio {0} · culmine {1} · fine {2}",
        PassMaxElevation = "alt. max {0}",
        PassDirection = "da {0} a {1}",
        PassCompassN = "N",
        PassCompassNE = "NE",
        PassCompassE = "E",
        PassCompassSE = "SE",
        PassCompassS = "S",
        PassCompassSW = "SO",
        PassCompassW = "O",
        PassCompassNW = "NO",
        PassLightVisible = "visibile a occhio nudo",
        PassLightLit = "illuminato, cielo non buio",
        PassLightShadow = "in ombra terrestre",
        PassTrackServiceOff = "previsioni satelliti disattivate (satellites.enabled nella configurazione).",
        PassTrackNoCalibration = "serve una calibrazione valida: vedi la vista Camera.",
        PassTrackNoPasses = "nessun passaggio in corso o imminente.",
        PassStatusLabel = "Transito",
        PassNextValue = "{0} · {1}",
        PassInProgressValue = "{0} in corso",
        YouTubeConnected = "Account collegato",
        YouTubeNotConnected = "Account non collegato",
        YouTubeAutomaticOn = "Caricamento automatico attivo",
        YouTubeAutomaticOff = "Caricamento automatico spento",
        YouTubeNextUpload = "Prossimo caricamento: {0}",
        YouTubeNoNextUpload = "Nessun caricamento programmato",
        YouTubeLastUpload = "Ultimo caricamento: notte del {0}, registrato il {1}",
        YouTubeNoUploadYet = "Nessun caricamento completato finora",
        YouTubeOpenVideo = "Apri il video su YouTube",
        YouTubeLastProblem = "Ultimo problema: {0}",
        YouTubeProblemAuthRequired = "Accesso non valido o revocato: collega di nuovo l'account.",
        YouTubeProblemLoginDenied = "Autorizzazione negata nel browser.",
        YouTubeProblemLoginTimedOut = "Nessuna risposta dal browser entro il tempo limite. Riprova.",
        YouTubeProblemCredentialsMissing = "Inserisci client ID e client secret.",
        YouTubeProblemLoginFailed = "Collegamento non riuscito. Controlla la connessione e le credenziali.",
        YouTubeProblemSecretUnavailable = "La protezione dei segreti non è disponibile su questo sistema.",
        YouTubeProblemQuota = "Quota giornaliera di YouTube esaurita: nuovo tentativo domani.",
        YouTubeProblemUploadFailed = "Caricamento non riuscito per questa notte. Vedi il log.",
        YouTubeProblemNeedsReview = "Esito incerto: controlla su YouTube prima di ricaricare.",
        YouTubeProblemRetry = "Errore temporaneo: nuovo tentativo programmato.",
        YouTubeProblemSettingsInvalid = "Impostazioni o modelli non validi: nessuna chiamata a YouTube.",
        YouTubeProblemSettingsNotSaved = "Le credenziali sono salvate, ma le impostazioni no. Riprova.",
        YouTubeProblemUnavailable = "Caricamento YouTube non disponibile in questo avvio. Vedi il log.",
        YouTubeOutcomeCompleted = "Video caricato.",
        YouTubeOutcomeAlreadyUploaded = "La notte risulta già caricata.",
        YouTubeOutcomeInProgress = "Caricamento in corso o in attesa di un nuovo tentativo.",
        YouTubeOutcomeNoNight = "Nessuna notte completata con timelapse pronto.",
        YouTubeOutcomeDailyLimit = "Limite giornaliero di caricamenti raggiunto.",
        YouTubeOutcomeNotDue = "Caricamento non ancora dovuto.",
        YouTubeOutcomeSlotMissed = "L'ora prevista è passata oltre la finestra di recupero: nessun caricamento per oggi.",
        YouTubeProblemDailyTime = "L'ora deve avere il formato HH:mm, per esempio 12:30.",
        YouTubeProblemPrivacy = "Scegli una privacy dall'elenco.",
        YouTubeProblemTag = "Un tag è vuoto.",
        YouTubeProblemCategory = "L'ID categoria deve contenere solo cifre.",
        YouTubeSaveFailed = "Salvataggio non riuscito. Controlla i campi evidenziati e i permessi della cartella dati.",
        YouTubePrivacyPrivate = "Privato",
        YouTubePrivacyUnlisted = "Non in elenco",
        YouTubePrivacyPublic = "Pubblico",
        YouTubeAccountTitle = "Account Google",
        YouTubeClientIdLabel = "Client ID",
        YouTubeClientSecretLabel = "Client secret",
        YouTubeSecretNote = "Il client secret viene cifrato al collegamento e non viene mai mostrato né riletto. Per cambiarlo, collega di nuovo l'account.",
        YouTubeLogin = "Collega account",
        YouTubeLoginCancel = "Annulla collegamento",
        YouTubeLoginWaiting = "In attesa dell'autorizzazione nel browser...",
        YouTubeLogout = "Scollega account",
        YouTubeLoginDone = "Account collegato. Il caricamento automatico è attivo.",
        YouTubeLogoutDone = "Account scollegato. Il caricamento automatico è spento.",
        YouTubeTestingWarning = "Con l'app OAuth in stato «Testing» Google fa scadere il collegamento dopo 7 giorni, e il caricamento notturno si fermerebbe. Passa l'app in «In production» nella Google Cloud Console. Istruzioni: docs/YOUTUBE-OAUTH.md.",
        YouTubeSettingsTitle = "Impostazioni YouTube",
        YouTubeDailyTimeLabel = "Ora del caricamento (HH:mm)",
        YouTubePrivacyLabel = "Privacy",
        YouTubeTagsLabel = "Tag (separati da virgola)",
        YouTubeCategoryLabel = "ID categoria",
        YouTubeCategoryHint = "Numero della categoria YouTube. Predefinito 28, da verificare sulla documentazione YouTube.",
        YouTubeSave = "Salva impostazioni",
        YouTubeSaved = "Impostazioni salvate.",
        YouTubeUploadTitle = "Caricamento",
        YouTubeUploadNow = "Carica ora la notte scorsa",
        YouTubeUploadConfirm = "Caricare ora su YouTube il timelapse della notte più recente completata? Il video usa la privacy scelta nelle impostazioni.",
        YouTubeUploadConfirmRisk = "Il caricamento precedente di questa notte non è andato a buon fine o è incerto. Se il video esiste già su YouTube, ricaricarlo crea un duplicato. Continuare?",
        YouTubeConfirmButton = "Conferma",
        YouTubeCancelButton = "Annulla",
        YouTubeUploadRunning = "Caricamento in corso...",
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
        NightsFramesCount = "{0} frames",
        NightsTimelapseNone = "No timelapse yet",
        NightsTimelapseReady = "Timelapse ready",
        NightsTimelapseGenerating = "In progress",
        NightsTimelapseFailed = "Timelapse failed",
        NightsTimelapseUnknown = "Unknown state",
        NightsTimelapseQueued = "Queued or starting…",
        NightsTimelapseProgress = "In progress: {0}%",
        NightsFramePosition = "Frame {0} of {1}",
        NightsFrameTimeTooltip = "UTC time: {0}",
        NightsListName = "Night list",
        NightsLoading = "Reading the archive…",
        NightsLoadError = "Cannot read the night archive: {0}. Check the data folder and try again.",
        NightsSelectNight = "Select a night from the list to see its keogram, startrails and timelapse.",
        NightsTimelapseTitle = "Timelapse",
        NightsTimelapsePlay = "Play",
        NightsTimelapsePause = "Pause",
        NightsTimelapseStop = "Stop",
        NightsTimelapseRegenerate = "Regenerate timelapse",
        NightsTimelapseRegenerateTooltip = "Rebuilds the video from the archived frames. The current file is replaced when done.",
        NightsTimelapseRefused = "Cannot queue the timelapse: the night has no frames or is already queued.",
        NightsTimelapseRequested = "Timelapse queued. It will appear here when ready.",
        NightsTimelapseRegenerateError = "Cannot start regeneration: {0}",
        NightsTimelapseMissingFile = "The timelapse file is missing. Regenerate it to create it again.",
        NightsPlaybackError = "Cannot play the timelapse. Check that the H.264 codec is installed (Windows Video Extensions), then try again.",
        NightsPlayerName = "Timelapse playback",
        NightsKeogramTitle = "Keogram",
        NightsKeogramName = "Night keogram, time scrubber",
        NightsKeogramHint = "Click or drag on the keogram to see the frame at that moment. With the keogram focused, the ← and → arrows move one frame at a time.",
        NightsKeogramMissing = "No keogram for this night.",
        NightsStartrailsTitle = "Startrails",
        NightsStartrailsName = "Night startrails",
        NightsStartrailsMissing = "No startrails for this night.",
        NightsFrameTitle = "Selected frame",
        NightsFrameName = "Selected night frame",
        NightsFrameMissing = "This frame file is no longer available.",
        NightsNoFrames = "No archived frames in this night.",
        IllusNoNightTitle = "No night yet",
        IllusNoNightBody = "Images will appear here after sunset, when night capture starts.",
        IllusNoEventsTitle = "No events",
        IllusNoEventsBody = "Detected meteors, fireballs and satellite passes will appear here. There are none yet.",
        IllusCameraTitle = "Camera not connected",
        IllusCameraBody = "Connect a camera from the Camera view to see live images.",
        IllusYouTubeTitle = "YouTube not connected",
        IllusYouTubeBody = "Connect a YouTube account from the Publication view to upload the timelapse automatically.",
        TonightNav = "Tonight",
        TonightTitle = "Tonight",
        TonightImageName = "Sky image for tonight",
        TonightNoImage = "No image yet. The first frame will appear here.",
        TonightComputing = "Calculating…",
        TonightLoadError = "Cannot compute tonight: {0}",
        TonightSiteInvalid = "Invalid site position: check latitude and longitude in settings.",
        TonightTimelineTitle = "Night timeline",
        TonightTimelineName = "Timeline from sunset to sunrise",
        TonightSunset = "Sunset",
        TonightSunrise = "Sunrise",
        TonightNoWindow = "This night has no sunset or sunrise: the Sun stays always above or always below the horizon.",
        TonightNowMarker = "Now: {0}",
        TonightNotInWindow = "Right now we are outside the window between sunset and sunrise.",
        TonightGeometricNote = "Geometric altitudes, without atmospheric refraction.",
        TonightSunCurve = "Sun",
        TonightMoonCurve = "Moon",
        TonightHorizon = "Horizon",
        TonightCurveName = "Sun and Moon altitude during the night",
        TonightSegmentLine = "{0}: from {1} to {2}",
        TonightPhaseDay = "Day",
        TonightPhaseCivil = "Civil twilight",
        TonightPhaseNautical = "Nautical twilight",
        TonightPhaseAstro = "Astronomical twilight",
        TonightPhaseNight = "Astronomical night",
        TonightConditionsTitle = "Conditions",
        TonightMoonGlyphName = "Moon phase: {0}, {1} illuminated",
        TonightMoonNew = "New moon",
        TonightMoonWaxingCrescent = "Waxing crescent",
        TonightMoonFirstQuarter = "First quarter",
        TonightMoonWaxingGibbous = "Waxing gibbous",
        TonightMoonFull = "Full moon",
        TonightMoonWaningGibbous = "Waning gibbous",
        TonightMoonLastQuarter = "Last quarter",
        TonightMoonWaningCrescent = "Waning crescent",
        TonightMoonAltitude = "Moon altitude",
        TonightSunAltitude = "Sun altitude",
        TonightSkyClarity = "Clear-sky index",
        TonightSkyClarityPending = "Not available yet: it will arrive with star detection.",
        TonightWeather = "Weather",
        TonightWeatherPending = "Not available yet: it will arrive with the weather source.",
        TonightEventsTitle = "Upcoming events",
        TonightNextChange = "Next sky phase change",
        TonightNextTransit = "Next transit",
        TonightNoTransit = "No transit predicted",
        TonightTransitPending = "Transit prediction is not active yet.",
        TonightLastMeteor = "Last meteor",
        TonightNoMeteor = "No meteor recorded",
        TonightMeteorPending = "Meteor detection is not active yet.",
        TonightCaptureTitle = "Capture status",
        TonightCountdownIn = "in {0}",
        TonightCountdownMinutes = "{0} min",
        TonightCountdownHoursMinutes = "{0} h {1} min",
        TonightCountdownLessThanMinute = "less than 1 min",
        TonightNow = "now",
        TonightAgo = "{0} ago",
        CameraDiscButton = "Disconnect",
        SettingsTitle = "Settings",
        SettingsObservatoryTitle = "Observatory",
        SettingsLatitude = "Latitude (°)",
        SettingsLongitude = "Longitude (°)",
        SettingsElevation = "Elevation (m)",
        SettingsSiteHint = "Latitude from -90 to 90, longitude from -180 to 180 (positive east), elevation from -500 to 9000 metres.",
        SettingsProblemEmpty = "Enter a value.",
        SettingsProblemNotNumber = "Enter a number.",
        SettingsProblemLatitude = "Latitude must be between -90 and 90.",
        SettingsProblemLongitude = "Longitude must be between -180 and 180.",
        SettingsProblemElevation = "Elevation must be between -500 and 9000 metres.",
        SettingsSiteSave = "Save location",
        SettingsSiteSaved = "Location saved. Sky and phase calculations use the new values.",
        SettingsNoChanges = "No changes to save.",
        SettingsSaveError = "Could not save the settings. Check the permissions of the data folder and try again.",
        SettingsInvalidValues = "Some values are not valid: fix them and save again.",
        SettingsGeneralTitle = "General",
        SettingsLanguageLabel = "Language",
        SettingsLanguageRestart = "The language change applies the next time All Sky Brain starts.",
        SettingsLanguageSaved = "Language saved. Restart the app to apply it.",
        SettingsThemeLabel = "Theme",
        SettingsThemeNormal = "Standard",
        SettingsThemeNightRed = "Red night",
        SettingsThemeHint = "Red night uses no blue or green, so it does not disturb dark adaptation. Also toggled with Ctrl+R.",
        SettingsDataFolderTitle = "Data folder",
        SettingsDataFolderPath = "Path: {0}",
        SettingsDataFolderReadOnly = "Read only. Choose the folder at startup with --data-folder.",
        SettingsOpenFolder = "Open folder",
        SettingsOpenFolderError = "Could not open the folder. Path: {0}",
        SettingsStartupTitle = "Startup",
        SettingsStartWithWindows = "Start All Sky Brain with Windows",
        SettingsStartWithWindowsHint = "Registers the app for the current user's startup. With --data-folder, automatic start uses the default folder.",
        SettingsAutostartError = "Could not change startup with Windows: {0}",
        SettingsUpdatesTitle = "Updates",
        SettingsCurrentVersion = "Installed version: {0}",
        SettingsAutoCheck = "Check for updates and download them automatically",
        SettingsAutoCheckHint = "The first check runs one minute after startup, then every 24 hours. Installing always needs your confirmation.",
        SettingsCheckNow = "Check now",
        SettingsChecking = "Checking…",
        SettingsUpToDate = "You already have the latest version.",
        SettingsUpdateAvailable = "Version {0} is available. Use \"Check for updates\" from the notification area icon to download it.",
        SettingsUpdateDisabled = "Updates are only available for the installed version, not for the portable or development copy.",
        SettingsUpdateNetworkError = "The update server is not reachable. Check the connection and try again.",
        SettingsUpdateGenericError = "Check failed. Try again later.",
        SettingsLastCheck = "Last check: {0}",
        SettingsNeverChecked = "Never checked.",
        SettingsAccessTitle = "Browser access",
        SettingsWebEnabled = "Enable the web interface",
        SettingsWebHint = "Starts a small local server for the browser panel. It is off by default: the desktop app does not need it.",
        SettingsWebRestart = "The change applies the next time All Sky Brain starts.",
        SettingsWebRunning = "Running at {0}.",
        SettingsWebNotRunning = "Not running in this session.",
        SettingsLanEnabled = "Allow access from the local network",
        SettingsLanHint = "Listens on all interfaces and asks for a password. Traffic is not encrypted: use it only on trusted networks.",
        SettingsLanPasswordLabel = "Local network password",
        SettingsLanPasswordHint = "At least 8 characters. It is never shown: you can only set a new one.",
        SettingsLanPasswordSet = "Password set.",
        SettingsLanPasswordNotSet = "No password set.",
        SettingsPasswordTooShort = "At least 8 characters.",
        SettingsPasswordRequired = "A password is required for the local network.",
        SettingsAccessSave = "Save access",
        SettingsAccessSaved = "Access settings saved. Restart the app to apply them.",
        SettingsSaving = "Saving…",
        SettingsSaved = "Settings saved.",
        LogViewTitle = "Log",
        LogViewLevelLabel = "Level",
        LogViewLevelAll = "All",
        LogViewLevelWarnings = "Warnings and errors",
        LogViewLevelErrors = "Errors only",
        LogViewSearchLabel = "Search text",
        LogViewAutoScroll = "Scroll to bottom",
        LogViewRefresh = "Refresh",
        LogViewCopy = "Copy",
        LogViewOpenFile = "Open file",
        LogViewEmpty = "No entries match the filter.",
        LogViewNoFile = "No log file yet. It appears the first time All Sky Brain starts.",
        LogViewCopied = "Text copied to the clipboard.",
        LogViewOpenError = "Could not open the log file. File: {0}",
        LogViewReadError = "Could not read the log. Try again shortly.",
        LogViewSecretsNote = "Tokens, passwords and client secrets are masked.",
        LogViewCount = "{0} entries",
        LogViewFile = "File: {0}",
        LogViewTagInfo = "Info",
        LogViewTagWarning = "Warning",
        LogViewTagError = "Error",
        CameraDiscBusy = "Disconnecting…",
        CameraDiscDone = "Camera disconnected.",
        PublicationSettingsTitle = "Uplink settings",
        PublicationEnabled = "Publish to the server",
        PublicationServerUrl = "Server address",
        PublicationServerUrlHint = "Base address only, for example https://server-name. Plain http is allowed only for localhost and 127.0.0.1.",
        PublicationDeviceId = "Device ID",
        PublicationDeviceIdHint = "Letters, numbers, dot, dash or underscore. Up to 64 characters.",
        PublicationProfileName = "Profile name (optional)",
        PublicationInterval = "Interval between uploads, in seconds",
        PublicationRangeHint = "From {0} to {1}. Default: {2}.",
        PublicationLongEdge = "Long edge of the image, in pixels",
        PublicationJpegQuality = "JPEG quality",
        PublicationTokenLabel = "Token",
        PublicationSecretStatus = "Status: {0}",
        PublicationSecretSet = "set",
        PublicationSecretNotSet = "not set",
        PublicationSecretReplace = "Replace with a new value (leave empty to keep the current one)",
        PublicationSecretHint = "The value is encrypted on disk and is never shown, not even in part.",
        PublicationClearToken = "Remove the saved token",
        PublicationExtraHeaderName = "Extra HTTP header: name (optional)",
        PublicationExtraHeaderValue = "Extra HTTP header: value",
        PublicationClearExtraHeader = "Remove the saved extra header",
        PublicationSave = "Save settings",
        PublicationSaving = "Saving…",
        PublicationSaved = "Settings saved.",
        PublicationNoChanges = "Nothing to save.",
        PublicationSaveFailed = "Settings not saved. Fix the fields listed.",
        PublicationIssueLine = "{0}: {1}",
        PublicationFieldGeneral = "Publishing",
        PublicationUnavailable = "Publishing is unavailable: the module did not start. Check the application log.",
        PublicationProblemEmpty = "Required field.",
        PublicationProblemNotInteger = "Enter a whole number.",
        PublicationProblemOutOfRange = "The value must be between {0} and {1}.",
        PublicationTest = "Test connection",
        PublicationTesting = "Testing…",
        PublicationTestNote = "The test uses the values in the form, even if not saved. It sends no images.",
        PublicationTestOk = "Connection OK: the server accepts the token and device ID.",
        PublicationTestFailed = "Test failed: {0}",
        PublicationStatusTitle = "Status",
        PublicationStateUnknown = "Status unavailable",
        PublicationStateDisabled = "Off",
        PublicationStateDisabledHint = "Turn on «Publish to the server», enter the address, device ID and token, then save.",
        PublicationStateIdle = "On, waiting for the next upload",
        PublicationStateSending = "Uploading",
        PublicationStateBackoff = "Temporary error: next attempt scheduled",
        PublicationStateSuspended = "Paused: credentials rejected. Save a new token to resume.",
        PublicationCounters = "Queued: {0} · Sent: {1} · Discarded: {2}",
        PublicationNoSuccessYet = "No successful upload yet",
        PublicationLastSuccess = "Last successful upload: {0}",
        PublicationNextAttempt = "Next attempt: {0}",
        PublicationLastErrorLine = "Last error at {0}: {1}",
        PublicationReasonAuth = "The server rejected the token or device ID.",
        PublicationReasonRejected = "The server rejected the image: discarded, not retried.",
        PublicationReasonTooLarge = "Image too large for the server: discarded.",
        PublicationReasonBadResponse = "Invalid server response: will retry later.",
        PublicationReasonServerCode = "Server error (code {0}).",
        PublicationReasonServer = "Temporary server error.",
        PublicationReasonRateLimited = "The server asks to slow down: retrying after the indicated wait.",
        PublicationReasonNetwork = "Server unreachable: check the address and the network.",
        PublicationReasonTimeout = "The server did not respond in time.",
        PublicationReasonInvalidSettings = "Invalid address or settings: check the form.",
        PublicationReasonNoHealth = "The server has no health check page: check the address.",
        PublicationReasonOther = "Unclassified error: see the publishing log.",
        PublicationYouTubeTitle = "YouTube",
        OvlSun = "Sun",
        OvlMoon = "Moon",
        OvlPhase = "phase",
        OvlSatellites = "Satellites",
        OvlNone = "none",
        OvlMissing = "n/a",
        OvlExposure = "exp",
        OvlGain = "gain",
        CalibTitle = "Optical calibration",
        CalibStatusCollecting = "Sun points collected: {0} of {1} needed.",
        CalibStatusCollectingHint = "The Sun is looked for by day, when it is above 10°. Points are needed in several directions of the sky.",
        CalibFailureReason = "Last attempt: {0}",
        CalibLastPoint = "Last point: {0}",
        CalibStatusReview = "Proposal ready: check the grid on the frame, then accept or reject.",
        CalibStatusReviewManual = "Manual values pending: accept to save them.",
        CalibStatusReviewNotAcceptable = "Error of {0} px, above the {1}% of the radius threshold: this proposal cannot be accepted. Collect more points or use manual mode.",
        CalibStatusCalibrated = "Calibration active: error {0} px, {1}, from {2}.",
        CalibSourceSun = "estimated from the Sun",
        CalibSourceManual = "entered manually",
        CalibResidualSummary = "Mean error {0} px over {1} points; {2} excluded as outliers.",
        CalibFrameTitle = "Preview",
        CalibFrameEmpty = "No frame yet: the grid appears with the first frame.",
        CalibGridLegend = "Alt/az grid: circles at 0°, 30° and 60°, meridians every 30°. N, E, S, W on the horizon.",
        CalibObservedName = "Observed Sun points",
        CalibAccept = "Accept",
        CalibReject = "Reject",
        CalibAcceptedNotice = "Calibration saved and applied.",
        CalibRejectedNotice = "Proposal discarded: collection continues.",
        CalibPointsTitle = "Points collected",
        CalibNoPoints = "No points yet.",
        CalibManualTitle = "Manual mode",
        CalibManualHint = "Values update the preview at once. The radius is the horizon radius, in frame pixels.",
        CalibManualCenterX = "Center X (px)",
        CalibManualCenterY = "Center Y (px)",
        CalibManualRadius = "Horizon radius (px)",
        CalibManualK2 = "Radial correction K2",
        CalibManualRotation = "North rotation (°)",
        CalibManualMirrored = "East on the right (mirrored)",
        CalibManualPreviewOn = "Manual preview: not saved yet.",
        CalibManualUse = "Use as proposal",
        CalibManualReset = "Frame values",
        CalibManualNoFrame = "No frame: manual mode needs at least one frame.",
        CalibManualInvalid = "Fix the highlighted fields.",
        CalibProblemEmpty = "Enter a value.",
        CalibProblemNotNumber = "Enter a number.",
        CalibProblemNotPositive = "The radius must be greater than zero.",
        CalibProblemOutOfRange = "K2 must be greater than -0.33.",
        CalibActionNoProposal = "There is no proposal to accept.",
        CalibActionNotAcceptable = "The proposal does not meet the precision threshold.",
        CalibActionNoFrame = "No frame received: manual mode cannot be set.",
        CalibActionSaveFailed = "Save failed. Check the permissions of the data folder and try again.",
        CalibCardinalNorth = "N",
        CalibCardinalEast = "E",
        CalibCardinalSouth = "S",
        CalibCardinalWest = "W",
        PassListTitle = "Upcoming passes",
        PassStatusDisabled = "Satellite forecasts are off. Turn them on with satellites.enabled in the configuration file.",
        PassStatusWaiting = "Waiting for orbital data (CelesTrak).",
        PassStatusBlocked = "CelesTrak requests suspended: {0}",
        PassStatusFailed = "orbital data unavailable: {0}",
        PassNoneInWindow = "No passes in the forecast window.",
        PassInProgress = "in progress",
        PassTimes = "start {0} · peak {1} · end {2}",
        PassMaxElevation = "max alt. {0}",
        PassDirection = "from {0} to {1}",
        PassCompassN = "N",
        PassCompassNE = "NE",
        PassCompassE = "E",
        PassCompassSE = "SE",
        PassCompassS = "S",
        PassCompassSW = "SW",
        PassCompassW = "W",
        PassCompassNW = "NW",
        PassLightVisible = "visible to the naked eye",
        PassLightLit = "sunlit, sky not dark",
        PassLightShadow = "in Earth's shadow",
        PassTrackServiceOff = "satellite forecasts are off (satellites.enabled in the configuration).",
        PassTrackNoCalibration = "a valid calibration is needed: see the Camera view.",
        PassTrackNoPasses = "no pass in progress or imminent.",
        PassStatusLabel = "Transit",
        PassNextValue = "{0} · {1}",
        PassInProgressValue = "{0} in progress",
        YouTubeConnected = "Account connected",
        YouTubeNotConnected = "Account not connected",
        YouTubeAutomaticOn = "Automatic upload on",
        YouTubeAutomaticOff = "Automatic upload off",
        YouTubeNextUpload = "Next upload: {0}",
        YouTubeNoNextUpload = "No upload scheduled",
        YouTubeLastUpload = "Last upload: night of {0}, recorded on {1}",
        YouTubeNoUploadYet = "No upload completed yet",
        YouTubeOpenVideo = "Open the video on YouTube",
        YouTubeLastProblem = "Last problem: {0}",
        YouTubeProblemAuthRequired = "Access is invalid or revoked: connect the account again.",
        YouTubeProblemLoginDenied = "Authorization denied in the browser.",
        YouTubeProblemLoginTimedOut = "No response from the browser before the time limit. Try again.",
        YouTubeProblemCredentialsMissing = "Enter the client ID and client secret.",
        YouTubeProblemLoginFailed = "Connection failed. Check the connection and the credentials.",
        YouTubeProblemSecretUnavailable = "Secret protection is not available on this system.",
        YouTubeProblemQuota = "Daily YouTube quota used up: next attempt tomorrow.",
        YouTubeProblemUploadFailed = "Upload failed for this night. See the log.",
        YouTubeProblemNeedsReview = "Outcome uncertain: check on YouTube before uploading again.",
        YouTubeProblemRetry = "Temporary error: a new attempt is scheduled.",
        YouTubeProblemSettingsInvalid = "Settings or templates are invalid: no call to YouTube.",
        YouTubeProblemSettingsNotSaved = "The credentials are saved, but the settings are not. Try again.",
        YouTubeProblemUnavailable = "YouTube upload is not available in this session. See the log.",
        YouTubeOutcomeCompleted = "Video uploaded.",
        YouTubeOutcomeAlreadyUploaded = "This night is already uploaded.",
        YouTubeOutcomeInProgress = "Upload in progress or waiting for another attempt.",
        YouTubeOutcomeNoNight = "No completed night with a timelapse ready.",
        YouTubeOutcomeDailyLimit = "Daily upload limit reached.",
        YouTubeOutcomeNotDue = "Upload not due yet.",
        YouTubeOutcomeSlotMissed = "The scheduled time passed outside the catch-up window: no upload today.",
        YouTubeProblemDailyTime = "The time must use HH:mm, for example 12:30.",
        YouTubeProblemPrivacy = "Choose a privacy setting from the list.",
        YouTubeProblemTag = "A tag is empty.",
        YouTubeProblemCategory = "The category ID must contain digits only.",
        YouTubeSaveFailed = "Save failed. Check the highlighted fields and the data folder permissions.",
        YouTubePrivacyPrivate = "Private",
        YouTubePrivacyUnlisted = "Unlisted",
        YouTubePrivacyPublic = "Public",
        YouTubeAccountTitle = "Google account",
        YouTubeClientIdLabel = "Client ID",
        YouTubeClientSecretLabel = "Client secret",
        YouTubeSecretNote = "The client secret is encrypted when you connect and is never shown or read back. To change it, connect the account again.",
        YouTubeLogin = "Connect account",
        YouTubeLoginCancel = "Cancel connection",
        YouTubeLoginWaiting = "Waiting for authorization in the browser...",
        YouTubeLogout = "Disconnect account",
        YouTubeLoginDone = "Account connected. Automatic upload is on.",
        YouTubeLogoutDone = "Account disconnected. Automatic upload is off.",
        YouTubeTestingWarning = "With the OAuth app in «Testing» status, Google expires the connection after 7 days, and the nightly upload would stop. Move the app to «In production» in the Google Cloud Console. Instructions: docs/YOUTUBE-OAUTH.md.",
        YouTubeSettingsTitle = "YouTube settings",
        YouTubeDailyTimeLabel = "Upload time (HH:mm)",
        YouTubePrivacyLabel = "Privacy",
        YouTubeTagsLabel = "Tags (comma-separated)",
        YouTubeCategoryLabel = "Category ID",
        YouTubeCategoryHint = "YouTube category number. Default 28, to be checked in the YouTube documentation.",
        YouTubeSave = "Save settings",
        YouTubeSaved = "Settings saved.",
        YouTubeUploadTitle = "Upload",
        YouTubeUploadNow = "Upload last night now",
        YouTubeUploadConfirm = "Upload the timelapse of the most recent completed night to YouTube now? The video uses the privacy set in the settings.",
        YouTubeUploadConfirmRisk = "The previous upload of this night failed or is uncertain. If the video already exists on YouTube, uploading it again creates a duplicate. Continue?",
        YouTubeConfirmButton = "Confirm",
        YouTubeCancelButton = "Cancel",
        YouTubeUploadRunning = "Upload in progress...",
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

    /// <summary>Numero di frame di una notte, con la parola "frame" o "frames" (segnaposto {0}).</summary>
    public required string NightsFramesCount { get; init; }

    /// <summary>Stato del timelapse: non creato.</summary>
    public required string NightsTimelapseNone { get; init; }

    /// <summary>Stato del timelapse: pronto alla riproduzione.</summary>
    public required string NightsTimelapseReady { get; init; }

    /// <summary>Stato del timelapse: in lavorazione.</summary>
    public required string NightsTimelapseGenerating { get; init; }

    /// <summary>Stato del timelapse: ultima generazione non riuscita.</summary>
    public required string NightsTimelapseFailed { get; init; }

    /// <summary>Stato del timelapse non riconosciuto.</summary>
    public required string NightsTimelapseUnknown { get; init; }

    /// <summary>Avanzamento del timelapse quando è in coda o non ha ancora progresso.</summary>
    public required string NightsTimelapseQueued { get; init; }

    /// <summary>Avanzamento del timelapse in percentuale intera (segnaposto {0}).</summary>
    public required string NightsTimelapseProgress { get; init; }

    /// <summary>Posizione del frame selezionato, numerata da 1 (segnaposti {0} e {1}).</summary>
    public required string NightsFramePosition { get; init; }

    /// <summary>Tooltip con l'istante UTC del frame (segnaposto {0}).</summary>
    public required string NightsFrameTimeTooltip { get; init; }

    /// <summary>Nome dell'elenco delle notti per le tecnologie di accessibilità.</summary>
    public required string NightsListName { get; init; }

    /// <summary>Stato di caricamento della vista "Notti".</summary>
    public required string NightsLoading { get; init; }

    /// <summary>Errore nella lettura dell'archivio (segnaposto {0} con il dettaglio).</summary>
    public required string NightsLoadError { get; init; }

    /// <summary>Stato vuoto del dettaglio: nessuna notte selezionata.</summary>
    public required string NightsSelectNight { get; init; }

    /// <summary>Titolo della sezione timelapse.</summary>
    public required string NightsTimelapseTitle { get; init; }

    /// <summary>Pulsante "Riproduci" del timelapse.</summary>
    public required string NightsTimelapsePlay { get; init; }

    /// <summary>Pulsante "Pausa" del timelapse.</summary>
    public required string NightsTimelapsePause { get; init; }

    /// <summary>Pulsante "Stop" del timelapse.</summary>
    public required string NightsTimelapseStop { get; init; }

    /// <summary>Pulsante "Rigenera timelapse".</summary>
    public required string NightsTimelapseRegenerate { get; init; }

    /// <summary>Spiegazione del pulsante "Rigenera timelapse", con l'effetto sul file attuale.</summary>
    public required string NightsTimelapseRegenerateTooltip { get; init; }

    /// <summary>Messaggio quando la rigenerazione non viene accodata.</summary>
    public required string NightsTimelapseRefused { get; init; }

    /// <summary>Messaggio quando la rigenerazione è stata accodata.</summary>
    public required string NightsTimelapseRequested { get; init; }

    /// <summary>Errore nell'avvio della rigenerazione (segnaposto {0} con il dettaglio).</summary>
    public required string NightsTimelapseRegenerateError { get; init; }

    /// <summary>Messaggio quando il file del timelapse manca.</summary>
    public required string NightsTimelapseMissingFile { get; init; }

    /// <summary>Messaggio quando il lettore non riesce a riprodurre il file, di solito per un codec mancante.</summary>
    public required string NightsPlaybackError { get; init; }

    /// <summary>Nome del lettore del timelapse per le tecnologie di accessibilità.</summary>
    public required string NightsPlayerName { get; init; }

    /// <summary>Titolo della sezione keogram.</summary>
    public required string NightsKeogramTitle { get; init; }

    /// <summary>Nome della barra del keogram per le tecnologie di accessibilità.</summary>
    public required string NightsKeogramName { get; init; }

    /// <summary>Suggerimento d'uso della barra del keogram, con i comandi da tastiera.</summary>
    public required string NightsKeogramHint { get; init; }

    /// <summary>Messaggio quando il keogram della notte non esiste.</summary>
    public required string NightsKeogramMissing { get; init; }

    /// <summary>Titolo della sezione startrails.</summary>
    public required string NightsStartrailsTitle { get; init; }

    /// <summary>Nome dell'immagine startrails per le tecnologie di accessibilità.</summary>
    public required string NightsStartrailsName { get; init; }

    /// <summary>Messaggio quando gli startrails della notte non esistono.</summary>
    public required string NightsStartrailsMissing { get; init; }

    /// <summary>Titolo della sezione del frame selezionato.</summary>
    public required string NightsFrameTitle { get; init; }

    /// <summary>Nome dell'immagine del frame per le tecnologie di accessibilità.</summary>
    public required string NightsFrameName { get; init; }

    /// <summary>Messaggio quando il file del frame non esiste più.</summary>
    public required string NightsFrameMissing { get; init; }

    /// <summary>Messaggio quando la notte selezionata non ha frame archiviati.</summary>
    public required string NightsNoFrames { get; init; }

    /// <summary>Etichetta "Sole" nella maschera informativa del timelapse.</summary>
    public required string OvlSun { get; init; }

    /// <summary>Etichetta "Luna" nella maschera informativa del timelapse.</summary>
    public required string OvlMoon { get; init; }

    /// <summary>Etichetta della fase lunare nella maschera informativa.</summary>
    public required string OvlPhase { get; init; }

    /// <summary>Intestazione dei satelliti in transito nella maschera informativa.</summary>
    public required string OvlSatellites { get; init; }

    /// <summary>Valore quando nessun satellite e' in transito nel frame.</summary>
    public required string OvlNone { get; init; }

    /// <summary>Segnaposto per un dato mancante nella maschera informativa.</summary>
    public required string OvlMissing { get; init; }

    /// <summary>Abbreviazione di "esposizione" nella riga di esposizione della maschera.</summary>
    public required string OvlExposure { get; init; }

    /// <summary>Etichetta del gain nella riga di esposizione della maschera.</summary>
    public required string OvlGain { get; init; }

    /// <summary>Titolo del pannello di calibrazione ottica.</summary>
    public required string CalibTitle { get; init; }

    /// <summary>Stato della raccolta: punti raccolti su quelli necessari ({0} e {1}).</summary>
    public required string CalibStatusCollecting { get; init; }

    /// <summary>Suggerimento su quando e dove cercare il Sole.</summary>
    public required string CalibStatusCollectingHint { get; init; }

    /// <summary>Motivo dell'ultimo fit fallito ({0}).</summary>
    public required string CalibFailureReason { get; init; }

    /// <summary>Ora dell'ultimo punto raccolto ({0}).</summary>
    public required string CalibLastPoint { get; init; }

    /// <summary>Stato con una proposta del Sole pronta da accettare.</summary>
    public required string CalibStatusReview { get; init; }

    /// <summary>Stato con valori manuali in attesa di accettazione.</summary>
    public required string CalibStatusReviewManual { get; init; }

    /// <summary>Proposta del Sole sopra soglia: errore ({0}) e soglia in percentuale ({1}).</summary>
    public required string CalibStatusReviewNotAcceptable { get; init; }

    /// <summary>Calibrazione attiva: errore ({0}), origine ({1}), data ({2}).</summary>
    public required string CalibStatusCalibrated { get; init; }

    /// <summary>Origine "stimata dal Sole".</summary>
    public required string CalibSourceSun { get; init; }

    /// <summary>Origine "inserita a mano".</summary>
    public required string CalibSourceManual { get; init; }

    /// <summary>Riepilogo dei residui: errore medio ({0}), punti ({1}), outlier ({2}).</summary>
    public required string CalibResidualSummary { get; init; }

    /// <summary>Titolo dell'anteprima del frame.</summary>
    public required string CalibFrameTitle { get; init; }

    /// <summary>Messaggio dell'anteprima quando non c'e' ancora un frame.</summary>
    public required string CalibFrameEmpty { get; init; }

    /// <summary>Legenda della griglia alt/az sovrapposta.</summary>
    public required string CalibGridLegend { get; init; }

    /// <summary>Nome accessibile dei punti del Sole sovrapposti.</summary>
    public required string CalibObservedName { get; init; }

    /// <summary>Pulsante "Accetta".</summary>
    public required string CalibAccept { get; init; }

    /// <summary>Pulsante "Rifiuta".</summary>
    public required string CalibReject { get; init; }

    /// <summary>Conferma dell'accettazione.</summary>
    public required string CalibAcceptedNotice { get; init; }

    /// <summary>Conferma dello scarto della proposta.</summary>
    public required string CalibRejectedNotice { get; init; }

    /// <summary>Titolo dell'elenco dei punti raccolti.</summary>
    public required string CalibPointsTitle { get; init; }

    /// <summary>Messaggio quando non c'e' ancora nessun punto.</summary>
    public required string CalibNoPoints { get; init; }

    /// <summary>Titolo della sezione manuale.</summary>
    public required string CalibManualTitle { get; init; }

    /// <summary>Spiegazione della modalita' manuale.</summary>
    public required string CalibManualHint { get; init; }

    /// <summary>Etichetta del centro x.</summary>
    public required string CalibManualCenterX { get; init; }

    /// <summary>Etichetta del centro y.</summary>
    public required string CalibManualCenterY { get; init; }

    /// <summary>Etichetta del raggio dell'orizzonte.</summary>
    public required string CalibManualRadius { get; init; }

    /// <summary>Etichetta della correzione radiale K2.</summary>
    public required string CalibManualK2 { get; init; }

    /// <summary>Etichetta della rotazione del Nord.</summary>
    public required string CalibManualRotation { get; init; }

    /// <summary>Etichetta dello specchio (Est a destra).</summary>
    public required string CalibManualMirrored { get; init; }

    /// <summary>Avviso che l'anteprima manuale non e' ancora salvata.</summary>
    public required string CalibManualPreviewOn { get; init; }

    /// <summary>Pulsante che propone il modello manuale.</summary>
    public required string CalibManualUse { get; init; }

    /// <summary>Pulsante che riporta i valori manuali a quelli del frame.</summary>
    public required string CalibManualReset { get; init; }

    /// <summary>Messaggio quando la modalita' manuale non ha un frame.</summary>
    public required string CalibManualNoFrame { get; init; }

    /// <summary>Messaggio quando almeno un campo manuale non e' valido.</summary>
    public required string CalibManualInvalid { get; init; }

    /// <summary>Problema di campo: vuoto.</summary>
    public required string CalibProblemEmpty { get; init; }

    /// <summary>Problema di campo: non e' un numero.</summary>
    public required string CalibProblemNotNumber { get; init; }

    /// <summary>Problema di campo: il raggio non e' positivo.</summary>
    public required string CalibProblemNotPositive { get; init; }

    /// <summary>Problema di campo: K2 fuori dall'intervallo.</summary>
    public required string CalibProblemOutOfRange { get; init; }

    /// <summary>Esito: nessuna proposta da accettare.</summary>
    public required string CalibActionNoProposal { get; init; }

    /// <summary>Esito: proposta sopra soglia.</summary>
    public required string CalibActionNotAcceptable { get; init; }

    /// <summary>Esito: nessun frame per la modalita' manuale.</summary>
    public required string CalibActionNoFrame { get; init; }

    /// <summary>Esito: salvataggio non riuscito.</summary>
    public required string CalibActionSaveFailed { get; init; }

    /// <summary>Lettera del Nord sulla griglia.</summary>
    public required string CalibCardinalNorth { get; init; }

    /// <summary>Lettera dell'Est sulla griglia.</summary>
    public required string CalibCardinalEast { get; init; }

    /// <summary>Lettera del Sud sulla griglia.</summary>
    public required string CalibCardinalSouth { get; init; }

    /// <summary>Lettera dell'Ovest sulla griglia.</summary>
    public required string CalibCardinalWest { get; init; }

    /// <summary>Titolo dell'illustrazione "nessuna notte".</summary>
    public required string IllusNoNightTitle { get; init; }

    /// <summary>Testo dell'illustrazione "nessuna notte".</summary>
    public required string IllusNoNightBody { get; init; }

    /// <summary>Titolo dell'illustrazione "nessun evento".</summary>
    public required string IllusNoEventsTitle { get; init; }

    /// <summary>Testo dell'illustrazione "nessun evento".</summary>
    public required string IllusNoEventsBody { get; init; }

    /// <summary>Titolo dell'illustrazione "camera non collegata".</summary>
    public required string IllusCameraTitle { get; init; }

    /// <summary>Testo dell'illustrazione "camera non collegata".</summary>
    public required string IllusCameraBody { get; init; }

    /// <summary>Titolo dell'illustrazione "YouTube non collegato".</summary>
    public required string IllusYouTubeTitle { get; init; }

    /// <summary>Testo dell'illustrazione "YouTube non collegato".</summary>
    public required string IllusYouTubeBody { get; init; }

    /// <summary>Voce di menu della vista "Stanotte".</summary>
    public required string TonightNav { get; init; }

    /// <summary>Titolo della vista "Stanotte".</summary>
    public required string TonightTitle { get; init; }

    /// <summary>Nome dell'immagine del cielo per le tecnologie di accessibilità.</summary>
    public required string TonightImageName { get; init; }

    /// <summary>Messaggio quando non c'è ancora alcun frame da mostrare.</summary>
    public required string TonightNoImage { get; init; }

    /// <summary>Testo mentre la notte viene calcolata.</summary>
    public required string TonightComputing { get; init; }

    /// <summary>Errore di calcolo della notte; {0} è il dettaglio.</summary>
    public required string TonightLoadError { get; init; }

    /// <summary>Messaggio quando la posizione del sito non permette il calcolo.</summary>
    public required string TonightSiteInvalid { get; init; }

    /// <summary>Titolo della linea temporale della notte.</summary>
    public required string TonightTimelineTitle { get; init; }

    /// <summary>Nome della linea temporale per le tecnologie di accessibilità.</summary>
    public required string TonightTimelineName { get; init; }

    /// <summary>Etichetta del tramonto.</summary>
    public required string TonightSunset { get; init; }

    /// <summary>Etichetta dell'alba.</summary>
    public required string TonightSunrise { get; init; }

    /// <summary>Messaggio per una notte senza tramonto né alba.</summary>
    public required string TonightNoWindow { get; init; }

    /// <summary>Marcatore dell'ora corrente; {0} è l'orario locale.</summary>
    public required string TonightNowMarker { get; init; }

    /// <summary>Messaggio quando l'ora corrente non cade fra tramonto e alba.</summary>
    public required string TonightNotInWindow { get; init; }

    /// <summary>Nota sul tipo di altezze mostrate.</summary>
    public required string TonightGeometricNote { get; init; }

    /// <summary>Nome della curva del Sole.</summary>
    public required string TonightSunCurve { get; init; }

    /// <summary>Nome della curva della Luna.</summary>
    public required string TonightMoonCurve { get; init; }

    /// <summary>Nome della linea dell'orizzonte.</summary>
    public required string TonightHorizon { get; init; }

    /// <summary>Nome del grafico delle altezze, per le tecnologie di accessibilità.</summary>
    public required string TonightCurveName { get; init; }

    /// <summary>Riga di riepilogo di una fascia; {0} è la fase, {1} e {2} gli orari.</summary>
    public required string TonightSegmentLine { get; init; }

    /// <summary>Fase: giorno.</summary>
    public required string TonightPhaseDay { get; init; }

    /// <summary>Fase: crepuscolo civile.</summary>
    public required string TonightPhaseCivil { get; init; }

    /// <summary>Fase: crepuscolo nautico.</summary>
    public required string TonightPhaseNautical { get; init; }

    /// <summary>Fase: crepuscolo astronomico.</summary>
    public required string TonightPhaseAstro { get; init; }

    /// <summary>Fase: notte astronomica.</summary>
    public required string TonightPhaseNight { get; init; }

    /// <summary>Titolo della carta "Condizioni".</summary>
    public required string TonightConditionsTitle { get; init; }

    /// <summary>Nome del glifo lunare; {0} è la fase, {1} la percentuale illuminata.</summary>
    public required string TonightMoonGlyphName { get; init; }

    /// <summary>Fase lunare: luna nuova.</summary>
    public required string TonightMoonNew { get; init; }

    /// <summary>Fase lunare: falce crescente.</summary>
    public required string TonightMoonWaxingCrescent { get; init; }

    /// <summary>Fase lunare: primo quarto.</summary>
    public required string TonightMoonFirstQuarter { get; init; }

    /// <summary>Fase lunare: gibbosa crescente.</summary>
    public required string TonightMoonWaxingGibbous { get; init; }

    /// <summary>Fase lunare: luna piena.</summary>
    public required string TonightMoonFull { get; init; }

    /// <summary>Fase lunare: gibbosa calante.</summary>
    public required string TonightMoonWaningGibbous { get; init; }

    /// <summary>Fase lunare: ultimo quarto.</summary>
    public required string TonightMoonLastQuarter { get; init; }

    /// <summary>Fase lunare: falce calante.</summary>
    public required string TonightMoonWaningCrescent { get; init; }

    /// <summary>Etichetta dell'altezza della Luna.</summary>
    public required string TonightMoonAltitude { get; init; }

    /// <summary>Etichetta dell'altezza del Sole.</summary>
    public required string TonightSunAltitude { get; init; }

    /// <summary>Etichetta dell'indice di cielo sereno.</summary>
    public required string TonightSkyClarity { get; init; }

    /// <summary>Valore dell'indice di cielo sereno finché non è disponibile.</summary>
    public required string TonightSkyClarityPending { get; init; }

    /// <summary>Etichetta del meteo.</summary>
    public required string TonightWeather { get; init; }

    /// <summary>Valore del meteo finché non è disponibile.</summary>
    public required string TonightWeatherPending { get; init; }

    /// <summary>Titolo della carta "Prossimi eventi".</summary>
    public required string TonightEventsTitle { get; init; }

    /// <summary>Etichetta del prossimo cambio di fase.</summary>
    public required string TonightNextChange { get; init; }

    /// <summary>Etichetta del prossimo transito.</summary>
    public required string TonightNextTransit { get; init; }

    /// <summary>Segnaposto quando non c'è un transito previsto.</summary>
    public required string TonightNoTransit { get; init; }

    /// <summary>Spiegazione finché il calcolo dei transiti non è attivo.</summary>
    public required string TonightTransitPending { get; init; }

    /// <summary>Etichetta dell'ultima meteora.</summary>
    public required string TonightLastMeteor { get; init; }

    /// <summary>Segnaposto quando non ci sono meteore registrate.</summary>
    public required string TonightNoMeteor { get; init; }

    /// <summary>Spiegazione finché il rilevamento delle meteore non è attivo.</summary>
    public required string TonightMeteorPending { get; init; }

    /// <summary>Titolo della carta "Stato della cattura".</summary>
    public required string TonightCaptureTitle { get; init; }

    /// <summary>Conto alla rovescia con preposizione; {0} è la durata.</summary>
    public required string TonightCountdownIn { get; init; }

    /// <summary>Durata in minuti; {0} sono i minuti.</summary>
    public required string TonightCountdownMinutes { get; init; }

    /// <summary>Durata in ore e minuti; {0} sono le ore, {1} i minuti.</summary>
    public required string TonightCountdownHoursMinutes { get; init; }

    /// <summary>Durata inferiore al minuto.</summary>
    public required string TonightCountdownLessThanMinute { get; init; }

    /// <summary>Parola per un istante che coincide con l'ora corrente.</summary>
    public required string TonightNow { get; init; }

    /// <summary>Età di un dato; {0} è la durata.</summary>
    public required string TonightAgo { get; init; }

    /// <summary>Titolo della carta dei prossimi passaggi.</summary>
    public required string PassListTitle { get; init; }

    /// <summary>Messaggio quando la previsione è spenta.</summary>
    public required string PassStatusDisabled { get; init; }

    /// <summary>Messaggio quando la previsione non ha ancora dati.</summary>
    public required string PassStatusWaiting { get; init; }

    /// <summary>Messaggio di blocco; {0} è la spiegazione tecnica.</summary>
    public required string PassStatusBlocked { get; init; }

    /// <summary>Messaggio di errore; {0} è la spiegazione tecnica.</summary>
    public required string PassStatusFailed { get; init; }

    /// <summary>Messaggio quando la previsione è valida ma vuota.</summary>
    public required string PassNoneInWindow { get; init; }

    /// <summary>Stato di un passaggio in corso.</summary>
    public required string PassInProgress { get; init; }

    /// <summary>Orari di inizio, culminazione e fine; segnaposto in ordine.</summary>
    public required string PassTimes { get; init; }

    /// <summary>Altezza massima; {0} è il valore già formattato.</summary>
    public required string PassMaxElevation { get; init; }

    /// <summary>Direzione del passaggio; {0} inizio, {1} fine.</summary>
    public required string PassDirection { get; init; }

    /// <summary>Punto cardinale nord.</summary>
    public required string PassCompassN { get; init; }

    /// <summary>Punto cardinale nord-est.</summary>
    public required string PassCompassNE { get; init; }

    /// <summary>Punto cardinale est.</summary>
    public required string PassCompassE { get; init; }

    /// <summary>Punto cardinale sud-est.</summary>
    public required string PassCompassSE { get; init; }

    /// <summary>Punto cardinale sud.</summary>
    public required string PassCompassS { get; init; }

    /// <summary>Punto cardinale sud-ovest.</summary>
    public required string PassCompassSW { get; init; }

    /// <summary>Punto cardinale ovest.</summary>
    public required string PassCompassW { get; init; }

    /// <summary>Punto cardinale nord-ovest.</summary>
    public required string PassCompassNW { get; init; }

    /// <summary>Condizione: satellite illuminato e cielo buio.</summary>
    public required string PassLightVisible { get; init; }

    /// <summary>Condizione: satellite illuminato ma cielo non buio.</summary>
    public required string PassLightLit { get; init; }

    /// <summary>Condizione: satellite in ombra.</summary>
    public required string PassLightShadow { get; init; }

    /// <summary>Motivo: tracce non disponibili perché la previsione è spenta.</summary>
    public required string PassTrackServiceOff { get; init; }

    /// <summary>Motivo: tracce non disponibili senza calibrazione.</summary>
    public required string PassTrackNoCalibration { get; init; }

    /// <summary>Messaggio quando non ci sono tracce da mostrare.</summary>
    public required string PassTrackNoPasses { get; init; }

    /// <summary>Etichetta della voce della barra di stato.</summary>
    public required string PassStatusLabel { get; init; }

    /// <summary>Valore della barra di stato: nome del satellite e conto alla rovescia.</summary>
    public required string PassNextValue { get; init; }

    /// <summary>Valore della barra di stato per un passaggio in corso; {0} è il nome.</summary>
    public required string PassInProgressValue { get; init; }

    /// <summary>Riga: account collegato.</summary>
    public required string YouTubeConnected { get; init; }

    /// <summary>Riga: account non collegato.</summary>
    public required string YouTubeNotConnected { get; init; }

    /// <summary>Riga: caricamento automatico attivo.</summary>
    public required string YouTubeAutomaticOn { get; init; }

    /// <summary>Riga: caricamento automatico spento.</summary>
    public required string YouTubeAutomaticOff { get; init; }

    /// <summary>Prossimo caricamento; {0} è la data e l'ora locali.</summary>
    public required string YouTubeNextUpload { get; init; }

    /// <summary>Nessun caricamento programmato.</summary>
    public required string YouTubeNoNextUpload { get; init; }

    /// <summary>Ultimo caricamento; {0} è la notte, {1} la data di registrazione.</summary>
    public required string YouTubeLastUpload { get; init; }

    /// <summary>Nessun caricamento completato.</summary>
    public required string YouTubeNoUploadYet { get; init; }

    /// <summary>Pulsante: apre il video nel browser.</summary>
    public required string YouTubeOpenVideo { get; init; }

    /// <summary>Riga del problema; {0} è il testo del problema.</summary>
    public required string YouTubeLastProblem { get; init; }

    /// <summary>Problema: accesso non valido.</summary>
    public required string YouTubeProblemAuthRequired { get; init; }

    /// <summary>Problema: autorizzazione negata.</summary>
    public required string YouTubeProblemLoginDenied { get; init; }

    /// <summary>Problema: nessuna risposta dal browser.</summary>
    public required string YouTubeProblemLoginTimedOut { get; init; }

    /// <summary>Problema: credenziali mancanti.</summary>
    public required string YouTubeProblemCredentialsMissing { get; init; }

    /// <summary>Problema: collegamento non riuscito.</summary>
    public required string YouTubeProblemLoginFailed { get; init; }

    /// <summary>Problema: protezione dei segreti assente.</summary>
    public required string YouTubeProblemSecretUnavailable { get; init; }

    /// <summary>Problema: quota esaurita.</summary>
    public required string YouTubeProblemQuota { get; init; }

    /// <summary>Problema: caricamento permanentemente fallito.</summary>
    public required string YouTubeProblemUploadFailed { get; init; }

    /// <summary>Problema: esito incerto.</summary>
    public required string YouTubeProblemNeedsReview { get; init; }

    /// <summary>Problema: errore temporaneo.</summary>
    public required string YouTubeProblemRetry { get; init; }

    /// <summary>Problema: impostazioni non valide.</summary>
    public required string YouTubeProblemSettingsInvalid { get; init; }

    /// <summary>Problema: credenziali sì, impostazioni no.</summary>
    public required string YouTubeProblemSettingsNotSaved { get; init; }

    /// <summary>Problema: caricamento YouTube non disponibile.</summary>
    public required string YouTubeProblemUnavailable { get; init; }

    /// <summary>Esito: video caricato.</summary>
    public required string YouTubeOutcomeCompleted { get; init; }

    /// <summary>Esito: notte già caricata.</summary>
    public required string YouTubeOutcomeAlreadyUploaded { get; init; }

    /// <summary>Esito: in corso o in attesa.</summary>
    public required string YouTubeOutcomeInProgress { get; init; }

    /// <summary>Esito: nessuna notte pronta.</summary>
    public required string YouTubeOutcomeNoNight { get; init; }

    /// <summary>Esito: limite giornaliero.</summary>
    public required string YouTubeOutcomeDailyLimit { get; init; }

    /// <summary>Esito: non ancora dovuto.</summary>
    public required string YouTubeOutcomeNotDue { get; init; }

    /// <summary>Esito: ora saltata.</summary>
    public required string YouTubeOutcomeSlotMissed { get; init; }

    /// <summary>Errore di campo: ora.</summary>
    public required string YouTubeProblemDailyTime { get; init; }

    /// <summary>Errore di campo: privacy.</summary>
    public required string YouTubeProblemPrivacy { get; init; }

    /// <summary>Errore di campo: tag vuoto.</summary>
    public required string YouTubeProblemTag { get; init; }

    /// <summary>Errore di campo: categoria.</summary>
    public required string YouTubeProblemCategory { get; init; }

    /// <summary>Salvataggio non riuscito.</summary>
    public required string YouTubeSaveFailed { get; init; }

    /// <summary>Nome della privacy: privato.</summary>
    public required string YouTubePrivacyPrivate { get; init; }

    /// <summary>Nome della privacy: non in elenco.</summary>
    public required string YouTubePrivacyUnlisted { get; init; }

    /// <summary>Nome della privacy: pubblico.</summary>
    public required string YouTubePrivacyPublic { get; init; }

    /// <summary>Titolo della sezione account.</summary>
    public required string YouTubeAccountTitle { get; init; }

    /// <summary>Etichetta del campo client ID.</summary>
    public required string YouTubeClientIdLabel { get; init; }

    /// <summary>Etichetta del campo client secret.</summary>
    public required string YouTubeClientSecretLabel { get; init; }

    /// <summary>Nota sul client secret: non viene mai riletto.</summary>
    public required string YouTubeSecretNote { get; init; }

    /// <summary>Pulsante: collega l'account.</summary>
    public required string YouTubeLogin { get; init; }

    /// <summary>Pulsante: annulla il collegamento in corso.</summary>
    public required string YouTubeLoginCancel { get; init; }

    /// <summary>Messaggio durante il collegamento.</summary>
    public required string YouTubeLoginWaiting { get; init; }

    /// <summary>Pulsante: scollega l'account.</summary>
    public required string YouTubeLogout { get; init; }

    /// <summary>Esito: collegamento riuscito.</summary>
    public required string YouTubeLoginDone { get; init; }

    /// <summary>Esito: scollegamento riuscito.</summary>
    public required string YouTubeLogoutDone { get; init; }

    /// <summary>Avviso sullo stato Testing di Google (scadenza 7 giorni).</summary>
    public required string YouTubeTestingWarning { get; init; }

    /// <summary>Titolo della sezione impostazioni YouTube.</summary>
    public required string YouTubeSettingsTitle { get; init; }

    /// <summary>Etichetta dell'ora di caricamento.</summary>
    public required string YouTubeDailyTimeLabel { get; init; }

    /// <summary>Etichetta della privacy.</summary>
    public required string YouTubePrivacyLabel { get; init; }

    /// <summary>Etichetta dei tag.</summary>
    public required string YouTubeTagsLabel { get; init; }

    /// <summary>Etichetta dell'ID categoria.</summary>
    public required string YouTubeCategoryLabel { get; init; }

    /// <summary>Suggerimento sull'ID categoria.</summary>
    public required string YouTubeCategoryHint { get; init; }

    /// <summary>Pulsante: salva le impostazioni YouTube.</summary>
    public required string YouTubeSave { get; init; }

    /// <summary>Esito: impostazioni salvate.</summary>
    public required string YouTubeSaved { get; init; }

    /// <summary>Titolo della sezione caricamento.</summary>
    public required string YouTubeUploadTitle { get; init; }

    /// <summary>Pulsante: carica la notte più recente completata.</summary>
    public required string YouTubeUploadNow { get; init; }

    /// <summary>Conferma prima del caricamento manuale.</summary>
    public required string YouTubeUploadConfirm { get; init; }

    /// <summary>Seconda conferma: rischio di duplicato.</summary>
    public required string YouTubeUploadConfirmRisk { get; init; }

    /// <summary>Pulsante di conferma.</summary>
    public required string YouTubeConfirmButton { get; init; }

    /// <summary>Pulsante di annullamento.</summary>
    public required string YouTubeCancelButton { get; init; }

    /// <summary>Messaggio durante il caricamento.</summary>
    public required string YouTubeUploadRunning { get; init; }
}
