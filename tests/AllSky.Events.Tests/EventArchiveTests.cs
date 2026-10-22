using System.IO.Compression;
using System.Text.Json;
using AllSky.Detection;
using AllSky.Events;
using Xunit;

namespace AllSky.Events.Tests;

// Archivio eventi: round-trip, filtri, etichette, stato, zip, validazione dei percorsi, versioni e file corrotti.
public class EventArchiveTests : IDisposable
{
    private readonly EventsTempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private EventArchive NewArchive() => new(_tmp.Path);

    private static EventRecord Record(string id, string night, DateTimeOffset ts, EventKind kind = EventKind.Meteor, StreakClass cls = StreakClass.Meteor)
    {
        return new EventRecord
        {
            Id = id,
            Kind = kind,
            NightId = night,
            TimestampUtc = ts,
            SourceFrameId = EventsFixtures.FrameId(ts),
            Class = cls,
            Score = 3.25,
            LengthPx = 61.5,
            PeakLuma = 120,
            IntegratedLuma = 900.5,
            Segment = new PixelSegment { X1 = 40, Y1 = 60, X2 = 100, Y2 = 61 },
            Files = new EventFiles { Crop = "crop.jpg", Frame = "frame.jpg" },
        };
    }

    private void SaveRaw(EventArchive archive, EventRecord record)
    {
        archive.CreateEventFolder(record.NightId, record.Id);
        archive.WriteFile(record.NightId, record.Id, "crop.jpg", [1, 2, 3]);
        archive.WriteFile(record.NightId, record.Id, "frame.jpg", [4, 5, 6]);
        archive.WriteRecord(record);
    }

    private static DateTimeOffset T(int minute) => new DateTimeOffset(2026, 10, 19, 21, 0, 0, TimeSpan.Zero).AddMinutes(minute);

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        EventArchive archive = NewArchive();
        EventRecord record = Record("20261019T210000Z-00", "2026-10-19", T(0), EventKind.Bolide, StreakClass.Bolide) with
        {
            StartAltAz = new AltAz { AltDeg = 41.25, AzDeg = 183.5 },
            EndAltAz = new AltAz { AltDeg = 38.0, AzDeg = 190.125 },
            Files = new EventFiles { Crop = "crop.jpg", Frame = "frame.jpg", Clip = "clip.mp4" },
            Label = UserLabel.Confirmed,
            LabelReason = "visto anche a occhio",
        };
        SaveRaw(archive, record);

        EventRecord? read = archive.Get("2026-10-19", record.Id);

        Assert.NotNull(read);
        Assert.Equal(record, read!);
        Assert.Equal(EventRecord.CurrentSchemaVersion, read!.SchemaVersion);
    }

    [Fact]
    public void RoundTrip_NullableSkyPositionsStayNull()
    {
        EventArchive archive = NewArchive();
        EventRecord record = Record("20261019T210000Z-01", "2026-10-19", T(0));
        SaveRaw(archive, record);

        EventRecord? read = archive.Get("2026-10-19", record.Id);

        Assert.NotNull(read);
        Assert.Null(read!.StartAltAz);
        Assert.Null(read.EndAltAz);
        Assert.Null(read.Files.Clip);
    }

    [Fact]
    public void List_FiltersByNightKindAndDiscardedState()
    {
        EventArchive archive = NewArchive();
        SaveRaw(archive, Record("20261019T210000Z-00", "2026-10-19", T(0), EventKind.Meteor));
        SaveRaw(archive, Record("20261019T210100Z-00", "2026-10-19", T(1), EventKind.Bolide, StreakClass.Bolide));
        SaveRaw(archive, Record("20261020T210000Z-00", "2026-10-20", T(60), EventKind.Meteor));
        archive.SetStatus("2026-10-19", "20261019T210000Z-00", EventStatus.Discarded, "test");

        Assert.Single(archive.List(new EventQuery { NightId = "2026-10-19" }));
        Assert.Equal(3, archive.List(new EventQuery { IncludeDiscarded = true }).Count);
        Assert.Single(archive.List(new EventQuery { Kind = EventKind.Meteor }));
        Assert.Single(archive.List(new EventQuery { Kind = EventKind.Meteor, NightId = "2026-10-19", IncludeDiscarded = true }));
        Assert.Empty(archive.List(new EventQuery { NightId = "2026-10-18" }));
        Assert.Empty(archive.List(new EventQuery { Kind = EventKind.Transit }));
    }

    [Fact]
    public void List_IsSortedNewestFirst()
    {
        EventArchive archive = NewArchive();
        SaveRaw(archive, Record("20261019T210000Z-00", "2026-10-19", T(0)));
        SaveRaw(archive, Record("20261019T210500Z-00", "2026-10-19", T(5)));

        IReadOnlyList<EventRecord> all = archive.List();

        Assert.Equal("20261019T210500Z-00", all[0].Id);
        Assert.Equal("20261019T210000Z-00", all[1].Id);
    }

    [Fact]
    public void Label_ConfirmAndFalsePositive_PersistAndClearReason()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        SaveRaw(archive, Record(id, "2026-10-19", T(0)));

        archive.SetLabel("2026-10-19", id, UserLabel.Confirmed);
        Assert.Equal(UserLabel.Confirmed, archive.Get("2026-10-19", id)!.Label);
        Assert.Null(archive.Get("2026-10-19", id)!.LabelReason);

        archive.SetLabel("2026-10-19", id, UserLabel.FalsePositive, "  aereo con fanale  ");
        EventRecord? falsePositive = archive.Get("2026-10-19", id);
        Assert.Equal(UserLabel.FalsePositive, falsePositive!.Label);
        Assert.Equal("aereo con fanale", falsePositive.LabelReason);

        Assert.Single(archive.List(new EventQuery { Label = UserLabel.FalsePositive }));
        Assert.Empty(archive.List(new EventQuery { Label = UserLabel.Confirmed }));

        archive.SetLabel("2026-10-19", id, UserLabel.None, "ignorato");
        Assert.Equal(UserLabel.None, archive.Get("2026-10-19", id)!.Label);
        Assert.Null(archive.Get("2026-10-19", id)!.LabelReason);
    }

    [Fact]
    public void Label_UnknownEvent_Throws()
    {
        EventArchive archive = NewArchive();
        Assert.Throws<KeyNotFoundException>(() => archive.SetLabel("2026-10-19", "20261019T210000Z-00", UserLabel.Confirmed));
    }

    [Fact]
    public void Discard_MarksEventButKeepsEveryFile()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        SaveRaw(archive, Record(id, "2026-10-19", T(0)));

        EventRecord discarded = archive.SetStatus("2026-10-19", id, EventStatus.Discarded, "riclassificato");

        Assert.Equal(EventStatus.Discarded, discarded.Status);
        Assert.Equal("riclassificato", discarded.StatusReason);
        Assert.True(File.Exists(archive.GetEventFilePath("2026-10-19", id, "crop.jpg")));
        Assert.True(File.Exists(archive.GetEventFilePath("2026-10-19", id, "frame.jpg")));
        Assert.True(File.Exists(archive.GetEventFilePath("2026-10-19", id, EventArchive.EventFileName)));
        Assert.Empty(archive.List());
        Assert.Single(archive.List(new EventQuery { IncludeDiscarded = true }));

        EventRecord restored = archive.SetStatus("2026-10-19", id, EventStatus.Active, "non deve restare");
        Assert.Equal(EventStatus.Active, restored.Status);
        Assert.Null(restored.StatusReason);
    }

    [Fact]
    public void ExportZip_ContainsEventJsonAndProducedFiles()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        SaveRaw(archive, Record(id, "2026-10-19", T(0)));
        string zipPath = Path.Combine(_tmp.Path, "export", id + ".zip");

        string written = archive.ExportZip("2026-10-19", id, zipPath);

        Assert.Equal(Path.GetFullPath(zipPath), written);
        Assert.False(File.Exists(zipPath + ".tmp"));
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(string.Join(",", id + "/crop.jpg", id + "/event.json", id + "/frame.jpg"), string.Join(",", names));

        using Stream json = zip.GetEntry(id + "/event.json")!.Open();
        using JsonDocument doc = JsonDocument.Parse(json);
        Assert.Equal(id, doc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public void ExportZip_InsideEventFolder_IsRejected()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        SaveRaw(archive, Record(id, "2026-10-19", T(0)));
        string inside = Path.Combine(archive.GetEventFolder("2026-10-19", id), "copy.zip");

        Assert.Throws<ArgumentException>(() => archive.ExportZip("2026-10-19", id, inside));
    }

    [Fact]
    public void ExportZip_UnknownEvent_Throws()
    {
        EventArchive archive = NewArchive();
        Assert.Throws<KeyNotFoundException>(() => archive.ExportZip("2026-10-19", "20261019T210000Z-00", Path.Combine(_tmp.Path, "x.zip")));
    }

    [Theory]
    [InlineData("../2026-10-19")]
    [InlineData("2026-10-19/../..")]
    [InlineData("..")]
    [InlineData("2026-10-19\\..")]
    [InlineData("2026-13-45")]
    [InlineData("")]
    public void InvalidNightId_IsRejected(string nightId)
    {
        EventArchive archive = NewArchive();
        Assert.Throws<ArgumentException>(() => archive.GetEventFolder(nightId, "20261019T210000Z-00"));
        Assert.Throws<ArgumentException>(() => archive.List(new EventQuery { NightId = nightId }));
    }

    [Theory]
    [InlineData("../../escape")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData(".hidden")]
    public void InvalidEventId_IsRejected(string eventId)
    {
        EventArchive archive = NewArchive();
        Assert.Throws<ArgumentException>(() => archive.GetEventFolder("2026-10-19", eventId));
        Assert.Throws<ArgumentException>(() => archive.Get("2026-10-19", eventId));
        Assert.Throws<ArgumentException>(() => archive.CreateEventFolder("2026-10-19", eventId));
    }

    [Theory]
    [InlineData("../evil.jpg")]
    [InlineData("..\\evil.jpg")]
    [InlineData("sub/evil.jpg")]
    [InlineData("")]
    public void InvalidFileName_IsRejected(string fileName)
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        archive.CreateEventFolder("2026-10-19", id);

        Assert.Throws<ArgumentException>(() => archive.GetEventFilePath("2026-10-19", id, fileName));
        Assert.Throws<ArgumentException>(() => archive.WriteFile("2026-10-19", id, fileName, [1]));
    }

    [Fact]
    public void WriteRecord_WithUnsafeFileName_IsRejected()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        archive.CreateEventFolder("2026-10-19", id);
        EventRecord bad = Record(id, "2026-10-19", T(0)) with { Files = new EventFiles { Crop = "../../x.jpg" } };

        Assert.Throws<ArgumentException>(() => archive.WriteRecord(bad));
    }

    [Fact]
    public void TamperedFileName_OnDisk_MakesRecordUnreadable()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        SaveRaw(archive, Record(id, "2026-10-19", T(0)));
        string json = archive.GetEventFilePath("2026-10-19", id, EventArchive.EventFileName);
        File.WriteAllText(json, File.ReadAllText(json).Replace("\"crop.jpg\"", "\"../../../secret.txt\""));

        Assert.Null(archive.Get("2026-10-19", id));
        Assert.Empty(archive.List());
    }

    [Fact]
    public void FutureSchemaVersion_IsIgnored()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        SaveRaw(archive, Record(id, "2026-10-19", T(0)));
        string json = archive.GetEventFilePath("2026-10-19", id, EventArchive.EventFileName);
        File.WriteAllText(json, File.ReadAllText(json).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"));

        Assert.Null(archive.Get("2026-10-19", id));
    }

    [Fact]
    public void WriteRecord_WithOtherSchemaVersion_IsRejected()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        archive.CreateEventFolder("2026-10-19", id);

        Assert.Throws<ArgumentException>(() => archive.WriteRecord(Record(id, "2026-10-19", T(0)) with { SchemaVersion = 2 }));
    }

    [Fact]
    public void CorruptJson_IsTreatedAsMissing()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        archive.CreateEventFolder("2026-10-19", id);
        archive.WriteFile("2026-10-19", id, EventArchive.EventFileName, "{ non json"u8.ToArray());

        Assert.Null(archive.Get("2026-10-19", id));
        Assert.Empty(archive.List(new EventQuery { IncludeDiscarded = true }));
    }

    [Fact]
    public void CreateEventFolder_TwiceThrows_AndKeepsLabels()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        SaveRaw(archive, Record(id, "2026-10-19", T(0)));
        archive.SetLabel("2026-10-19", id, UserLabel.Confirmed);

        Assert.Throws<InvalidOperationException>(() => archive.CreateEventFolder("2026-10-19", id));
        Assert.Equal(UserLabel.Confirmed, archive.Get("2026-10-19", id)!.Label);
    }

    [Fact]
    public void Write_LeavesNoTemporaryFiles()
    {
        EventArchive archive = NewArchive();
        const string id = "20261019T210000Z-00";
        SaveRaw(archive, Record(id, "2026-10-19", T(0)));
        archive.SetLabel("2026-10-19", id, UserLabel.FalsePositive, "prova");

        string folder = archive.GetEventFolder("2026-10-19", id);
        Assert.Empty(Directory.EnumerateFiles(folder, "*.tmp"));
    }

    [Fact]
    public void EventIds_ForStreak_AreStableAndValidated()
    {
        Assert.Equal("20261019T210000Z-003", EventIds.ForStreak("20261019T210000Z", 3));
        Assert.Equal(EventIds.ForStreak("20261019T210000Z", 3), EventIds.ForStreak("20261019T210000Z", 3));
        Assert.True(EventIds.IsValid("20261019T210000Z-003"));
        Assert.False(EventIds.IsValid("../x"));
        Assert.False(EventIds.IsValid(null));
        Assert.Throws<ArgumentException>(() => EventIds.ForStreak("2026-10-19", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventIds.ForStreak("20261019T210000Z", 1000));
    }
}
