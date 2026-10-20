using AllSky.Archive;
using AllSky.Core;

namespace AllSky.Engine;

/// <summary>
/// Servizio applicativo esposto dall'Host HTTP. Ogni membro corrisponde a un endpoint di docs/API.md.
/// Errori attesi: <see cref="EngineException"/> con messaggio in italiano. Argomenti non validi: <see cref="ArgumentException"/>.
/// </summary>
public interface IAllSkyService
{
    /// <summary>Stato corrente (<c>GET /api/status</c>).</summary>
    StatusDto GetStatus();

    /// <summary>Emesso a ogni cambio di stato, errore, frame, lavoro o configurazione applicata.</summary>
    event Action<StatusDto>? StatusChanged;

    /// <summary>Emesso per ogni frame elaborato e archiviato.</summary>
    event Action<FrameEvent>? FrameProduced;

    /// <summary>
    /// Ultimo JPEG elaborato (<paramref name="raw"/> = false) oppure la versione grezza, ricalcolata sull'ultimo frame
    /// (<paramref name="raw"/> = true). Null se non e' ancora stato prodotto alcun frame.
    /// </summary>
    byte[]? GetLatestJpeg(bool raw);

    /// <summary>Elenca le camere disponibili. Lancia <see cref="EngineException"/> se la ricerca fallisce.</summary>
    Task<IReadOnlyList<CameraCandidate>> DiscoverCamerasAsync(CancellationToken cancellationToken);

    /// <summary>Connette la camera e avvia la cattura. Non lancia per errori di connessione: li riporta nel risultato.</summary>
    Task<ConnectResult> ConnectCameraAsync(string id, CancellationToken cancellationToken);

    /// <summary>Configurazione corrente (<c>GET /api/config</c>).</summary>
    AppConfig GetConfig();

    /// <summary>Valida, salva e applica la configurazione (<c>PUT /api/config</c>). Con errori non cambia nulla.</summary>
    ConfigUpdateResult UpdateConfig(AppConfig config);

    /// <summary>Notti presenti, dalla piu' recente (<c>GET /api/nights</c>).</summary>
    IReadOnlyList<NightDto> ListNights();

    /// <summary>Frame di una notte in ordine di salvataggio (<c>GET /api/nights/{id}/frames</c>).</summary>
    IReadOnlyList<FrameDto> ListFrames(string nightId, int offset, int limit);

    /// <summary>Percorso del JPEG di un frame. Il file puo' non esistere: il chiamante lo verifica.</summary>
    string GetFramePath(string nightId, string frameId);

    /// <summary>Percorso della miniatura di un frame. Il file puo' non esistere.</summary>
    string GetThumbPath(string nightId, string frameId);

    /// <summary>Percorso di un prodotto della notte (keogram, startrails, timelapse). Il file puo' non esistere.</summary>
    string GetProductPath(string nightId, NightProduct kind);

    /// <summary>
    /// Accoda il timelapse della notte (<c>POST /api/nights/{id}/timelapse</c>). Restituisce false se la notte non
    /// ha frame, se il timelapse e' gia' in coda o in corso, o se il servizio non e' avviato.
    /// </summary>
    Task<bool> RequestTimelapseAsync(string nightId);

    /// <summary>
    /// Cancella una notte (<c>DELETE /api/nights/{id}</c>). Restituisce false se la notte non esiste.
    /// Lancia <see cref="EngineException"/> per la notte in corso o con timelapse in lavorazione.
    /// </summary>
    bool DeleteNight(string nightId);
}
