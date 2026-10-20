using AllSky.Archive;
using AllSky.Astro;
using AllSky.Core;

namespace AllSky.Engine;

/// <summary>Notti e frame dell'archivio: elenchi, percorsi e cancellazione.</summary>
public sealed partial class AllSkyEngine
{
    private const long NightSizeUnit = 1024L * 1024L;

    /// <inheritdoc />
    public IReadOnlyList<NightDto> ListNights()
    {
        NightArchive archive = ArchiveOf();
        AppConfig config;
        TimeZoneInfo tz;
        HashSet<string> queued;
        lock (_gate)
        {
            config = _config;
            tz = _tz;
            queued = _jobs.Select(j => j.NightId).ToHashSet(StringComparer.Ordinal);
        }

        var result = new List<NightDto>();
        foreach (NightInfo night in archive.ListNights())
        {
            string timelapse = queued.Contains(night.Id) ? "generating" : TimelapseWire(night.TimelapseState);
            result.Add(new NightDto(
                night.Id,
                night.Frames,
                night.SizeBytes / NightSizeUnit,
                night.FirstFrame,
                night.LastFrame,
                night.HasKeogram,
                night.HasStartrails,
                timelapse,
                SunBands(night.Id, config, tz)));
        }

        return result;
    }

    /// <inheritdoc />
    public IReadOnlyList<FrameDto> ListFrames(string nightId, int offset, int limit)
    {
        return ArchiveOf()
            .ListFrames(nightId, offset, limit)
            .Select(f => new FrameDto(f.Id, f.Timestamp, f.Phase))
            .ToList();
    }

    /// <inheritdoc />
    public string GetFramePath(string nightId, string frameId) => ArchiveOf().GetFramePath(nightId, frameId);

    /// <inheritdoc />
    public string GetThumbPath(string nightId, string frameId) => ArchiveOf().GetThumbPath(nightId, frameId);

    /// <inheritdoc />
    public string GetProductPath(string nightId, NightProduct kind) => ArchiveOf().GetProductPath(nightId, kind);

    /// <inheritdoc />
    public bool DeleteNight(string nightId)
    {
        lock (_gate)
        {
            if (_jobs.Any(j => j.NightId == nightId))
            {
                throw new EngineException("Il timelapse di questa notte è in lavorazione: attendere la fine prima di cancellarla.");
            }
        }

        try
        {
            bool deleted = ArchiveOf().DeleteNight(nightId);
            RaiseStatus();
            return deleted;
        }
        catch (InvalidOperationException ex)
        {
            throw new EngineException(
                $"{ex.Message} Scegliere un'altra notte da cancellare.",
                ex);
        }
    }

    private static IReadOnlyList<SunBandDto> SunBands(string nightId, AppConfig config, TimeZoneInfo tz)
    {
        try
        {
            return ObservingNight.GetBands(nightId, config.Site.Latitude, config.Site.Longitude, tz)
                .Select(b => new SunBandDto(SkyPhases.Simplify(b.Phase), b.From, b.To))
                .ToList();
        }
        catch (ArgumentException)
        {
            return Array.Empty<SunBandDto>();
        }
    }

    private static string TimelapseWire(TimelapseState state)
    {
        return state switch
        {
            TimelapseState.Ready => "ready",
            TimelapseState.Generating => "generating",
            TimelapseState.Failed => "failed",
            _ => "none",
        };
    }
}
