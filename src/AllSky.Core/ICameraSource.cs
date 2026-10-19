namespace AllSky.Core;

/// <summary>Sorgente di frame grezzi (simulatore, ASCOM COM, ASCOM Alpaca, ...).</summary>
public interface ICameraSource : IAsyncDisposable
{
    string Id { get; }

    string Name { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken cancellationToken);

    CameraInfo Info { get; }
}
