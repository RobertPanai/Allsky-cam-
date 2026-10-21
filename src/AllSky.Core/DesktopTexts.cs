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
