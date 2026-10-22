using AllSky.Capture;
using AllSky.Core;
using AllSky.Publish;
using AllSky.Satellites;
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
/// <param name="Publisher">
/// Publisher della pubblicazione periodica, facoltativo. Ogni frame elaborato gli viene offerto dopo l'archiviazione.
/// Il ciclo di vita (avvio e arresto) non e' dell'Engine: lo gestisce l'Host. Null = nessuna pubblicazione.
/// </param>
/// <param name="FrameOverlayFactory">
/// Crea la maschera informativa per un job di timelapse, con la configurazione in quel momento. Chiamata solo se
/// <c>overlay.applyToTimelapse</c> e' vero. Null = nessuna maschera (il timelapse usa i frame puliti).
/// </param>
/// <param name="TleFetcher">
/// Sorgente dei TLE per la previsione dei passaggi (P4.3). Null = previsione non disponibile. Il Desktop passa la sorgente CelesTrak.
/// </param>
/// <param name="OrbitPropagators">Crea i propagatori SGP4 dai TLE. Null = previsione non disponibile. Il Desktop passa SGP.NET.</param>
public sealed record EngineDependencies(
    IClock Clock,
    ICameraProvider CameraProvider,
    Func<AppConfig, IFrameProcessor> ProcessorFactory,
    ITimelapseBuilder TimelapseBuilder,
    ICaptureDelay CaptureDelay,
    IJpegEncoder JpegEncoder,
    string ConfigPath,
    Action<string, Exception?>? Log = null,
    Func<string?>? LocateFfmpeg = null,
    IPublisher? Publisher = null,
    Func<AppConfig, IFrameOverlay?>? FrameOverlayFactory = null,
    ITleFetcher? TleFetcher = null,
    IOrbitPropagatorFactory? OrbitPropagators = null);
