using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class NightUploaderTests
{
    [Fact]
    public async Task CaricamentoCompleto_TreChunkESalvaCompletato()
    {
        using var h = new UploadHarness();
        h.ScriptFullUpload("vid-42");

        UploadOutcome outcome = await h.Uploader.StartAsync(h.NightAt(), h.Settings, CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.Completed, outcome.Kind);
        Assert.Equal("vid-42", outcome.VideoId);
        NightUploadRecord record = h.Journal.State.Nights["2026-10-21"];
        Assert.Equal(NightUploadState.Completed, record.State);
        Assert.Equal("vid-42", record.VideoId);
        Assert.True(record.SessionUri.IsEmpty);

        Assert.Equal(4, h.Handler.Sent.Count);
        Assert.Contains("Timelapse notte del 2026-10-21", h.Handler.Sent[0].Body!);
        Assert.Equal("bytes 0-262143/600000", h.Handler.Sent[1].ContentRange);
        Assert.Equal("bytes 262144-524287/600000", h.Handler.Sent[2].ContentRange);
        Assert.Equal("bytes 524288-599999/600000", h.Handler.Sent[3].ContentRange);
        Assert.Equal(75_712, h.Handler.Sent[3].ContentLength);
    }

    [Fact]
    public async Task Interruzione_RiprendeDopoRiavvioSenzaNuovaSessione()
    {
        using var h = new UploadHarness();
        await UploadHarness_Crash(h);

        NightUploadRecord interrupted = h.Journal.State.Nights["2026-10-21"];
        Assert.Equal(NightUploadState.InProgress, interrupted.State);
        Assert.False(interrupted.SessionUri.IsEmpty);
        Assert.DoesNotContain("SECRET-ABC", interrupted.ToString());

        h.Restart();
        h.Handler.Reply(308, "", ("Range", "bytes=0-262143"));
        h.Handler.Reply(308, "", ("Range", "bytes=0-524287"));
        h.Handler.Reply(201, "{\"id\":\"vid-9\"}");

        UploadOutcome outcome = await h.Scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.Completed, outcome.Kind);
        Assert.Equal("vid-9", outcome.VideoId);
        Assert.Equal(1, h.Handler.Count("POST"));
        // Ordine: POST, PUT chunk 1, PUT chunk 2 (interrotto), query di ripresa, PUT chunk 2, PUT chunk 3.
        Assert.Equal("bytes */600000", h.Handler.Sent[3].ContentRange);
        Assert.Equal("bytes 262144-524287/600000", h.Handler.Sent[4].ContentRange);
        Assert.Equal("bytes 524288-599999/600000", h.Handler.Sent[5].ContentRange);
    }

    [Fact]
    public async Task Transitorio500_RitentaConBackoffEsponenziale()
    {
        using var h = new UploadHarness();
        h.Handler.Reply(500);
        h.Handler.Reply(500);
        h.Handler.ReplyLocation();
        h.Handler.Reply(308, "", ("Range", "bytes=0-262143"));
        h.Handler.Reply(500);
        h.Handler.Reply(308, "", ("Range", "bytes=0-524287"));
        h.Handler.Reply(201, "{\"id\":\"vid-5\"}");

        UploadOutcome outcome = await h.Uploader.StartAsync(h.NightAt(), h.Settings, CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.Completed, outcome.Kind);
        Assert.Equal(3, h.Handler.Count("POST"));
        Assert.Equal(3, h.Delays.Count);
        Assert.Equal(TimeSpan.FromSeconds(2), h.Delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(4), h.Delays[1]);
        Assert.Equal(TimeSpan.FromSeconds(2), h.Delays[2]);
    }

    [Fact]
    public async Task Errore4xx_NonRiprovaEMarcaFallito()
    {
        using var h = new UploadHarness();
        h.Handler.Reply(400, "{\"error\":{\"errors\":[{\"reason\":\"invalidTitle\"}]}}");

        UploadOutcome outcome = await h.Uploader.StartAsync(h.NightAt(), h.Settings, CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.Failed, outcome.Kind);
        Assert.Single(h.Handler.Sent);
        Assert.Empty(h.Delays);
        Assert.Equal(NightUploadState.Failed, h.Journal.State.Nights["2026-10-21"].State);
        Assert.Contains("invalidTitle", outcome.Message);
    }

    [Fact]
    public async Task Quota403_BloccaFinoAMezzanotteEConservaLaSessione()
    {
        using var h = new UploadHarness();
        h.Handler.ReplyLocation();
        h.Handler.Reply(403, "{\"error\":{\"errors\":[{\"reason\":\"quotaExceeded\"}]}}");

        UploadOutcome outcome = await h.Uploader.StartAsync(h.NightAt(), h.Settings, CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.QuotaBlocked, outcome.Kind);
        Assert.Empty(h.Delays);
        NightUploadRecord record = h.Journal.State.Nights["2026-10-21"];
        Assert.Equal(NightUploadState.InProgress, record.State);
        Assert.False(record.SessionUri.IsEmpty);
        Assert.Equal(UploadHarness.Local(2026, 10, 23, 0, 0), h.Journal.State.QuotaBlockedUntilUtc!.Value, TimeSpan.Zero);
    }

    [Fact]
    public async Task SessioneScadutaDopoRiavvio_RichiedeVerificaSenzaRicaricare()
    {
        using var h = new UploadHarness();
        await UploadHarness_Crash(h);
        h.Restart();
        h.Handler.Reply(404, "{\"error\":{\"errors\":[{\"reason\":\"notFound\"}]}}");

        UploadOutcome outcome = await h.Scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.NeedsReview, outcome.Kind);
        Assert.Equal(1, h.Handler.Count("POST"));
        Assert.Equal(NightUploadState.NeedsReview, h.Journal.State.Nights["2026-10-21"].State);
    }

    [Fact]
    public async Task CrashPrimaDellaSessione_NessunByteInviato_UnaSolaSessioneBuona()
    {
        using var h = new UploadHarness();
        h.Handler.Crash();
        await Assert.ThrowsAsync<SimulatedCrashException>(() => h.Uploader.StartAsync(h.NightAt(), h.Settings, CancellationToken.None));
        Assert.Equal(NightUploadState.InProgress, h.Journal.State.Nights["2026-10-21"].State);
        Assert.True(h.Journal.State.Nights["2026-10-21"].SessionUri.IsEmpty);

        h.Restart();
        h.ScriptFullUpload("vid-7");
        UploadOutcome outcome = await h.Scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.Completed, outcome.Kind);
        Assert.Equal(2, h.Handler.Count("POST"));
        Assert.Equal(3, h.Handler.Count("PUT"));
    }

    [Fact]
    public async Task NotteGiaCaricata_DopoRiavvio_NessunNuovoUpload()
    {
        using var h = new UploadHarness();
        h.ScriptFullUpload();
        await h.Uploader.StartAsync(h.NightAt(), h.Settings, CancellationToken.None);

        h.Restart();
        UploadOutcome outcome = await h.Uploader.StartAsync(h.NightAt(), h.Settings, CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.AlreadyUploaded, outcome.Kind);
        Assert.Equal(4, h.Handler.Sent.Count);
    }

    [Fact]
    public async Task ConfigurazioneNonValida_NessunaChiamataDiRete()
    {
        using var h = new UploadHarness();
        YouTubeUploadSettings defaults = h.Settings;

        h.Settings = defaults with { CategoryId = "ventotto" };
        UploadOutcome category = await h.Uploader.StartAsync(h.NightAt(), h.Settings, CancellationToken.None);

        h.Settings = defaults with { TitleTemplate = "Titolo {nope}" };
        UploadOutcome title = await h.Uploader.StartAsync(h.NightAt(), h.Settings, CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.ConfigError, category.Kind);
        Assert.Equal(UploadOutcomeKind.ConfigError, title.Kind);
        Assert.Empty(h.Handler.Sent);
        Assert.Empty(h.Journal.State.Nights);
    }

    private static async Task UploadHarness_Crash(UploadHarness h) => await UploadTestRecords.CrashAfterFirstChunkAsync(h);
}
