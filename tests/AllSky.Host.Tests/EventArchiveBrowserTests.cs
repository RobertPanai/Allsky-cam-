using System.IO.Compression;
using AllSky.Core.Events;
using AllSky.Detection;
using AllSky.Events;
using AllSky.Host.Events;
using AllSky.Transits;
using Xunit;

namespace AllSky.Host.Tests;

/// <summary>
/// Adattatore <see cref="IEventBrowser"/> sopra l'archivio eventi: elenco con filtri, dettaglio, etichette, esportazione
/// e percorsi dei file. Gli eventi si scrivono con <see cref="EventArchive"/> in una cartella temporanea.
/// </summary>
public sealed class EventArchiveBrowserTests : IDisposable
{
    private const string NightA = "2026-10-19";
    private const string NightB = "2026-10-20";

    private readonly TempFolder _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static DateTimeOffset At(int minute) => new DateTimeOffset(2026, 10, 19, 21, 0, 0, TimeSpan.Zero).AddMinutes(minute);

    private EventRecord Meteor(string id, string night, DateTimeOffset ts, string? crop = "crop.jpg", string? frame = "frame.jpg") => new()
    {
        Id = id,
        Kind = EventKind.Meteor,
        NightId = night,
        TimestampUtc = ts,
        SourceFrameId = "20261019T210000Z",
        Class = StreakClass.Meteor,
        Score = 3.25,
        LengthPx = 61.5,
        PeakLuma = 120,
        IntegratedLuma = 900.5,
        Segment = new PixelSegment { X1 = 40, Y1 = 60, X2 = 100, Y2 = 61 },
        Files = new EventFiles { Crop = crop, Frame = frame },
    };

    private EventRecord Transit(string id, string night, DateTimeOffset ts, string? composite = "composite.jpg") => new()
    {
        Id = id,
        Kind = EventKind.Transit,
        NightId = night,
        TimestampUtc = ts,
        SourceFrameId = "20261019T210000Z",
        Class = StreakClass.Satellite,
        Score = 0.8,
        Files = new EventFiles { Frame = "frame.jpg", Composite = composite, Clip = "clip.mp4" },
        StartAltAz = new AltAz { AltDeg = 41.25, AzDeg = 183.5 },
        TransitInfo = new TransitDetails
        {
            SatelliteName = "ISS (ZARYA)",
            NoradId = 25544,
            StartUtc = ts,
            CulminationUtc = ts.AddMinutes(3),
            EndUtc = ts.AddMinutes(6),
            MaxElevationDeg = 62.5,
            Score = 0.8,
            Verdict = TransitVerdict.Identificato,
            FramesEvaluated = 12,
            VisibleTrackLengthPx = 300,
        },
    };

    /// <summary>Scrive un evento con i file indicati nel record (solo quelli passati vengono creati su disco).</summary>
    private static void Save(EventArchive archive, EventRecord record, params string[] filesOnDisk)
    {
        archive.CreateEventFolder(record.NightId, record.Id);
        foreach (string name in filesOnDisk)
        {
            archive.WriteFile(record.NightId, record.Id, name, [1, 2, 3]);
        }

        archive.WriteRecord(record);
    }

    [Fact]
    public void EmptyArchive_ListsNothingAndGetReturnsNull()
    {
        var browser = new EventArchiveBrowser(() => _tmp.Path);

        Assert.Empty(browser.List(new EventFilter()));
        Assert.Null(browser.Get(NightA, "non-esiste"));
    }

    [Fact]
    public void List_ReturnsNewestFirst_AndMapsCategories()
    {
        EventArchive archive = new(_tmp.Path);
        Save(archive, Meteor("meteor-1", NightA, At(0)), "crop.jpg", "frame.jpg");
        Save(archive, Meteor("bolide-1", NightA, At(10)) with { Kind = EventKind.Bolide, Class = StreakClass.Bolide }, "crop.jpg");
        Save(archive, Transit("transit-1", NightB, At(20)), "frame.jpg", "composite.jpg");

        var browser = new EventArchiveBrowser(() => _tmp.Path);
        IReadOnlyList<EventListItem> items = browser.List(new EventFilter());

        Assert.Equal(new[] { "transit-1", "bolide-1", "meteor-1" }, items.Select(i => i.Id).ToArray());
        Assert.Equal(new[] { EventCategory.Transit, EventCategory.Bolide, EventCategory.Meteor }, items.Select(i => i.Category).ToArray());
    }

    [Fact]
    public void List_FiltersByNightAndCategory()
    {
        EventArchive archive = new(_tmp.Path);
        Save(archive, Meteor("meteor-a", NightA, At(0)), "crop.jpg");
        Save(archive, Meteor("meteor-b", NightB, At(30)), "crop.jpg");
        Save(archive, Transit("transit-b", NightB, At(40)), "composite.jpg");

        var browser = new EventArchiveBrowser(() => _tmp.Path);

        Assert.Equal(new[] { "meteor-b" }, browser.List(new EventFilter(NightB, EventCategory.Meteor)).Select(i => i.Id).ToArray());
        Assert.Equal(new[] { "transit-b", "meteor-b" }, browser.List(new EventFilter(NightB)).Select(i => i.Id).ToArray());
        Assert.Equal(new[] { "meteor-a" }, browser.List(new EventFilter(null, EventCategory.Meteor)).Where(i => i.NightId == NightA).Select(i => i.Id).ToArray());
    }

    [Fact]
    public void List_HidesDiscardedEvents()
    {
        EventArchive archive = new(_tmp.Path);
        Save(archive, Meteor("kept", NightA, At(0)), "crop.jpg");
        Save(archive, Meteor("dropped", NightA, At(5)), "crop.jpg");
        archive.SetStatus(NightA, "dropped", EventStatus.Discarded, "riclassificato");

        var browser = new EventArchiveBrowser(() => _tmp.Path);

        Assert.Equal(new[] { "kept" }, browser.List(new EventFilter()).Select(i => i.Id).ToArray());
        Assert.NotNull(browser.Get(NightA, "dropped"));
    }

    [Fact]
    public void Thumbnail_UsesCropForMeteors_AndCompositeForTransits()
    {
        EventArchive archive = new(_tmp.Path);
        Save(archive, Meteor("meteor-1", NightA, At(0)), "crop.jpg", "frame.jpg");
        Save(archive, Transit("transit-1", NightA, At(20)), "frame.jpg", "composite.jpg");

        var browser = new EventArchiveBrowser(() => _tmp.Path);
        IReadOnlyList<EventListItem> items = browser.List(new EventFilter());

        string meteorThumb = items.Single(i => i.Id == "meteor-1").ThumbnailPath!;
        string transitThumb = items.Single(i => i.Id == "transit-1").ThumbnailPath!;
        Assert.EndsWith("crop.jpg", meteorThumb);
        Assert.EndsWith("composite.jpg", transitThumb);
        Assert.True(Path.IsPathFullyQualified(meteorThumb));
    }

    [Fact]
    public void MissingFileOnDisk_IsReportedAsNullPath()
    {
        EventArchive archive = new(_tmp.Path);
        // Il record dichiara crop.jpg, ma il file non è stato scritto: il percorso non deve comparire.
        Save(archive, Meteor("meteor-1", NightA, At(0), crop: "crop.jpg", frame: "frame.jpg"), "frame.jpg");

        var browser = new EventArchiveBrowser(() => _tmp.Path);
        EventDetail? detail = browser.Get(NightA, "meteor-1");

        Assert.NotNull(detail);
        Assert.Null(detail!.CropPath);
        Assert.EndsWith("frame.jpg", detail.FramePath!);
        Assert.Null(detail.ClipPath);
        Assert.Null(detail.CompositePath);
    }

    [Fact]
    public void Get_MapsDataAndSkyPositions()
    {
        EventArchive archive = new(_tmp.Path);
        EventRecord record = Meteor("meteor-1", NightA, At(0)) with
        {
            StartAltAz = new AltAz { AltDeg = 41.25, AzDeg = 183.5 },
            EndAltAz = new AltAz { AltDeg = 38.0, AzDeg = 190.125 },
            Label = UserLabel.Confirmed,
        };
        Save(archive, record, "crop.jpg", "frame.jpg");

        EventDetail? detail = new EventArchiveBrowser(() => _tmp.Path).Get(NightA, "meteor-1");

        Assert.NotNull(detail);
        Assert.Equal(EventCategory.Meteor, detail!.Category);
        Assert.Equal(EventClassKind.Meteor, detail.Class);
        Assert.Equal(3.25, detail.Score);
        Assert.Equal(61.5, detail.LengthPx);
        Assert.Equal(new SkyPoint(41.25, 183.5), detail.Start);
        Assert.Equal(new SkyPoint(38.0, 190.125), detail.End);
        Assert.Equal(EventLabelChoice.Confirmed, detail.Label);
        Assert.Null(detail.Transit);
    }

    [Fact]
    public void Get_WithoutCalibration_LeavesSkyPositionsNull()
    {
        EventArchive archive = new(_tmp.Path);
        Save(archive, Meteor("meteor-1", NightA, At(0)), "crop.jpg");

        EventDetail? detail = new EventArchiveBrowser(() => _tmp.Path).Get(NightA, "meteor-1");

        Assert.NotNull(detail);
        Assert.Null(detail!.Start);
        Assert.Null(detail.End);
    }

    [Fact]
    public void Get_Transit_CarriesSatelliteWindowAndVerdict()
    {
        EventArchive archive = new(_tmp.Path);
        Save(archive, Transit("transit-1", NightA, At(20)), "frame.jpg", "composite.jpg", "clip.mp4");

        EventDetail? detail = new EventArchiveBrowser(() => _tmp.Path).Get(NightA, "transit-1");

        Assert.NotNull(detail);
        Assert.Equal(EventCategory.Transit, detail!.Category);
        Assert.NotNull(detail.Transit);
        Assert.Equal("ISS (ZARYA)", detail.Transit!.SatelliteName);
        Assert.Equal(25544, detail.Transit.NoradId);
        Assert.Equal(TransitOutcome.Identified, detail.Transit.Outcome);
        Assert.Equal(12, detail.Transit.FramesEvaluated);
        Assert.EndsWith("clip.mp4", detail.ClipPath!);
        Assert.EndsWith("composite.jpg", detail.CompositePath!);
    }

    [Theory]
    [InlineData(TransitVerdict.Identificato, TransitOutcome.Identified)]
    [InlineData(TransitVerdict.NonIdentificato, TransitOutcome.NotIdentified)]
    [InlineData(TransitVerdict.PrevistoNonVerificato, TransitOutcome.PredictedNotVerified)]
    public void Get_Transit_MapsEveryVerdict(TransitVerdict verdict, TransitOutcome expected)
    {
        EventArchive archive = new(_tmp.Path);
        EventRecord record = Transit("transit-1", NightA, At(20)) with
        {
            TransitInfo = Transit("x", NightA, At(20)).TransitInfo! with { Verdict = verdict },
        };
        Save(archive, record, "frame.jpg");

        EventDetail? detail = new EventArchiveBrowser(() => _tmp.Path).Get(NightA, "transit-1");

        Assert.Equal(expected, detail!.Transit!.Outcome);
    }

    [Fact]
    public void Get_Class_MapsAircraftAndUnknown()
    {
        EventArchive archive = new(_tmp.Path);
        Save(archive, Meteor("aereo", NightA, At(0)) with { Class = StreakClass.AereoOSatellite }, "crop.jpg");

        EventDetail? detail = new EventArchiveBrowser(() => _tmp.Path).Get(NightA, "aereo");

        Assert.Equal(EventClassKind.AircraftOrSatellite, detail!.Class);
    }

    [Fact]
    public void SetLabel_PersistsAndAppearsInTheList()
    {
        EventArchive archive = new(_tmp.Path);
        Save(archive, Meteor("meteor-1", NightA, At(0)), "crop.jpg");
        var browser = new EventArchiveBrowser(() => _tmp.Path);

        EventDetail updated = browser.SetLabel(NightA, "meteor-1", EventLabelChoice.FalsePositive);

        Assert.Equal(EventLabelChoice.FalsePositive, updated.Label);
        Assert.Equal(EventLabelChoice.FalsePositive, browser.Get(NightA, "meteor-1")!.Label);
        Assert.Equal(EventLabelChoice.FalsePositive, browser.List(new EventFilter()).Single().Label);

        EventDetail cleared = browser.SetLabel(NightA, "meteor-1", EventLabelChoice.None);
        Assert.Equal(EventLabelChoice.None, cleared.Label);
    }

    [Fact]
    public void SetLabel_UnknownEvent_Throws()
    {
        var browser = new EventArchiveBrowser(() => _tmp.Path);

        Assert.Throws<KeyNotFoundException>(() => browser.SetLabel(NightA, "non-esiste", EventLabelChoice.Confirmed));
    }

    [Fact]
    public void ExportZip_WritesEventFolderWithRecordAndFiles()
    {
        EventArchive archive = new(_tmp.Path);
        Save(archive, Meteor("meteor-1", NightA, At(0)), "crop.jpg", "frame.jpg");
        var browser = new EventArchiveBrowser(() => _tmp.Path);
        string zip = Path.Combine(_tmp.Path, "export", "meteor-1.zip");

        string written = browser.ExportZip(NightA, "meteor-1", zip);

        Assert.Equal(Path.GetFullPath(zip), written);
        using ZipArchive archiveZip = ZipFile.OpenRead(zip);
        string[] names = archiveZip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "meteor-1/crop.jpg", "meteor-1/event.json", "meteor-1/frame.jpg" }, names);
    }

    [Fact]
    public void ExportZip_InsideEventFolder_IsRefused()
    {
        EventArchive archive = new(_tmp.Path);
        Save(archive, Meteor("meteor-1", NightA, At(0)), "crop.jpg");
        var browser = new EventArchiveBrowser(() => _tmp.Path);
        string inside = Path.Combine(archive.GetEventFolder(NightA, "meteor-1"), "dentro.zip");

        Assert.Throws<ArgumentException>(() => browser.ExportZip(NightA, "meteor-1", inside));
    }

    [Fact]
    public void RootFolder_IsReadOnEveryCall()
    {
        string first = Path.Combine(_tmp.Path, "primo");
        string second = Path.Combine(_tmp.Path, "secondo");
        string current = first;
        var browser = new EventArchiveBrowser(() => current);

        Save(new EventArchive(first), Meteor("in-primo", NightA, At(0)), "crop.jpg");
        Save(new EventArchive(second), Meteor("in-secondo", NightA, At(5)), "crop.jpg");

        Assert.Equal(new[] { "in-primo" }, browser.List(new EventFilter()).Select(i => i.Id).ToArray());

        current = second;
        Assert.Equal(new[] { "in-secondo" }, browser.List(new EventFilter()).Select(i => i.Id).ToArray());
    }

    /// <summary>Cartella temporanea, rimossa alla fine del test. Classe annidata per non collidere con altri helper.</summary>
    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "allsky-host-events-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Una cartella ancora in uso non fa fallire il test.
            }
        }
    }
}
