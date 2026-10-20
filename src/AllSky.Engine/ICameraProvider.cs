using AllSky.Core;

namespace AllSky.Engine;

/// <summary>Candidato trovato dalla ricerca delle camere.</summary>
/// <param name="Id">Identificativo stabile, usato in config.camera.id.</param>
/// <param name="Name">Nome leggibile.</param>
/// <param name="Kind">Tipo: "sim", "ascom-com" o "ascom-alpaca".</param>
/// <param name="Detail">ProgID o host:porta.</param>
public sealed record CameraCandidate(string Id, string Name, string Kind, string Detail);

/// <summary>
/// Fornitore di camere iniettato nell'Engine. Sostituisce simulatore, ASCOM e Alpaca senza che l'Engine li conosca.
/// </summary>
public interface ICameraProvider
{
    /// <summary>Elenca le camere disponibili.</summary>
    Task<IReadOnlyList<CameraCandidate>> DiscoverAsync(CancellationToken cancellationToken);

    /// <summary>Crea la sorgente per l'identificativo indicato, senza connetterla. Lancia un'eccezione se l'id non e' noto.</summary>
    ICameraSource Create(string id);
}
