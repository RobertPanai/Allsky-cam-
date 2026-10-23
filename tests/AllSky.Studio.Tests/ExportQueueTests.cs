using Xunit;
using AllSky.Telegram;
using AllSky.YouTube;

namespace AllSky.Studio.Tests;

public sealed class ExportQueueTests : IDisposable
{
    private readonly ExportHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task LocalFile_Successo_ScriveIlFileFinale_SenzaTemporanei()
    {
        ExportJob done = await _h.RunOnceAsync(_h.LocalJob("{project} {date}"));

        Assert.Equal(ExportJobState.Succeeded, done.State);
        Assert.Equal(1.0, done.Progress);
        Assert.Equal("Notte di prova 2026-10-22.mp4", done.OutputFileName);
        string only = Assert.Single(Directory.GetFiles(_h.Folder));
        Assert.Equal("Notte di prova 2026-10-22.mp4", Path.GetFileName(only));
        Assert.Empty(Directory.GetFiles(_h.WorkFolder));
    }

    [Fact]
    public async Task LocalFile_Fail_FileEsistente_ErroreSenzaPercorso()
    {
        await _h.RunOnceAsync(_h.LocalJob("{project}"));
        ExportJob second = await _h.RunOnceAsync(_h.LocalJob("{project}", OverwritePolicy.Fail, sequence: 2));

        Assert.Equal(ExportJobState.Failed, second.State);
        Assert.False(second.Retryable);
        Assert.Contains("«Notte di prova.mp4»", second.Error!);
        Assert.DoesNotContain(_h.Folder, second.Error!);
        Assert.Single(Directory.GetFiles(_h.Folder));
    }

    [Fact]
    public async Task LocalFile_Rename_AggiungeNumeroTraParentesi()
    {
        await _h.RunOnceAsync(_h.LocalJob("{project}", OverwritePolicy.Rename, sequence: 1));
        ExportJob second = await _h.RunOnceAsync(_h.LocalJob("{project}", OverwritePolicy.Rename, sequence: 2));

        Assert.Equal(ExportJobState.Succeeded, second.State);
        Assert.Equal("Notte di prova (2).mp4", second.OutputFileName);
        Assert.Equal(2, Directory.GetFiles(_h.Folder).Length);
    }

    [Fact]
    public async Task LocalFile_Overwrite_SostituisceIlFileEsistente()
    {
        string existing = Path.Combine(_h.Folder, "Notte di prova.mp4");
        File.WriteAllText(existing, "vecchio");

        ExportJob done = await _h.RunOnceAsync(_h.LocalJob("{project}", OverwritePolicy.Overwrite));

        Assert.Equal(ExportJobState.Succeeded, done.State);
        Assert.Equal("FAKE-VIDEO", File.ReadAllText(existing));
    }

    [Fact]
    public async Task Annullamento_AMeta_NessunFileParzialeNeTemporaneo()
    {
        var started = new TaskCompletionSource();
        _h.Renderer.Behavior = async call =>
        {
            await File.WriteAllBytesAsync(call.OutputPath, new byte[] { 1, 2, 3 }, call.Token);
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, call.Token);
        };
        ExportJob job = _h.LocalJob("{project}");
        Assert.True(_h.Enqueue(job).Ok);

        Task run = _h.Queue.RunPendingAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        QueueResult cancel = _h.Queue.Cancel(job.Id);
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(cancel.Ok);
        Assert.Equal(ExportJobState.Cancelled, _h.Queue.Find(job.Id)!.State);
        Assert.Empty(Directory.GetFiles(_h.Folder));
        Assert.Empty(Directory.GetFiles(_h.WorkFolder));
    }

    [Fact]
    public async Task Pausa_NonRiparteDaSola_RiprendeDaZero()
    {
        ExportJob job = _h.LocalJob("{project}");
        Assert.True(_h.Enqueue(job).Ok);
        Assert.True(_h.Queue.Pause(job.Id).Ok);

        await _h.Queue.RunPendingAsync(CancellationToken.None);
        Assert.Equal(0, _h.Renderer.Calls);
        Assert.Equal(ExportJobState.Paused, _h.Queue.Find(job.Id)!.State);

        Assert.True(_h.Queue.Resume(job.Id).Ok);
        await _h.Queue.RunPendingAsync(CancellationToken.None);
        Assert.Equal(1, _h.Renderer.Calls);
        Assert.Equal(ExportJobState.Succeeded, _h.Queue.Find(job.Id)!.State);
    }

    [Fact]
    public async Task Progresso_NonDecresceMai_ArrivaAUno()
    {
        ExportJob job = _h.LocalJob("{project}");
        var seen = new List<double>();
        _h.Queue.JobChanged += j =>
        {
            if (j.Id == job.Id)
            {
                seen.Add(j.Progress);
            }
        };
        _h.Renderer.Behavior = call =>
        {
            call.Progress.Report(0.4);
            call.Progress.Report(0.2);
            call.Progress.Report(0.9);
            call.Progress.Report(0.5);
            File.WriteAllText(call.OutputPath, "x");
            return Task.CompletedTask;
        };

        ExportJob done = await _h.RunOnceAsync(job);

        Assert.Equal(ExportJobState.Succeeded, done.State);
        Assert.NotEmpty(seen);
        Assert.All(seen, p => Assert.InRange(p, 0, 1));
        for (int i = 1; i < seen.Count; i++)
        {
            Assert.True(seen[i] >= seen[i - 1], $"progresso decrescente in posizione {i}: {seen[i - 1]} -> {seen[i]}");
        }

        Assert.Equal(1.0, seen[^1]);
    }

    [Fact]
    public async Task Succeeded_NonRiparteENonSiAccodaDiNuovo()
    {
        ExportJob job = _h.LocalJob("{project}");
        await _h.RunOnceAsync(job);

        QueueResult again = _h.Enqueue(job);
        await _h.Queue.RunPendingAsync(CancellationToken.None);

        Assert.False(again.Ok);
        Assert.Equal(1, _h.Renderer.Calls);
        Assert.Equal(ExportJobState.Succeeded, _h.Queue.Find(job.Id)!.State);
    }

    [Fact]
    public async Task YouTube_Successo_TitoloEDescrizioneDaModello()
    {
        var destination = new YouTubeDestination
        {
            Title = "Timelapse {notte:yyyy-MM-dd}",
            Description = "Da {luogo}: {fotogrammi} fotogrammi",
            Privacy = "unlisted",
        };

        ExportJob done = await _h.RunOnceAsync(_h.Job(destination));

        Assert.Equal(ExportJobState.Succeeded, done.State);
        YouTubeUploadRequest request = Assert.Single(_h.YouTube.Requests);
        Assert.Equal("Timelapse 2026-10-22", request.Title);
        Assert.Equal("Da Osservatorio di prova: 10 fotogrammi", request.Description);
        Assert.Equal(YouTubePrivacy.Unlisted, request.Privacy);
        Assert.Empty(Directory.GetFiles(_h.WorkFolder));
    }

    [Fact]
    public async Task YouTube_ErroreDiReteAMeta_FailedRiprovabile_MaxTreTentativi()
    {
        int calls = 0;
        _h.YouTube.Behavior = (request, progress, ct) =>
        {
            calls++;
            progress?.Report(0.5);
            throw new ExportTransportException("connessione reimpostata dal peer");
        };
        ExportJob job = _h.Job(new YouTubeDestination { Title = "ok", Privacy = "private" });

        ExportJob first = await _h.RunOnceAsync(job);
        Assert.Equal(ExportJobState.Failed, first.State);
        Assert.True(first.Retryable);
        Assert.Contains("Riprova", first.Error!);
        Assert.Contains("%", first.Error!);
        Assert.Empty(Directory.GetFiles(_h.WorkFolder));

        Assert.True(_h.Queue.Retry(job.Id).Ok);
        await _h.Queue.RunPendingAsync(CancellationToken.None);
        Assert.True(_h.Queue.Find(job.Id)!.Retryable);

        Assert.True(_h.Queue.Retry(job.Id).Ok);
        await _h.Queue.RunPendingAsync(CancellationToken.None);

        ExportJob last = _h.Queue.Find(job.Id)!;
        Assert.Equal(ExportJobState.Failed, last.State);
        Assert.Equal(3, last.ConsecutiveFailures);
        Assert.False(last.Retryable);
        Assert.Contains("Tentativi esauriti", last.Error!);

        QueueResult refused = _h.Queue.Retry(job.Id);
        Assert.False(refused.Ok);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task YouTube_ErroreDiRetePoiSuccesso_AzzeraIContatore()
    {
        int calls = 0;
        _h.YouTube.Behavior = (request, progress, ct) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new ExportTransportException("rete caduta");
            }

            return Task.FromResult("video-ok");
        };
        ExportJob job = _h.Job(new YouTubeDestination { Title = "ok", Privacy = "private" });

        await _h.RunOnceAsync(job);
        Assert.True(_h.Queue.Retry(job.Id).Ok);
        await _h.Queue.RunPendingAsync(CancellationToken.None);

        ExportJob done = _h.Queue.Find(job.Id)!;
        Assert.Equal(ExportJobState.Succeeded, done.State);
        Assert.Equal(0, done.ConsecutiveFailures);
        Assert.Equal(2, done.Attempts);
    }

    [Fact]
    public async Task ErroreImprevisto_NonRiprovabile_MessaggioGenerico()
    {
        _h.YouTube.Behavior = (request, progress, ct) => throw new InvalidOperationException("dettaglio interno");
        ExportJob done = await _h.RunOnceAsync(_h.Job(new YouTubeDestination { Title = "ok", Privacy = "private" }));

        Assert.Equal(ExportJobState.Failed, done.State);
        Assert.False(done.Retryable);
        Assert.Contains("Errore imprevisto", done.Error!);
        Assert.DoesNotContain("dettaglio interno", done.Error!);
    }

    [Fact]
    public async Task Telegram_Successo_InviaConChatEDidascalia()
    {
        ExportJob done = await _h.RunOnceAsync(_h.Job(new TelegramDestination
        {
            ChatId = "-1001234567890",
            Caption = "Notte di prova",
        }));

        Assert.Equal(ExportJobState.Succeeded, done.State);
        var sent = Assert.Single(_h.Telegram.Sent);
        Assert.Equal(-1001234567890L, sent.ChatId);
        Assert.Equal("Notte di prova", sent.Caption);
        Assert.Empty(Directory.GetFiles(_h.WorkFolder));
    }

    [Fact]
    public async Task Telegram_FileOltreIlLimite_FallisceSenzaRete()
    {
        _h.Renderer.Behavior = call =>
        {
            using FileStream stream = File.Create(call.OutputPath);
            stream.SetLength(TelegramLimits.FileMaxBytes + 1);
            return Task.CompletedTask;
        };

        ExportJob done = await _h.RunOnceAsync(_h.Job(new TelegramDestination { ChatId = "42" }));

        Assert.Equal(ExportJobState.Failed, done.State);
        Assert.False(done.Retryable);
        Assert.Contains("limite", done.Error!);
        Assert.Equal(0, _h.Telegram.Calls);
        Assert.Empty(Directory.GetFiles(_h.WorkFolder));
    }

    [Fact]
    public async Task RenderingFallito_NonLasciaTemporanei()
    {
        _h.Renderer.Behavior = call =>
        {
            File.WriteAllText(call.OutputPath, "parziale");
            throw new ExportStepException("il codificatore si è fermato");
        };

        ExportJob done = await _h.RunOnceAsync(_h.LocalJob("{project}"));

        Assert.Equal(ExportJobState.Failed, done.State);
        Assert.Equal("il codificatore si è fermato", done.Error);
        Assert.Empty(Directory.GetFiles(_h.Folder));
    }

    [Fact]
    public async Task DueLavori_ConDueSlotDiConcorrenza_GiranoInsieme()
    {
        using var h = new ExportHarness(maxConcurrent: 2);
        int started = 0;
        var both = new TaskCompletionSource();
        h.Renderer.Behavior = async call =>
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                both.TrySetResult();
            }

            await both.Task.WaitAsync(TimeSpan.FromSeconds(10), call.Token);
            await File.WriteAllBytesAsync(call.OutputPath, new byte[] { 1 }, call.Token);
        };

        ExportJob a = h.LocalJob("{project} a", sequence: 1);
        ExportJob b = h.LocalJob("{project} b", sequence: 2);
        Assert.True(h.Enqueue(a).Ok);
        Assert.True(h.Enqueue(b).Ok);
        await h.Queue.RunPendingAsync(CancellationToken.None);

        Assert.Equal(2, h.Renderer.MaxParallel);
        Assert.Equal(ExportJobState.Succeeded, h.Queue.Find(a.Id)!.State);
        Assert.Equal(ExportJobState.Succeeded, h.Queue.Find(b.Id)!.State);
    }

    [Fact]
    public void ConcorrenzaFuoriScala_Errore()
    {
        var options = new ExportQueueOptions(
            Path.Combine(_h.Tmp.Root, "x.json"),
            _h.WorkFolder,
            new ExportTestClock(),
            TimeZoneInfo.Utc,
            "x",
            MaxConcurrent: 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExportQueue.Open(options,
            new ExportServices(_h.Renderer, _h.YouTube, _h.Telegram, _h.Resolver)));
    }

    [Fact]
    public void Accodamento_PianoDiversoDalLavoro_Rifiutato()
    {
        ExportJob job = _h.LocalJob("{project}") with { PlanFingerprint = "impronta-diversa" };
        QueueResult result = _h.Enqueue(job);
        Assert.False(result.Ok);
        Assert.Empty(_h.Queue.Jobs());
    }
}
