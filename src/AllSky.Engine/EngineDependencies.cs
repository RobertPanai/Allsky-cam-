using AllSky.Capture;
using AllSky.Core;
using AllSky.Timelapse;

namespace AllSky.Engine;

/// <summary>Dipendenze iniettate in <see cref="AllSkyEngine"/>.</summary>
/// <param name="Clock">Orologio: di sistema nell'applicazione, finto nei test.</param>
/// <param name="CameraProvider">Fornitore delle camere.</param>
/// <param name="ProcessorFactory">Crea un <see cref="IFrameProcessor"/> per una configurazione; chiamata a ogni connessione.</param>
/// <param name="TimelapseBuilder">Costruttore dei timelapse MP4.</param>
/// <param name="CaptureDelay">Attesa usata dal ciclo di cattura.</param>
/// <param name="JpegEncoder">Codificatore JPEG per keogram e startrails.</param>
/// <param name="ConfigPath">File JSON della configurazione.</param>
/// <param name="Log">Logger: messaggio e, se presente, eccezione. Null = nessun log.</param>
/// <param name="LocateFfmpeg">Percorso di ffmpeg; null usa <see cref="FfmpegLocator.Locate(string?, string?, string?)"/>.</param>
public sealed record EngineDependencies(
    IClock Clock,
    ICameraProvider CameraProvider,
    Func<AppConfig, IFrameProcessor> ProcessorFactory,
    ITimelapseBuilder TimelapseBuilder,
    ICaptureDelay CaptureDelay,
    IJpegEncoder JpegEncoder,
    string ConfigPath,
    Action<string, Exception?>? Log = null,
    Func<string?>? LocateFfmpeg = null);
