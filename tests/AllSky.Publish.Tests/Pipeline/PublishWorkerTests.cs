using System.Diagnostics;
using AllSky.Core;
using AllSky.Imaging.Jpeg;
using Xunit;

namespace AllSky.Publish.Tests.Pipeline;

/// <summary>Test del PublishWorker guidati da RunOnceAsync: nessun ciclo reale, orologio e attese finti.</summary>
public sealed class PublishWorkerTests : IDisposable
{
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "allsky-worker-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WorkerFakeClock _clock = new(WorkerData.Start);
    private readonly WorkerFakeDelay _delay;
    private readonly WorkerFakeSender _sender = new();
    private readonly List<string> _logs = new();

    public PublishWorkerTests()
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

    private DiskQueue NewQueue(int maxItems = 20) => new(_folder, _clock, maxItems);

    private PublishWorker NewWorker(PublishSettings settings, DiskQueue? queue = null, RetryPolicy? retry = null) =>
        new(
            settings,
            _clock,
            _delay,
            _sender,
            new WebImagePreparer(new BaselineJpegEncoder()),
            queue ?? NewQueue(),
            retry ?? new RetryPolicy(new WorkerFixedJitter()),
            message =>
            {
                lock (_logs)
                {
                    _logs.Add(message);
                }
            });

    private string[] Logs
    {
        get
        {
            lock (_logs)
            {
                return _logs.ToArray();
            }
        }
    }

    private static string CaptureIdAt(int secondsAfterStart) =>
        CaptureIdFactory.Create("cam-01", WorkerData.Start.AddSeconds(secondsAfterStart));

    [Fact]
    public async Task Intervallo_minimo_di_15_secondi_e_rispettato()
    {
        PublishWorker worker = NewWorker(WorkerData.Settings(intervalSeconds: 15));

        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);

        _clock.Advance(TimeSpan.FromSeconds(10));
        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 2));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);

        _clock.Advance(TimeSpan.FromSeconds(5));
        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 3));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(2, _sender.CallCount);
    }

    [Fact]
    public async Task Intervallo_sotto_il_minimo_rende_le_impostazioni_non_valide_e_nulla_viene_inviato()
    {
        // Il validatore rifiuta gli intervalli sotto 15 s: per regola le impostazioni sono disattive.
        PublishWorker worker = NewWorker(WorkerData.Settings(intervalSeconds: 5));
        Assert.Equal(PublishState.Disabled, worker.GetStatus().State);

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        _clock.Advance(TimeSpan.FromSeconds(60));
        await worker.RunOnceAsync(NoCancel);

        Assert.Equal(0, _sender.CallCount);
    }

    [Fact]
    public async Task Intervallo_di_60_secondi_viene_rispettato()
    {
        PublishWorker worker = NewWorker(WorkerData.Settings(intervalSeconds: 60));

        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);

        _clock.Advance(TimeSpan.FromSeconds(30));
        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 2));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);

        _clock.Advance(TimeSpan.FromSeconds(30));
        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 3));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(2, _sender.CallCount);
    }

    [Fact]
    public async Task Immagine_identica_alla_precedente_viene_saltata()
    {
        PublishWorker worker = NewWorker(WorkerData.Settings());

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 7));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);

        _clock.Advance(TimeSpan.FromSeconds(60));
        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 7));
        await worker.RunOnceAsync(NoCancel);

        Assert.Equal(1, _sender.CallCount);
        Assert.Equal(0, worker.GetStatus().QueueCount);
    }

    [Fact]
    public async Task Camera_ferma_non_invia_e_logga_una_riga_ogni_dieci_tick()
    {
        PublishWorker worker = NewWorker(WorkerData.Settings());

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);

        for (int i = 0; i < 10; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(60));
            await worker.RunOnceAsync(NoCancel);
        }

        Assert.Equal(1, _sender.CallCount);
        Assert.Equal(1, Logs.Count(line => line.Contains("nessuna nuova immagine", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Interruzione_lunga_limita_la_coda_a_20_e_dopo_invia_solo_gli_ultimi_3()
    {
        // Backoff iniziale di 10 minuti: durante l'attesa la coda cresce fino al limite di 20.
        var retry = new RetryPolicy(new WorkerFixedJitter(), baseDelay: TimeSpan.FromMinutes(10));
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.Transient, "rete", "Connessione interrotta."));
        DiskQueue queue = NewQueue();
        PublishWorker worker = NewWorker(WorkerData.Settings(intervalSeconds: 15), queue, retry);

        int maxCount = 0;
        for (int n = 0; n < 30; n++)
        {
            worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: n + 1));
            await worker.RunOnceAsync(NoCancel);
            maxCount = Math.Max(maxCount, queue.Count);
            _clock.Advance(TimeSpan.FromSeconds(15));
        }

        Assert.True(maxCount <= 20, "La coda ha superato 20 elementi.");
        Assert.Equal(20, queue.Count);
        Assert.Equal(1, _sender.CallCount);
        Assert.Equal(WorkerData.Start.AddSeconds(150), queue.List()[0].CapturedAtUtc);
        Assert.Equal(10, worker.GetStatus().DiscardedTotal);

        // Rete ripristinata: solo gli ultimi 3, in ordine cronologico.
        _sender.SetResponder(_ => WorkerFakeSender.Ok());
        _clock.Advance(TimeSpan.FromSeconds(150));
        await worker.RunOnceAsync(NoCancel);

        string[] sent = _sender.Calls.Skip(1).Select(c => c.CaptureId).ToArray();
        Assert.Equal(new[] { CaptureIdAt(405), CaptureIdAt(420), CaptureIdAt(435) }, sent);

        PublishStatus status = worker.GetStatus();
        Assert.Equal(3, status.SentTotal);
        Assert.Equal(27, status.DiscardedTotal);
        Assert.Equal(0, status.QueueCount);
        Assert.Equal(PublishState.Idle, status.State);
    }

    [Fact]
    public async Task Backoff_30_60_120_secondi_con_cancello_sull_invio()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.Transient, "rete", "Connessione interrotta."));
        PublishWorker worker = NewWorker(WorkerData.Settings());

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        PublishStatus first = worker.GetStatus();
        Assert.Equal(PublishState.Backoff, first.State);
        Assert.Equal(WorkerData.Start.AddSeconds(30), first.NextAttemptUtc!.Value);
        Assert.Equal(1, _sender.CallCount);

        // Il tick arriva prima del prossimo tentativo: nessun invio.
        _clock.Advance(TimeSpan.FromSeconds(15));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);

        _clock.Advance(TimeSpan.FromSeconds(15));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(2, _sender.CallCount);
        Assert.Equal(WorkerData.Start.AddSeconds(90), worker.GetStatus().NextAttemptUtc!.Value);

        _clock.Advance(TimeSpan.FromSeconds(60));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(3, _sender.CallCount);
        Assert.Equal(WorkerData.Start.AddSeconds(210), worker.GetStatus().NextAttemptUtc!.Value);
    }

    [Fact]
    public async Task Retry_After_del_429_viene_rispettato_come_minimo()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.RateLimited, "limite", "Troppe richieste.", TimeSpan.FromSeconds(300), 429));
        PublishWorker worker = NewWorker(WorkerData.Settings());

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);

        PublishStatus status = worker.GetStatus();
        Assert.Equal(PublishState.Backoff, status.State);
        Assert.Equal(WorkerData.Start.AddSeconds(300), status.NextAttemptUtc!.Value);
        Assert.Equal("limite", status.LastErrorCode);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Errore_di_autorizzazione_sospende_finche_le_credenziali_non_cambiano(int httpStatus)
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.AuthError, "http_" + httpStatus, "Accesso negato dal server.", status: httpStatus));
        PublishWorker worker = NewWorker(WorkerData.Settings(token: "TOKEN-VECCHIO-1"));

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        PublishStatus suspended = worker.GetStatus();
        Assert.Equal(PublishState.Suspended, suspended.State);
        Assert.Null(suspended.NextAttemptUtc);

        // Il tempo passa e arrivano frame nuovi: nessun invio.
        _clock.Advance(TimeSpan.FromHours(1));
        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 2));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);

        // Stesse credenziali (cambia solo l'intervallo): resta sospeso.
        worker.ApplySettings(WorkerData.Settings(token: "TOKEN-VECCHIO-1", intervalSeconds: 120));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(PublishState.Suspended, worker.GetStatus().State);
        Assert.Equal(1, _sender.CallCount);

        // Credenziali nuove: riprende e invia.
        _sender.SetResponder(_ => WorkerFakeSender.Ok());
        worker.ApplySettings(WorkerData.Settings(token: "TOKEN-NUOVO-2", intervalSeconds: 120));
        Assert.Equal(PublishState.Idle, worker.GetStatus().State);
        await worker.RunOnceAsync(NoCancel);

        PublishStatus resumed = worker.GetStatus();
        Assert.Equal(3, _sender.CallCount);
        Assert.Equal(2, resumed.SentTotal);
        Assert.Equal(0, resumed.QueueCount);
    }

    [Fact]
    public async Task Test_connessione_riuscito_riprende_l_invio_sospeso_senza_cambiare_credenziali()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.AuthError, "http_401", "Accesso negato dal server.", status: 401));
        PublishWorker worker = NewWorker(WorkerData.Settings(token: "TOKEN-STESSO-1"));

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(PublishState.Suspended, worker.GetStatus().State);
        Assert.Equal(1, _sender.CallCount);

        // Il server viene corretto: stesse credenziali, test di connessione riuscito.
        _sender.SetTestResponder(_ => Task.FromResult(new TestResult(true, "ok", "Connessione riuscita.", 200)));
        TestResult test = await worker.TestConnectionAsync(null, NoCancel);
        Assert.True(test.Ok);

        PublishStatus resumed = worker.GetStatus();
        Assert.Equal(PublishState.Idle, resumed.State);
        Assert.Null(resumed.NextAttemptUtc);
        Assert.Equal(1, resumed.QueueCount);
        Assert.Contains("Connessione verificata: invio ripreso.", Logs);

        // Al giro successivo il frame rimasto in coda viene inviato.
        _sender.SetResponder(_ => WorkerFakeSender.Ok());
        _clock.Advance(TimeSpan.FromSeconds(60));
        await worker.RunOnceAsync(NoCancel);

        Assert.Equal(2, _sender.CallCount);
        Assert.Equal(CaptureIdAt(0), _sender.Calls[1].CaptureId);
        Assert.Equal(1, worker.GetStatus().SentTotal);
        Assert.Equal(0, worker.GetStatus().QueueCount);
    }

    [Fact]
    public async Task Test_connessione_riuscito_con_impostazioni_diverse_non_riprende_il_sospeso()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.AuthError, "http_401", "Accesso negato dal server.", status: 401));
        PublishWorker worker = NewWorker(WorkerData.Settings(token: "TOKEN-VECCHIO-3"));

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(PublishState.Suspended, worker.GetStatus().State);

        // Il test usa altre impostazioni (override): riuscito, ma il worker non deve riprendere con le impostazioni correnti.
        _sender.SetTestResponder(_ => Task.FromResult(new TestResult(true, "ok", "Connessione riuscita.", 200)));
        TestResult test = await worker.TestConnectionAsync(WorkerData.Settings(token: "TOKEN-NUOVO-3"), NoCancel);
        Assert.True(test.Ok);

        Assert.Equal(PublishState.Suspended, worker.GetStatus().State);
        Assert.DoesNotContain("Connessione verificata: invio ripreso.", Logs);
    }

    [Fact]
    public async Task Test_connessione_fallito_lascia_il_worker_sospeso()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.AuthError, "http_403", "Accesso negato dal server.", status: 403));
        PublishWorker worker = NewWorker(WorkerData.Settings(token: "TOKEN-STESSO-2"));

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(PublishState.Suspended, worker.GetStatus().State);

        _sender.SetTestResponder(_ => Task.FromResult(new TestResult(false, "http_403", "Accesso negato dal server.", 403)));
        TestResult test = await worker.TestConnectionAsync(null, NoCancel);
        Assert.False(test.Ok);
        Assert.Equal(PublishState.Suspended, worker.GetStatus().State);
        Assert.DoesNotContain("Connessione verificata: invio ripreso.", Logs);

        // Resta sospeso: nessun invio nemmeno al giro successivo.
        _sender.SetResponder(_ => WorkerFakeSender.Ok());
        _clock.Advance(TimeSpan.FromSeconds(60));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);
        Assert.Equal(PublishState.Suspended, worker.GetStatus().State);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(413)]
    [InlineData(415)]
    public async Task Errore_permanente_scarta_l_elemento_senza_riprovarlo_e_invia_il_successivo(int httpStatus)
    {
        bool outage = true;
        DateTimeOffset first = WorkerData.Start;
        _sender.SetResponder(item =>
        {
            if (outage)
            {
                return WorkerFakeSender.Fail(SendOutcome.Transient, "rete", "Connessione interrotta.");
            }

            return item.CapturedAtUtc == first
                ? WorkerFakeSender.Fail(SendOutcome.Permanent, "http_" + httpStatus, "Richiesta rifiutata dal server.", status: httpStatus)
                : WorkerFakeSender.Ok();
        });
        DiskQueue queue = NewQueue();
        PublishWorker worker = NewWorker(WorkerData.Settings(intervalSeconds: 15), queue);

        worker.Offer(WorkerData.Frame(first, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        _clock.Advance(TimeSpan.FromSeconds(15));
        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 2));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(2, queue.Count);

        outage = false;
        _clock.Advance(TimeSpan.FromSeconds(15));
        await worker.RunOnceAsync(NoCancel);

        PublishStatus status = worker.GetStatus();
        Assert.Equal(1, status.SentTotal);
        Assert.Equal(1, status.DiscardedTotal);
        Assert.Equal(0, status.QueueCount);
        Assert.Equal("http_" + httpStatus, status.LastErrorCode);

        // Il primo elemento e' stato inviato due volte in tutto (tentativo transitorio e rifiuto), mai ritentato dopo il rifiuto.
        Assert.Equal(2, _sender.Calls.Count(c => c.CaptureId == CaptureIdAt(0)));
        Assert.Equal(1, _sender.Calls.Count(c => c.CaptureId == CaptureIdAt(15)));

        int callsBefore = _sender.CallCount;
        _clock.Advance(TimeSpan.FromHours(1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(callsBefore, _sender.CallCount);
    }

    [Fact]
    public async Task Errore_temporaneo_non_perde_gli_elementi_in_coda()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.Transient, "rete", "Connessione interrotta."));
        DiskQueue queue = NewQueue();
        PublishWorker worker = NewWorker(WorkerData.Settings(intervalSeconds: 15), queue);

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        _clock.Advance(TimeSpan.FromSeconds(15));
        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 2));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(2, worker.GetStatus().QueueCount);

        _sender.SetResponder(_ => WorkerFakeSender.Ok());
        _clock.Advance(TimeSpan.FromSeconds(15));
        await worker.RunOnceAsync(NoCancel);

        Assert.Equal(new[] { CaptureIdAt(0), CaptureIdAt(15) }, _sender.Calls.Skip(1).Select(c => c.CaptureId).ToArray());
        Assert.Equal(2, worker.GetStatus().SentTotal);
        Assert.Equal(0, worker.GetStatus().QueueCount);
    }

    [Fact]
    public async Task Riavvio_mantiene_il_captureId_e_non_duplica_la_coda()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.Transient, "rete", "Connessione interrotta."));
        var firstQueue = NewQueue();
        PublishWorker first = NewWorker(WorkerData.Settings(), firstQueue);
        first.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await first.RunOnceAsync(NoCancel);
        Assert.Equal(1, firstQueue.Count);
        await first.StopAsync();

        // Nuova istanza sulla stessa cartella e stesso frame.
        var secondQueue = NewQueue();
        Assert.Equal(1, secondQueue.Count);
        _sender.SetResponder(_ => WorkerFakeSender.Ok());
        PublishWorker second = NewWorker(WorkerData.Settings(), secondQueue);
        second.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await second.RunOnceAsync(NoCancel);

        Assert.Equal(0, secondQueue.Count);
        Assert.Equal(2, _sender.CallCount);
        string expected = CaptureIdFactory.Create("cam-01", WorkerData.Start);
        Assert.True(_sender.Calls.All(c => c.CaptureId == expected), "Il captureId non e' stabile dopo il riavvio.");
        Assert.Equal(1, second.GetStatus().SentTotal);
    }

    [Fact]
    public async Task StatusChanged_viene_emesso_una_volta_per_cambio_e_ignora_gli_handler_difettosi()
    {
        PublishWorker worker = NewWorker(WorkerData.Settings());
        var events = new List<PublishStatus>();
        worker.StatusChanged += _ => throw new InvalidOperationException("handler difettoso");
        worker.StatusChanged += events.Add;

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);

        Assert.True(events.Count >= 2, "Attesi almeno due eventi (Sending, poi Idle).");
        for (int i = 1; i < events.Count; i++)
        {
            Assert.NotEqual(events[i - 1], events[i]);
        }

        Assert.Equal(worker.GetStatus(), events[events.Count - 1]);

        int before = events.Count;
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(before, events.Count);
    }

    [Fact]
    public async Task Offer_non_blocca_anche_se_l_invio_non_completa_mai()
    {
        var never = new TaskCompletionSource<SendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sender.SetAsyncResponder(_ => never.Task);
        PublishWorker worker = NewWorker(WorkerData.Settings());

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        Task run = worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);
        Assert.False(run.IsCompleted);

        var watch = Stopwatch.StartNew();
        worker.Offer(WorkerData.Frame(WorkerData.Start.AddSeconds(15), seed: 2));
        worker.Offer(WorkerData.Frame(WorkerData.Start.AddSeconds(30), seed: 3));
        worker.GetStatus();
        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), "Offer ha impiegato troppo tempo.");

        never.SetResult(WorkerFakeSender.Ok());
        await run;
    }

    [Fact]
    public async Task Pubblicazione_disattivata_non_acquisisce_ne_invia()
    {
        PublishWorker worker = NewWorker(WorkerData.Settings(enabled: false));

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(PublishState.Disabled, worker.GetStatus().State);
        Assert.Equal(0, _sender.CallCount);

        worker.ApplySettings(WorkerData.Settings(enabled: true));
        Assert.Equal(PublishState.Idle, worker.GetStatus().State);

        _clock.Advance(TimeSpan.FromSeconds(60));
        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 2));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, _sender.CallCount);
    }

    [Fact]
    public async Task Impostazioni_non_valide_restano_disattivate()
    {
        PublishWorker worker = NewWorker(WorkerData.Settings() with { ServerUrl = "http://remote.test" });
        Assert.Equal(PublishState.Disabled, worker.GetStatus().State);

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(0, _sender.CallCount);
    }

    [Fact]
    public async Task Nessun_segreto_in_log_stato_eccezioni_ne_risultato_del_test()
    {
        const string token = "TOKEN-SEGRETO-123";
        const string extra = "EXTRA-SEGRETO-456";
        PublishSettings settings = WorkerData.Settings(token) with
        {
            ExtraHeaderName = "X-Chiave",
            ExtraHeaderValue = new Secret(extra),
        };
        PublishWorker worker = NewWorker(settings);

        // Il sender lancia un'eccezione che contiene il token.
        _sender.SetAsyncResponder(_ => throw new InvalidOperationException("boom " + token));
        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);

        // Poi il server risponde con un testo che contiene token e header extra: deve essere sanificato.
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.Transient, "http_500", "Errore con " + token + " e " + extra + " nel testo.", status: 500));
        _clock.Advance(TimeSpan.FromSeconds(60));
        worker.Offer(WorkerData.Frame(_clock.UtcNow, seed: 2));
        await worker.RunOnceAsync(NoCancel);

        _sender.SetTestResponder(_ => Task.FromResult(new TestResult(false, "http_500", "Risposta " + token + " ricevuta", 500)));
        TestResult test = await worker.TestConnectionAsync(null, NoCancel);

        string logs = string.Join("\n", Logs);
        string status = worker.GetStatus().ToString();
        string everything = logs + "\n" + status + "\n" + test.Message + "\n" + test.Code;

        Assert.DoesNotContain(token, everything);
        Assert.DoesNotContain(extra, everything);
        Assert.DoesNotContain("uplink.test", logs);
        Assert.DoesNotContain("boom", logs);
        Assert.Contains("***", worker.GetStatus().LastError!);
    }

    [Fact]
    public async Task Immagine_inviata_e_entro_300_KB_con_lato_lungo_entro_1200_con_encoder_reale()
    {
        PublishWorker worker = NewWorker(WorkerData.Settings());

        worker.Offer(WorkerData.BigFrame(WorkerData.Start));
        await worker.RunOnceAsync(NoCancel);

        WorkerSendCall call = _sender.Calls.Single();
        Assert.True(call.Jpeg.Length <= WebImagePreparer.TargetBytes, $"JPEG troppo pesante: {call.Jpeg.Length} byte.");
        (int width, int height) = WorkerJpeg.Size(call.Jpeg);
        Assert.True(Math.Max(width, height) <= 1200, $"Lato lungo troppo grande: {Math.Max(width, height)} px.");
        Assert.InRange(Math.Max(width, height), 320, 1200);
    }

    [Fact]
    public async Task Immagine_mancante_su_disco_viene_rimossa_senza_invio()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.Transient, "rete", "Connessione interrotta."));
        DiskQueue queue = NewQueue();
        PublishWorker worker = NewWorker(WorkerData.Settings(), queue);

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, queue.Count);

        foreach (string jpg in Directory.GetFiles(_folder, "*.jpg"))
        {
            File.Delete(jpg);
        }

        _sender.SetResponder(_ => WorkerFakeSender.Ok());
        _clock.Advance(TimeSpan.FromSeconds(60));
        await worker.RunOnceAsync(NoCancel);

        Assert.Equal(1, _sender.CallCount);
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, worker.GetStatus().SentTotal);
    }

    [Fact]
    public async Task Cambio_di_dispositivo_svuota_la_coda()
    {
        _sender.SetResponder(_ => WorkerFakeSender.Fail(SendOutcome.Transient, "rete", "Connessione interrotta."));
        DiskQueue queue = NewQueue();
        PublishWorker worker = NewWorker(WorkerData.Settings(), queue);

        worker.Offer(WorkerData.Frame(WorkerData.Start, seed: 1));
        await worker.RunOnceAsync(NoCancel);
        Assert.Equal(1, queue.Count);

        worker.ApplySettings(WorkerData.Settings(deviceId: "cam-02"));

        PublishStatus status = worker.GetStatus();
        Assert.Equal(0, queue.Count);
        Assert.Equal(0, status.QueueCount);
        Assert.Equal(1, status.DiscardedTotal);
    }
}
