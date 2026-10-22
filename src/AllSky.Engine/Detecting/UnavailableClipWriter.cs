using AllSky.Events;

namespace AllSky.Engine.Detecting;

/// <summary>
/// Scrittore di clip usato quando ffmpeg non è configurato: ogni richiesta fallisce con un motivo leggibile.
/// L'evento viene comunque salvato senza clip, e il motivo va nel log (<c>ClipError</c>).
/// </summary>
internal sealed class UnavailableClipWriter : IClipWriter
{
    private readonly string _reason;

    public UnavailableClipWriter(string reason)
    {
        _reason = reason;
    }

    public Task WriteAsync(IReadOnlyList<string> jpegPaths, string outputMp4, int fps, CancellationToken ct)
    {
        return Task.FromException(new InvalidOperationException(_reason));
    }
}
