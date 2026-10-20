using AllSky.Core;

namespace AllSky.Engine;

/// <summary>
/// Stato completo dell'applicazione: corrisponde a <c>GET /api/status</c> di docs/API.md.
/// I nomi sono PascalCase; la serializzazione camelCase e' compito dell'Host.
/// </summary>
/// <param name="Version">Versione dell'applicazione.</param>
/// <param name="State">"idle", "capturing" oppure "error".</param>
/// <param name="Phase">"day", "twilight" o "night"; null se la posizione del sito non e' valida.</param>
/// <param name="SunAltitudeDeg">Altezza del Sole in gradi; null se non calcolabile.</param>
/// <param name="MoonAltitudeDeg">Altezza della Luna in gradi; null se non calcolabile.</param>
/// <param name="Camera">Camera configurata o collegata. Sempre presente, anche senza connessione.</param>
/// <param name="LastFrame">Ultimo frame archiviato; null se non ce n'e' ancora.</param>
/// <param name="Disk">Uso del disco, aggiornato al massimo ogni 60 secondi; null se non leggibile.</param>
/// <param name="UptimeS">Secondi dall'avvio del servizio.</param>
/// <param name="Error">Messaggio in italiano che dice cosa fare; null se non ci sono errori.</param>
/// <param name="Jobs">Lavori in coda o in corso (timelapse).</param>
public sealed record StatusDto(
    string Version,
    string State,
    string? Phase,
    double? SunAltitudeDeg,
    double? MoonAltitudeDeg,
    CameraStatusDto Camera,
    LastFrameDto? LastFrame,
    DiskDto? Disk,
    long UptimeS,
    string? Error,
    IReadOnlyList<JobDto> Jobs);

/// <summary>Camera di sistema: identificativo, nome, tipo e stato di connessione.</summary>
/// <param name="Id">Identificativo della camera (config.camera.id).</param>
/// <param name="Name">Nome riportato dalla sorgente; vuoto se non collegata.</param>
/// <param name="Kind">Tipo: "sim", "ascom-com", "ascom-alpaca" o "sconosciuto".</param>
/// <param name="Connected">Vero se la sorgente e' stata connessa con successo.</param>
public sealed record CameraStatusDto(string Id, string Name, string Kind, bool Connected);

/// <summary>Ultimo frame archiviato con i dati di esposizione.</summary>
/// <param name="Id">Identificativo del frame, formato yyyyMMddTHHmmssZ (UTC).</param>
/// <param name="Timestamp">Istante di cattura in UTC.</param>
/// <param name="ExposureMs">Esposizione in millisecondi.</param>
/// <param name="Gain">Gain della camera.</param>
/// <param name="MedianAdu">Mediana ADU del frame grezzo.</param>
public sealed record LastFrameDto(string Id, DateTimeOffset Timestamp, double ExposureMs, double Gain, int MedianAdu);

/// <summary>Uso del disco in GB (1 GB = 1024^3 byte).</summary>
/// <param name="FreeGb">Spazio libero sul volume dell'archivio; null se non leggibile.</param>
/// <param name="UsedGb">Dimensione della cartella dell'archivio.</param>
public sealed record DiskDto(double? FreeGb, double UsedGb);

/// <summary>Lavoro in coda o in corso.</summary>
/// <param name="Kind">Tipo di lavoro: "timelapse".</param>
/// <param name="NightId">Notte a cui si riferisce, formato YYYY-MM-DD.</param>
/// <param name="Progress">Avanzamento da 0 a 1 (0 se in coda).</param>
public sealed record JobDto(string Kind, string NightId, double Progress);

/// <summary>Evento emesso per ogni frame archiviato.</summary>
/// <param name="Id">Identificativo del frame.</param>
/// <param name="Timestamp">Istante di cattura in UTC.</param>
public sealed record FrameEvent(string Id, DateTimeOffset Timestamp);

/// <summary>
/// Notte osservativa (da mezzogiorno a mezzogiorno locale), corrisponde a un elemento di <c>GET /api/nights</c>.
/// </summary>
/// <param name="Id">Data di inizio della notte, YYYY-MM-DD.</param>
/// <param name="Frames">Numero di frame archiviati.</param>
/// <param name="SizeMb">Dimensione su disco in MB, arrotondata per difetto.</param>
/// <param name="FirstFrame">Primo frame in UTC, null se la notte e' vuota.</param>
/// <param name="LastFrame">Ultimo frame in UTC, null se la notte e' vuota.</param>
/// <param name="Keogram">Vero se keogram.jpg esiste (anche parziale).</param>
/// <param name="Startrails">Vero se startrails.jpg esiste (anche parziale).</param>
/// <param name="Timelapse">"ready", "generating", "none" o "failed". "generating" anche se in coda.</param>
/// <param name="SunBands">Fasi del cielo della notte, con campionamento di 1 minuto.</param>
public sealed record NightDto(
    string Id,
    int Frames,
    long SizeMb,
    DateTimeOffset? FirstFrame,
    DateTimeOffset? LastFrame,
    bool Keogram,
    bool Startrails,
    string Timelapse,
    IReadOnlyList<SunBandDto> SunBands);

/// <summary>Intervallo continuo di una fase del cielo.</summary>
/// <param name="Phase">"day", "twilight" o "night".</param>
/// <param name="From">Inizio in UTC.</param>
/// <param name="To">Fine in UTC.</param>
public sealed record SunBandDto(string Phase, DateTimeOffset From, DateTimeOffset To);

/// <summary>Riga di <c>GET /api/nights/{id}/frames</c>.</summary>
/// <param name="Id">Identificativo del frame.</param>
/// <param name="Timestamp">Istante di cattura in UTC.</param>
/// <param name="Phase">"day", "twilight" o "night".</param>
public sealed record FrameDto(string Id, DateTimeOffset Timestamp, string Phase);

/// <summary>Esito di <c>POST /api/camera/connect</c>.</summary>
/// <param name="Ok">Vero se la camera e' connessa e la cattura e' partita.</param>
/// <param name="Error">Messaggio in italiano se <paramref name="Ok"/> e' falso; null altrimenti.</param>
public sealed record ConnectResult(bool Ok, string? Error);

/// <summary>Esito di <c>PUT /api/config</c>. Con errori la configurazione non e' stata cambiata.</summary>
/// <param name="Ok">Vero se la configurazione e' valida, salvata e in applicazione.</param>
/// <param name="Errors">Errori di validazione con percorso JSON e messaggio; vuoto se <paramref name="Ok"/> e' vero.</param>
public sealed record ConfigUpdateResult(bool Ok, IReadOnlyList<ValidationError> Errors);
