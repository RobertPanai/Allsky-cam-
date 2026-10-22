using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class YouTubeUploadSchedulerTests
{
    [Fact]
    public async Task PrimaDellOra_NessunaChiamataDiRete()
    {
        using var h = new UploadHarness();
        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 7, 30);

        UploadOutcome outcome = await h.Scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.NotDue, outcome.Kind);
        Assert.Empty(h.Handler.Sent);
    }

    [Fact]
    public async Task All_OraPrevista_CaricaUnaVoltaAlGiorno()
    {
        using var h = new UploadHarness();
        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 8, 5);
        h.ScriptFullUpload("vid-day1");

        UploadOutcome first = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.Completed, first.Kind);

        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 9, 0);
        UploadOutcome later = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.NotDue, later.Kind);
        Assert.Equal(4, h.Handler.Sent.Count);

        // Il giorno dopo la stessa notte risulta gia' caricata: nessun nuovo upload.
        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 23, 8, 5);
        UploadOutcome nextDay = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.AlreadyUploaded, nextDay.Kind);
        Assert.Equal(4, h.Handler.Sent.Count);
    }

    [Fact]
    public async Task PcSpentoEntroLaFinestra_CaricaALAvvio()
    {
        using var h = new UploadHarness();
        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 15, 0);
        h.ScriptFullUpload("vid-late");

        UploadOutcome outcome = await h.Scheduler.TickAsync(CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.Completed, outcome.Kind);
        Assert.Equal("vid-late", outcome.VideoId);
    }

    [Fact]
    public async Task PcSpentoOltreLaFinestra_SaltaOggiESegnala()
    {
        using var h = new UploadHarness();
        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 21, 30);

        UploadOutcome missed = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.SlotMissed, missed.Kind);

        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 22, 0);
        UploadOutcome again = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.NotDue, again.Kind);
        Assert.Empty(h.Handler.Sent);
    }

    [Fact]
    public async Task Manuale_DopoIlCaricamentoProgrammato_RispettaIlLimiteGiornaliero()
    {
        using var h = new UploadHarness();
        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 8, 5);
        h.ScriptFullUpload("vid-a");
        await h.Scheduler.TickAsync(CancellationToken.None);

        h.Provider.Latest = h.NightAt("2026-10-22");
        UploadOutcome manual = await h.Scheduler.UploadLatestNowAsync(false, CancellationToken.None);

        Assert.Equal(UploadOutcomeKind.DailyLimitReached, manual.Kind);
        Assert.Equal(4, h.Handler.Sent.Count);
    }

    [Fact]
    public async Task NeedsReview_RichiedeConfermaEPoiRicarica()
    {
        using var h = new UploadHarness();
        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 10, 0);
        UploadTestRecords.SetRecords(h.Journal, UploadTestRecords.Record("2026-10-21", NightUploadState.NeedsReview));

        UploadOutcome blocked = await h.Scheduler.UploadLatestNowAsync(false, CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.NeedsReview, blocked.Kind);
        Assert.Empty(h.Handler.Sent);

        h.ScriptFullUpload("vid-confirmed");
        UploadOutcome confirmed = await h.Scheduler.UploadLatestNowAsync(true, CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.Completed, confirmed.Kind);
    }

    [Fact]
    public async Task Quota_BloccaFinoAlGiornoSuccessivoPoiRiprende()
    {
        using var h = new UploadHarness();
        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 8, 5);
        h.Handler.Reply(403, "{\"error\":{\"errors\":[{\"reason\":\"quotaExceeded\"}]}}");

        UploadOutcome blocked = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.QuotaBlocked, blocked.Kind);

        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 12, 0);
        UploadOutcome stillBlocked = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.QuotaBlocked, stillBlocked.Kind);
        Assert.Equal(1, h.Handler.Sent.Count);

        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 23, 8, 5);
        h.ScriptFullUpload("vid-after-quota");
        UploadOutcome resumed = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.Completed, resumed.Kind);
        Assert.Equal(2, h.Handler.Count("POST"));
    }

    [Fact]
    public async Task Transitorio_DopoIRetryEsauriti_RiprendeAllaScadenzaDellAttesa()
    {
        using var h = new UploadHarness();
        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 8, 5);
        for (int i = 0; i < 5; i++)
            h.Handler.Reply(500);

        UploadOutcome failed = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.InProgress, failed.Kind);
        Assert.Equal(4, h.Delays.Count);
        Assert.Equal(TimeSpan.FromSeconds(16), h.Delays[3]);

        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 8, 20);
        UploadOutcome waiting = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.NotDue, waiting.Kind);
        Assert.Equal(5, h.Handler.Sent.Count);

        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 8, 36);
        h.ScriptFullUpload("vid-retry");
        UploadOutcome done = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.Completed, done.Kind);
    }

    [Fact]
    public async Task NessunaNotte_NonCaricaENonRiprova()
    {
        using var h = new UploadHarness();
        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 8, 5);
        h.Provider.Latest = null;

        UploadOutcome none = await h.Scheduler.TickAsync(CancellationToken.None);
        Assert.Equal(UploadOutcomeKind.NoNight, none.Kind);

        h.Clock.UtcNow = UploadHarness.Local(2026, 10, 22, 9, 0);
        Assert.Equal(UploadOutcomeKind.NotDue, (await h.Scheduler.TickAsync(CancellationToken.None)).Kind);
        Assert.Empty(h.Handler.Sent);
    }
}
