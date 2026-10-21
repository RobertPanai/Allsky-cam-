using AllSky.Core;
using AllSky.Imaging.Jpeg;
using Xunit;

namespace AllSky.Publish.Tests.Pipeline;

/// <summary>Test del ciclo di sfondo (StartAsync/StopAsync) con attesa finta: nessun sleep reale.</summary>
public sealed class PublishWorkerLoopTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "allsky-worker-loop-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WorkerFakeClock _clock = new(WorkerData.Start);
    private readonly WorkerFakeDelay _delay;
    private readonly WorkerFakeSender _sender = new();
    private readonly List<string> _logs = new();

    public PublishWorkerLoopTests()
    {
        _delay = new WorkerFakeDelay(_clock);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // Cartella temporanea: il sistema la pulira' comunque.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private PublishWorker NewWorker(PublishSettings settings, DiskQueue? queue = null) =>
        new(
            settings,
            _clock,
            _delay,
            _sender,
            new WebImagePreparer(new BaselineJpegEncoder()),
            queue ?? new DiskQueue(_folder, _clock),
            new RetryPolicy(new WorkerFixedJitter()),
            message =>
            {
                lock (_logs)
                {
                    _logs.Add(message);
                }
            });

    [Fact]
    public async Task Start_e_Stop_non_lasciano_task_pendenti()
    {
        PublishWorker worker = NewWorker(WorkerData.Settings());

        await worker.StartAsync(CancellationToken.None);
        await worker.StartAsync(CancellationToken.None);
        Task? loop = worker.LoopTask;
        Assert.NotNull(loop);

        await WorkerWait.UntilAsync(() => _delay.Requests.Count >= 3);
        Assert.True(_delay.Requests.Count >= 3, "Il ciclo non ha dormito.");

        await worker.StopAsync();
        await worker.StopAsync();
        Assert.True(loop!.IsCompleted, "Il ciclo di sfondo e' ancora in esecuzione dopo StopAsync.");
    }

    [Fact]
    public async Task Pubblicazione_disattivata_controlla_le_impostazioni_ogni_secondo()
    {
        PublishWorker worker = NewWorker(WorkerData.Settings(enabled: false));

        await worker.StartAsync(CancellationToken.None);
        await WorkerWait.UntilAsync(() => _delay.Requests.Contains(TimeSpan.FromSeconds(1)));
        await worker.StopAsync();

        Assert.True(_delay.Requests.Contains(TimeSpan.FromSeconds(1)), "Il ciclo disattivato non controlla ogni secondo.");
        Assert.Equal(0, _sender.CallCount);
        Assert.Equal(PublishState.Disabled, worker.GetStatus().State);
    }

    [Fact]
    public async Task Ciclo_invia_l_immagine_e_lascia_la_coda_su_disco_allo_stop()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.Transient, "rete", "Connessione interrotta."));
        PublishWorker worker = NewWorker(WorkerData.Settings());
        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));

        await worker.StartAsync(CancellationToken.None);
        await WorkerWait.UntilAsync(() => _sender.CallCount >= 1);
        Assert.True(_sender.CallCount >= 1, "Il ciclo non ha tentato l'invio.");

        await worker.StopAsync();

        Assert.True(worker.LoopTask!.IsCompleted);
        Assert.Equal(1, Directory.GetFiles(_folder, "*.jpg").Length);
        Assert.Equal(1, Directory.GetFiles(_folder, "*.json").Length);
    }

    [Fact]
    public async Task StartAsync_riprende_un_publisher_sospeso()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.AuthError, "http_401", "Accesso negato dal server.", status: 401));
        PublishWorker worker = NewWorker(WorkerData.Settings());
        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));

        await worker.StartAsync(CancellationToken.None);
        await WorkerWait.UntilAsync(() => worker.GetStatus().State == PublishState.Suspended);
        Assert.Equal(PublishState.Suspended, worker.GetStatus().State);

        _sender.SetResponder(_ => WorkerFakeSender.Ok());
        await worker.StartAsync(CancellationToken.None);
        await WorkerWait.UntilAsync(() => worker.GetStatus().SentTotal >= 1);

        await worker.StopAsync();
        Assert.Equal(1, worker.GetStatus().SentTotal);
    }

    [Fact]
    public async Task Eccezione_imprevista_nel_ciclo_viene_registrata_e_il_ciclo_continua()
    {
        _delay.FailNextWith(new InvalidOperationException("guasto simulato"));
        PublishWorker worker = NewWorker(WorkerData.Settings());

        await worker.StartAsync(CancellationToken.None);
        await WorkerWait.UntilAsync(() => _delay.Requests.Count >= 3);
        await worker.StopAsync();

        string[] logs;
        lock (_logs)
        {
            logs = _logs.ToArray();
        }

        Assert.True(logs.Any(line => line.Contains("Errore interno", StringComparison.Ordinal)), "L'errore imprevisto non e' stato registrato.");
        Assert.True(_delay.Requests.Count >= 3, "Il ciclo non e' ripartito dopo l'errore.");
        Assert.True(_delay.Requests.Contains(TimeSpan.FromSeconds(5)), "Manca la pausa di 5 secondi dopo l'errore.");
    }
}
