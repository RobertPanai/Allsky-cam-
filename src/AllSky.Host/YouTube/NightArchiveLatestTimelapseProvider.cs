using System.Globalization;
using AllSky.Archive;
using AllSky.Astro;
using AllSky.Core;
using AllSky.YouTube;

namespace AllSky.Host;

/// <summary>
/// Fonte del timelapse per il caricamento automatico: l'ultima notte COMPLETA con <c>timelapse.mp4</c> pronto.
/// La notte in corso e' esclusa con la regola di <see cref="ObservingNight"/> (mezzogiorno locale), come nell'archivio.
/// Legge l'archivio ad ogni chiamata, quindi vede i timelapse appena generati.
/// </summary>
/// <remarks>
/// Meteore e transiti sono sempre 0: il registro degli eventi non e' ancora collegato all'Host. Di conseguenza
/// {meteore} e {transiti} nella descrizione mostrano 0.
/// </remarks>
public sealed class NightArchiveLatestTimelapseProvider : ILatestTimelapseProvider
{
    private const string NightIdFormat = "yyyy-MM-dd";

    private readonly Func<AppConfig> _config;
    private readonly AllSky.Core.IClock _clock;

    /// <param name="config">Configurazione corrente (cartella dell'archivio, fuso orario, fps del timelapse).</param>
    /// <param name="clock">Orologio per riconoscere la notte in corso.</param>
    public NightArchiveLatestTimelapseProvider(Func<AppConfig> config, AllSky.Core.IClock clock)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public LatestTimelapse? GetLatestCompleted()
    {
        AppConfig config = _config();
        TimeZoneInfo zone = YouTubeConfigMapper.ResolveZone(config.Site.Timezone);
        string folder = string.IsNullOrWhiteSpace(config.Storage.Folder)
            ? new AppConfig.StorageConfig().Folder
            : config.Storage.Folder;

        var archive = new NightArchive(folder, _clock, zone);
        string current = ObservingNight.GetNightId(_clock.UtcNow, zone);

        NightInfo? night = archive.ListNights()
            .Where(n => n.TimelapseState == TimelapseState.Ready && string.CompareOrdinal(n.Id, current) < 0)
            .OrderByDescending(n => n.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (night is null)
        {
            return null;
        }

        string path = archive.GetProductPath(night.Id, NightProduct.Timelapse);
        if (!File.Exists(path))
        {
            return null;
        }

        int fps = Math.Max(1, config.Products.TimelapseFps);
        DateTime nightStart = DateOnly.ParseExact(night.Id, NightIdFormat, CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
        return new LatestTimelapse(
            night.Id,
            nightStart,
            path,
            night.Frames,
            TimeSpan.FromSeconds(night.Frames / (double)fps),
            Meteors: 0,
            Transits: 0);
    }
}
