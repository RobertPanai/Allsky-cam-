using Xunit;
using AllSky.YouTube;

namespace AllSky.Studio.Tests;

/// <summary>Stato della coda su file, ripresa dopo riavvio, persistenza e sicurezza.</summary>
public sealed class ExportRecoveryTests : IDisposable
{
    private readonly ExportHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Riavvio_LavoroInCorso_TornaInCodaConConteggio_ERipartePulito()
    {
        ExportJob job = _h.LocalJob("{project}") with { State = ExportJobState.Running, Attempts = 1 };
        ExportQueueStore.Write(_h.StateFile, [job]);
        string leftover = Path.Combine(_h.Folder, "." + job.Id + ".part");
        File.WriteAllText(leftover, "parziale di prima");

        ExportQueue queue = _h.Reopen();

        ExportJob reloaded = Assert.Single(queue.Jobs());
        Assert.Equal(ExportJobState.Queued, reloaded.State);
        Assert.Equal(1, reloaded.InterruptedRuns);
        Assert.Equal(1, reloaded.Attempts);
        Assert.False(File.Exists(leftover));

        await queue.RunPendingAsync(CancellationToken.None);

        ExportJob done = queue.Find(job.Id)!;
        Assert.Equal(ExportJobState.Succeeded, done.State);
        Assert.Equal(2, done.Attempts);
        Assert.Equal(1, _h.Renderer.Calls);
    }

    [Fact]
    public async Task Riavvio_LavoroCompletato_NonRipartemai()
    {
        ExportJob job = _h.LocalJob("{project}") with
        {
            State = ExportJobState.Succeeded,
            Progress = 1,
            OutputFileName = "gia-fatto.mp4",
        };
        ExportQueueStore.Write(_h.StateFile, [job]);

        ExportQueue queue = _h.Reopen();
        await queue.RunPendingAsync(CancellationToken.None);

        ExportJob after = queue.Find(job.Id)!;
        Assert.Equal(ExportJobState.Succeeded, after.State);
        Assert.Equal("gia-fatto.mp4", after.OutputFileName);
        Assert.Equal(0, _h.Renderer.Calls);
    }

    [Fact]
    public void CodaCorrotta_MessaDaParte_PartoneVuota()
    {
        File.WriteAllText(_h.StateFile, "{ non e' json");

        ExportQueue queue = _h.Reopen();

        Assert.NotNull(queue.LoadWarning);
        Assert.Empty(queue.Jobs());
        Assert.True(File.Exists(_h.StateFile + ".corrotto"));
    }

    [Fact]
    public async Task Persistenza_StatoSalvatoERicaricato_UgualeEScrittoAtomicamente()
    {
        ExportJob local = _h.LocalJob("{project}", sequence: 1);
        ExportJob youtube = _h.Job(new YouTubeDestination { Title = "Notte {notte}", Privacy = "private" }, sequence: 2);
        Assert.True(_h.Enqueue(local).Ok);
        Assert.True(_h.Enqueue(youtube).Ok);
        List<ExportJob> before = _h.Queue.Jobs().ToList();

        ExportQueue reopened = _h.Reopen();

        Assert.Equal(before, reopened.Jobs());
        Assert.Empty(Directory.GetFiles(_h.Tmp.Root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_h.Tmp.Root, ".*.tmp", SearchOption.AllDirectories));

        await reopened.RunPendingAsync(CancellationToken.None);
        Assert.Equal(ExportJobState.Succeeded, reopened.Find(local.Id)!.State);
    }

    [Fact]
    public void FileVersioneFutura_NonSiLegge()
    {
        File.WriteAllText(_h.StateFile, """{ "version": 99, "jobs": [] }""");

        ExportQueue queue = _h.Reopen();

        Assert.NotNull(queue.LoadWarning);
        Assert.Empty(queue.Jobs());
    }

    [Fact]
    public async Task Segreti_NonFinisconoNeiLogNeiMessaggiNeLaCoda()
    {
        const string fakeToken = "SEGRETO-FINTO-123";
        const string fakeBotToken = "123456789:SEGRETO_FINTO_TG_ABCDEFGHIJKLMNOP";
        _h.YouTube.Behavior = (request, progress, ct) =>
            throw new ExportTransportException(
                $"token={fakeToken} rete caduta in C:\\Users\\robert\\dati\\cartella\\video.json");
        _h.Telegram.Behavior = (chatId, file, progress, ct) =>
            throw new ExportTransportException($"https://api.telegram.org/bot{fakeBotToken}/sendVideo caduta");

        ExportJob youtube = _h.Job(new YouTubeDestination { Title = "ok", Privacy = "private" }, sequence: 1);
        ExportJob telegram = _h.Job(new TelegramDestination { ChatId = "42" }, sequence: 2);
        ExportJob local = _h.LocalJob("{project}", OverwritePolicy.Fail, sequence: 3);
        File.WriteAllText(Path.Combine(_h.Folder, "Notte di prova.mp4"), "gia' presente");

        await _h.RunOnceAsync(youtube);
        await _h.RunOnceAsync(telegram);
        await _h.RunOnceAsync(local);

        string state = File.ReadAllText(_h.StateFile);
        string log = string.Join("\n", _h.Lines);
        string errors = string.Join("\n", _h.Queue.Jobs().Select(j => j.Error));

        foreach (string text in new[] { state, log, errors })
        {
            Assert.DoesNotContain(fakeToken, text);
            Assert.DoesNotContain("SEGRETO_FINTO_TG", text);
            Assert.DoesNotContain("C:\\Users", text);
            Assert.DoesNotContain("robert", text);
        }

        Assert.DoesNotContain(_h.Folder, errors);
        Assert.Contains("[segreto]", log);
    }

    [Fact]
    public void Redazione_CopreTokenBotEPercorsi()
    {
        Assert.Equal("token=[segreto] fine", ExportRedactor.Redact("token=ABC123 fine"));
        Assert.Equal("[segreto]", ExportRedactor.Redact("123456789:AAAAAAAAAAAAAAAAAAAAAAAA"));
        Assert.Equal("bot[segreto]/send", ExportRedactor.Redact("bot123456789:AAAAAAAAAAAAAAAAAAAAAAAA/send"));
        Assert.Equal("file [percorso]", ExportRedactor.Redact("file /home/robert/dati/x.mp4"));
        Assert.Equal("Bearer [segreto]", ExportRedactor.Redact("Bearer abc.def"));
        Assert.Equal(string.Empty, ExportRedactor.Redact(null));
    }
}
