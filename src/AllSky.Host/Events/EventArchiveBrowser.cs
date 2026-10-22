using AllSky.Core.Events;
using AllSky.Events;
using AllSky.Transits;

namespace AllSky.Host.Events;

/// <summary>
/// Adattatore di <see cref="IEventBrowser"/> per la vista Eventi del desktop: traduce <see cref="EventArchive"/> nei tipi
/// di AllSky.Core e risolve i file dell'evento in percorsi assoluti verificati.
/// </summary>
/// <remarks>
/// <para>
/// La radice è quella dell'archivio delle notti (<c>storage.folder</c>), come per <c>NightArchive</c>: la funzione
/// <paramref name="rootFolder"/> viene letta a ogni chiamata, così un cambio di cartella nella configurazione
/// viene seguito. L'istanza di <see cref="EventArchive"/> si ricrea solo quando la cartella cambia.
/// </para>
/// <para>
/// Lettura, etichetta ed esportazione passano dall'archivio, che ha il suo lock e le sue validazioni: l'adattatore non
/// tocca il disco da solo. Gli eventi scartati non compaiono nell'elenco.
/// </para>
/// </remarks>
public sealed class EventArchiveBrowser : IEventBrowser
{
    private readonly Func<string> _rootFolder;
    private readonly object _gate = new();
    private string? _archiveFolder;
    private EventArchive? _archive;

    /// <summary>Crea l'adattatore. Non apre file: l'archivio si crea alla prima chiamata.</summary>
    /// <param name="rootFolder">Radice dell'archivio delle notti, letta a ogni chiamata.</param>
    public EventArchiveBrowser(Func<string> rootFolder)
    {
        _rootFolder = rootFolder ?? throw new ArgumentNullException(nameof(rootFolder));
    }

    /// <inheritdoc/>
    public IReadOnlyList<EventListItem> List(EventFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        EventArchive archive = Archive();
        var query = new EventQuery
        {
            NightId = filter.NightId,
            Kind = filter.Category is null ? null : ToKind(filter.Category.Value),
        };

        return archive.List(query)
            .Select(record => ToListItem(archive, record))
            .ToList();
    }

    /// <inheritdoc/>
    public EventDetail? Get(string nightId, string id)
    {
        EventArchive archive = Archive();
        EventRecord? record = archive.Get(nightId, id);
        return record is null ? null : ToDetail(archive, record);
    }

    /// <inheritdoc/>
    public EventDetail SetLabel(string nightId, string id, EventLabelChoice label)
    {
        EventArchive archive = Archive();
        EventRecord updated = archive.SetLabel(nightId, id, ToUserLabel(label));
        return ToDetail(archive, updated);
    }

    /// <inheritdoc/>
    public string ExportZip(string nightId, string id, string zipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        return Archive().ExportZip(nightId, id, zipPath);
    }

    // ---- Traduzione ----

    private EventListItem ToListItem(EventArchive archive, EventRecord record)
    {
        EventCategory category = ToCategory(record.Kind);
        // Per un transito la miniatura è la composita dei frame; per le scie il ritaglio ingrandito.
        string? thumbnail = category == EventCategory.Transit
            ? ExistingFile(archive, record, record.Files.Composite) ?? ExistingFile(archive, record, record.Files.Frame)
            : ExistingFile(archive, record, record.Files.Crop) ?? ExistingFile(archive, record, record.Files.Frame);

        return new EventListItem(
            record.NightId,
            record.Id,
            category,
            record.TimestampUtc,
            record.Score,
            ToLabel(record.Label),
            thumbnail);
    }

    private EventDetail ToDetail(EventArchive archive, EventRecord record)
    {
        return new EventDetail(
            record.NightId,
            record.Id,
            ToCategory(record.Kind),
            record.TimestampUtc,
            ToClassKind(record.Class),
            record.Score,
            record.LengthPx,
            record.PeakLuma,
            record.IntegratedLuma,
            ToLabel(record.Label),
            record.StartAltAz is null ? null : new SkyPoint(record.StartAltAz.AltDeg, record.StartAltAz.AzDeg),
            record.EndAltAz is null ? null : new SkyPoint(record.EndAltAz.AltDeg, record.EndAltAz.AzDeg),
            ExistingFile(archive, record, record.Files.Crop),
            ExistingFile(archive, record, record.Files.Frame),
            ExistingFile(archive, record, record.Files.Clip),
            ExistingFile(archive, record, record.Files.Composite),
            record.TransitInfo is null ? null : ToTransit(record.TransitInfo));
    }

    private static TransitInfo ToTransit(TransitDetails info) => new(
        info.SatelliteName,
        info.NoradId,
        info.StartUtc,
        info.CulminationUtc,
        info.EndUtc,
        info.MaxElevationDeg,
        info.Score,
        info.Verdict switch
        {
            TransitVerdict.Identificato => TransitOutcome.Identified,
            TransitVerdict.NonIdentificato => TransitOutcome.NotIdentified,
            _ => TransitOutcome.PredictedNotVerified,
        },
        info.FramesEvaluated);

    /// <summary>Percorso assoluto del file dell'evento, solo se il nome è dichiarato e il file esiste.</summary>
    private static string? ExistingFile(EventArchive archive, EventRecord record, string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        string path = archive.GetEventFilePath(record.NightId, record.Id, fileName);
        return File.Exists(path) ? path : null;
    }

    private static EventCategory ToCategory(EventKind kind) => kind switch
    {
        EventKind.Bolide => EventCategory.Bolide,
        EventKind.Transit => EventCategory.Transit,
        _ => EventCategory.Meteor,
    };

    private static EventKind ToKind(EventCategory category) => category switch
    {
        EventCategory.Bolide => EventKind.Bolide,
        EventCategory.Transit => EventKind.Transit,
        _ => EventKind.Meteor,
    };

    private static EventClassKind ToClassKind(AllSky.Detection.StreakClass value) => value switch
    {
        AllSky.Detection.StreakClass.Meteor => EventClassKind.Meteor,
        AllSky.Detection.StreakClass.Bolide => EventClassKind.Bolide,
        AllSky.Detection.StreakClass.Satellite => EventClassKind.Satellite,
        AllSky.Detection.StreakClass.AereoOSatellite => EventClassKind.AircraftOrSatellite,
        _ => EventClassKind.Unknown,
    };

    private static EventLabelChoice ToLabel(UserLabel label) => label switch
    {
        UserLabel.Confirmed => EventLabelChoice.Confirmed,
        UserLabel.FalsePositive => EventLabelChoice.FalsePositive,
        _ => EventLabelChoice.None,
    };

    private static UserLabel ToUserLabel(EventLabelChoice label) => label switch
    {
        EventLabelChoice.Confirmed => UserLabel.Confirmed,
        EventLabelChoice.FalsePositive => UserLabel.FalsePositive,
        _ => UserLabel.None,
    };

    // ---- Archivio ----

    /// <summary>Archivio per la radice corrente. Si ricrea solo se la cartella è cambiata.</summary>
    private EventArchive Archive()
    {
        string folder = Path.GetFullPath(_rootFolder());
        lock (_gate)
        {
            if (_archive is null || !string.Equals(_archiveFolder, folder, StringComparison.Ordinal))
            {
                _archive = new EventArchive(folder);
                _archiveFolder = folder;
            }

            return _archive;
        }
    }
}
