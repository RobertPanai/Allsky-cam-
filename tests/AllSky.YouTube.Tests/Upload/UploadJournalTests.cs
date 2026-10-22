using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class UploadJournalTests
{
    [Fact]
    public void SessioneCifrataNelFile_EFuoriDalToString()
    {
        using var dir = new TempDir();
        var protector = new FakeProtector();
        var journal = new UploadJournal(dir.File("journal.json"), protector);
        UploadTestRecords.SetRecords(journal, UploadTestRecords.Record(
            "2026-10-21",
            NightUploadState.InProgress,
            new OAuthSecret("https://upload.example/s?upload_id=SECRET-XYZ")));

        string text = File.ReadAllText(dir.File("journal.json"));
        Assert.DoesNotContain("SECRET-XYZ", text);
        Assert.DoesNotContain("SECRET-XYZ", journal.State.Nights["2026-10-21"].ToString());

        var reloaded = new UploadJournal(dir.File("journal.json"), protector);
        Assert.Contains("SECRET-XYZ", reloaded.State.Nights["2026-10-21"].SessionUri.Reveal());
    }

    [Fact]
    public void ScritturaAtomica_NessunFileTemporaneoResiduo()
    {
        using var dir = new TempDir();
        var journal = new UploadJournal(dir.File("journal.json"), new FakeProtector());

        UploadTestRecords.SetRecords(journal, UploadTestRecords.Record("2026-10-21", NightUploadState.Completed));

        Assert.True(File.Exists(dir.File("journal.json")));
        Assert.False(File.Exists(dir.File("journal.json.tmp")));
    }

    [Fact]
    public void FileCorrotto_BloccaIlCaricamento()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("journal.json"), "{ non e' json");

        Assert.Throws<InvalidDataException>(() => { _ = new UploadJournal(dir.File("journal.json"), new FakeProtector()); });
    }

    [Fact]
    public void ProtezioneNonDisponibile_NonSalvaELoStatoResta()
    {
        using var dir = new TempDir();
        var protector = new FakeProtector { NotSupported = true };
        var journal = new UploadJournal(dir.File("journal.json"), protector);

        Assert.Throws<InvalidOperationException>(() => UploadTestRecords.SetRecords(
            journal,
            UploadTestRecords.Record("2026-10-21", NightUploadState.InProgress, new OAuthSecret("sessione"))));

        Assert.Empty(journal.State.Nights);
        Assert.False(File.Exists(dir.File("journal.json")));
    }

    [Fact]
    public void SessioneNonDecifrabile_DiventaDaVerificare()
    {
        using var dir = new TempDir();
        var protector = new FakeProtector();
        var journal = new UploadJournal(dir.File("journal.json"), protector);
        UploadTestRecords.SetRecords(journal, UploadTestRecords.Record(
            "2026-10-21",
            NightUploadState.InProgress,
            new OAuthSecret("sessione-prova")));

        var reloaded = new UploadJournal(dir.File("journal.json"), new FakeProtector { FailUnprotect = true });

        NightUploadRecord record = reloaded.State.Nights["2026-10-21"];
        Assert.Equal(NightUploadState.NeedsReview, record.State);
        Assert.True(record.SessionUri.IsEmpty);
    }
}
