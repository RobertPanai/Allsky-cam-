using AllSky.Archive;
using AllSky.Core;

namespace AllSky.Engine.Detecting;

/// <summary>
/// Un frame archiviato e pronto per il rilevamento. Creato sul thread di cattura, elaborato in background.
/// <see cref="FullRgb"/> è il frame elaborato a piena risoluzione, senza overlay: serve solo se il frame contiene scie
/// candidate. <see cref="ReducedRgb"/> è il frame ridotto, usato per il rilevamento e per i fotogrammi di transito.
/// </summary>
internal sealed record DetectionJob(
    DateTimeOffset TimestampUtc,
    string FrameId,
    string NightId,
    string Phase,
    int FullWidth,
    int FullHeight,
    byte[] FullRgb,
    int DetWidth,
    int DetHeight,
    byte[] ReducedRgb,
    string[] WindowFrameIds,
    AppConfig Config,
    NightArchive Archive,
    TimeZoneInfo TimeZone);

/// <summary>Ultimi cinque identificativi di frame archiviati: la finestra [N−2, N+2] della clip. Usato solo dal thread di cattura.</summary>
internal sealed class RecentFrameIds
{
    private const int Size = 5;
    private readonly string[] _ids = new string[Size];
    private int _count;

    public void Add(string frameId)
    {
        _ids[_count % Size] = frameId;
        _count++;
    }

    /// <summary>Identificativi dal più vecchio al più recente.</summary>
    public string[] Snapshot()
    {
        int n = Math.Min(_count, Size);
        var result = new string[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = _ids[(_count - n + i) % Size];
        }

        return result;
    }
}
