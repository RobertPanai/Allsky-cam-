using AllSky.Telegram;
using AllSky.YouTube;

namespace AllSky.Studio;

/// <summary>Opzioni della coda, scelte dall'host.</summary>
/// <param name="StateFilePath">File JSON dello stato della coda. Lo sceglie l'host, accanto al progetto o nella cartella dati.</param>
/// <param name="WorkFolder">Cartella dei file temporanei per YouTube e Telegram. Deve essere scrivibile.</param>
/// <param name="Clock">Orologio, iniettabile per i test.</param>
/// <param name="LocalZone">Fuso per {data}, {ora} e {notte}.</param>
/// <param name="Location">Valore di {luogo} nei modelli di titolo e descrizione.</param>
/// <param name="MaxConcurrent">Lavori contemporanei, da 1 a 2.</param>
/// <param name="Log">Registro. Riceve testo già ripulito dai segreti.</param>
public sealed record ExportQueueOptions(
    string StateFilePath,
    string WorkFolder,
    IClock Clock,
    TimeZoneInfo LocalZone,
    string Location,
    int MaxConcurrent = 1,
    Action<string>? Log = null);

/// <summary>Le dipendenze esterne della coda, tutte iniettate.</summary>
public sealed record ExportServices(
    IVideoRenderer Renderer,
    IYouTubeUploader YouTube,
    ITelegramPublisher Telegram,
    IExportPlanResolver PlanResolver);

/// <summary>Esito di un comando sulla coda. Il messaggio è in italiano.</summary>
public sealed record QueueResult(bool Ok, string Message);

/// <summary>
/// Coda dei lavori di esportazione.
/// <list type="bullet">
/// <item>Un lavoro alla volta per impostazione (1 o 2). Lo stato è salvato a ogni cambiamento di stato, in modo atomico.</item>
/// <item>Annullamento e pausa cancellano il file temporaneo: non resta nulla di parziale.</item>
/// <item>Dopo un riavvio, un lavoro «In corso» torna in coda. Un lavoro «Completato» non riparte mai.</item>
/// <item>Un errore di rete durante un caricamento dà «Fallito» con «Riprova»; dopo tre tentativi consecutivi
/// falliti il lavoro si ferma.</item>
/// <item>Il progresso di un tentativo non decresce mai.</item>
/// </list>
/// </summary>
public sealed class ExportQueue
{
    /// <summary>Tentativi falliti consecutivi dopo i quali «Riprova» non è più offerto [S-scelta].</summary>
    public const int MaxConsecutiveFailures = 3;

    private readonly ExportQueueOptions _options;
    private readonly ExportServices _services;
    private readonly object _gate = new();
    private readonly List<ExportJob> _jobs = [];
    private readonly Dictionary<string, MontagePlan> _plans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RunControl> _runs = new(StringComparer.Ordinal);

    private ExportQueue(ExportQueueOptions options, ExportServices services)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        if (options.MaxConcurrent is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxConcurrent, "I lavori contemporanei devono essere 1 o 2.");
        }
    }

    /// <summary>Avviso di caricamento, per esempio «coda non leggibile, messa da parte». Null se tutto è andato bene.</summary>
    public string? LoadWarning { get; private set; }

    /// <summary>Cambio di stato o di avanzamento di un lavoro. Chiamato fuori dal blocco di sincronizzazione.</summary>
    public event Action<ExportJob>? JobChanged;

    /// <summary>
    /// Apre la coda leggendo lo stato salvato. I lavori «In corso» tornano in coda e i file temporanei rimasti
    /// vengono cancellati. Una coda illeggibile viene messa da parte (file «.corrotto») e si riparte vuoti.
    /// </summary>
    public static ExportQueue Open(ExportQueueOptions options, ExportServices services)
    {
        var queue = new ExportQueue(options, services);
        queue.LoadState();
        return queue;
    }

    /// <summary>Copia dell'elenco dei lavori, nell'ordine di creazione.</summary>
    public IReadOnlyList<ExportJob> Jobs()
    {
        lock (_gate)
        {
            return _jobs.ToList();
        }
    }

    /// <summary>Lavoro con quell'identificativo, o null.</summary>
    public ExportJob? Find(string id)
    {
        lock (_gate)
        {
            return _jobs.FirstOrDefault(j => j.Id == id);
        }
    }

    /// <summary>
    /// Accoda un lavoro. Un lavoro «Completato» non si accoda di nuovo; un lavoro «Fallito» o «Annullato» viene
    /// rimesso in coda da zero, su richiesta esplicita.
    /// </summary>
    /// <param name="job">Lavoro costruito con <see cref="ExportJobFactory"/>.</param>
    /// <param name="plan">Piano del progetto, già calcolato.</param>
    public QueueResult Enqueue(ExportJob job, MontagePlan plan)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(plan);

        IReadOnlyList<string> errors = ExportValidator.ValidateTarget(new ExportTarget
        {
            Preset = job.Preset,
            Destination = job.Destination,
        });
        if (errors.Count > 0)
        {
            return new QueueResult(false, "Esportazione non valida: " + string.Join(" ", errors.Take(3)));
        }

        if (job.PlanFingerprint != plan.Fingerprint())
        {
            return new QueueResult(false, "Il piano non corrisponde all'esportazione: riaccodarla dal progetto.");
        }

        ExportJob queued;
        lock (_gate)
        {
            int index = _jobs.FindIndex(j => j.Id == job.Id);
            if (index >= 0)
            {
                switch (_jobs[index].State)
                {
                    case ExportJobState.Succeeded:
                        return new QueueResult(false, "Questa esportazione è già completata e non riparte.");
                    case ExportJobState.Queued:
                        return new QueueResult(false, "Questa esportazione è già in coda.");
                    case ExportJobState.Running:
                        return new QueueResult(false, "Questa esportazione è già in corso.");
                    case ExportJobState.Paused:
                        return new QueueResult(false, "Questa esportazione è in pausa: riprenderla dalla coda.");
                }
            }

            queued = Reset(job);
            if (index >= 0)
            {
                _jobs[index] = queued;
            }
            else
            {
                _jobs.Add(queued);
            }

            _plans[job.Id] = plan;
            Persist();
        }

        Notify(queued);
        return new QueueResult(true, "Esportazione accodata.");
    }

    /// <summary>Annulla un lavoro. Se è in corso, l'annullamento arriva alla fine del passo in corso e cancella i temporanei.</summary>
    public QueueResult Cancel(string id)
    {
        ExportJob? changed = null;
        QueueResult result;
        lock (_gate)
        {
            int index = IndexOf(id);
            if (index < 0)
            {
                return new QueueResult(false, "Lavoro non trovato.");
            }

            ExportJob job = _jobs[index];
            switch (job.State)
            {
                case ExportJobState.Queued:
                case ExportJobState.Paused:
                    _jobs[index] = job with { State = ExportJobState.Cancelled, Error = null, Retryable = false };
                    changed = _jobs[index];
                    Persist();
                    result = new QueueResult(true, "Esportazione annullata.");
                    break;
                case ExportJobState.Running:
                    _runs[id].RequestStop(RunStop.Cancel);
                    result = new QueueResult(true, "Annullamento in corso.");
                    break;
                default:
                    return new QueueResult(false, "L'esportazione è già conclusa.");
            }
        }

        if (changed is not null)
        {
            Notify(changed);
        }

        return result;
    }

    /// <summary>Sospende un lavoro. In corso, il passo viene fermato e il lavoro ripartirà da zero alla ripresa.</summary>
    public QueueResult Pause(string id)
    {
        ExportJob? changed = null;
        QueueResult result;
        lock (_gate)
        {
            int index = IndexOf(id);
            if (index < 0)
            {
                return new QueueResult(false, "Lavoro non trovato.");
            }

            ExportJob job = _jobs[index];
            switch (job.State)
            {
                case ExportJobState.Queued:
                    _jobs[index] = job with { State = ExportJobState.Paused };
                    changed = _jobs[index];
                    Persist();
                    result = new QueueResult(true, "Esportazione in pausa.");
                    break;
                case ExportJobState.Running:
                    _runs[id].RequestStop(RunStop.Pause);
                    result = new QueueResult(true, "Sospensione in corso.");
                    break;
                default:
                    return new QueueResult(false, "Solo un'esportazione in attesa o in corso si può sospendere.");
            }
        }

        if (changed is not null)
        {
            Notify(changed);
        }

        return result;
    }

    /// <summary>Riprende un lavoro in pausa: torna in coda e ripartirà da zero.</summary>
    public QueueResult Resume(string id)
    {
        ExportJob? changed;
        lock (_gate)
        {
            int index = IndexOf(id);
            if (index < 0)
            {
                return new QueueResult(false, "Lavoro non trovato.");
            }

            ExportJob job = _jobs[index];
            if (job.State != ExportJobState.Paused)
            {
                return new QueueResult(false, "L'esportazione non è in pausa.");
            }

            _jobs[index] = job with { State = ExportJobState.Queued, Progress = 0, Error = null };
            changed = _jobs[index];
            Persist();
        }

        Notify(changed);
        return new QueueResult(true, "Esportazione ripresa.");
    }

    /// <summary>«Riprova» per un errore di rete. Dopo <see cref="MaxConsecutiveFailures"/> tentativi falliti di seguito, no.</summary>
    public QueueResult Retry(string id)
    {
        ExportJob? changed;
        lock (_gate)
        {
            int index = IndexOf(id);
            if (index < 0)
            {
                return new QueueResult(false, "Lavoro non trovato.");
            }

            ExportJob job = _jobs[index];
            if (job.State != ExportJobState.Failed)
            {
                return new QueueResult(false, "Solo un'esportazione fallita si può riprovare.");
            }

            if (job.ConsecutiveFailures >= MaxConsecutiveFailures)
            {
                return new QueueResult(false,
                    $"Tentativi esauriti: dopo {MaxConsecutiveFailures} tentativi consecutivi il lavoro si ferma. Riaccodarlo dal progetto dopo aver verificato la connessione.");
            }

            if (!job.Retryable)
            {
                return new QueueResult(false, "Questo errore non si risolve riprovando: correggere la causa e riaccodare l'esportazione.");
            }

            _jobs[index] = job with { State = ExportJobState.Queued, Progress = 0, Error = null, Retryable = false };
            changed = _jobs[index];
            Persist();
        }

        Notify(changed);
        return new QueueResult(true, "Nuovo tentativo in coda.");
    }

    /// <summary>
    /// Esegue i lavori in coda, fino a esaurirli o fino all'annullamento di <paramref name="cancellationToken"/>.
    /// Con l'annullamento dell'intera coda, i lavori in corso tornano in coda (come dopo un riavvio).
    /// </summary>
    public async Task RunPendingAsync(CancellationToken cancellationToken)
    {
        var inFlight = new List<Task>();
        while (!cancellationToken.IsCancellationRequested)
        {
            (string Id, RunControl Run, ExportJob Job)? claim;
            lock (_gate)
            {
                claim = ClaimNextLocked();
            }

            if (claim is null)
            {
                if (inFlight.Count == 0)
                {
                    break;
                }

                await Task.WhenAny(inFlight).ConfigureAwait(false);
                inFlight.RemoveAll(t => t.IsCompleted);
                continue;
            }

            Notify(claim.Value.Job);
            inFlight.Add(RunOneAsync(claim.Value.Id, claim.Value.Run, cancellationToken));
        }

        if (inFlight.Count > 0)
        {
            await Task.WhenAll(inFlight).ConfigureAwait(false);
        }
    }

    private void LoadState()
    {
        IReadOnlyList<ExportJob> stored;
        try
        {
            stored = ExportQueueStore.Read(_options.StateFilePath);
        }
        catch (StudioException ex)
        {
            Log("Coda non leggibile: " + ex.Message);
            LoadWarning = "La coda salvata non era leggibile: è stata messa da parte e la coda riparte vuota.";
            MoveAside(_options.StateFilePath);
            return;
        }

        lock (_gate)
        {
            foreach (ExportJob job in stored)
            {
                _jobs.Add(Recover(job));
            }

            Persist();
        }
    }

    /// <summary>Stato di un lavoro letto dal file: i lavori attivi vengono controllati, quelli conclusi restano così.</summary>
    private ExportJob Recover(ExportJob job)
    {
        if (job.State is ExportJobState.Succeeded or ExportJobState.Failed or ExportJobState.Cancelled)
        {
            return job;
        }

        IReadOnlyList<string> errors = ExportValidator.ValidateTarget(new ExportTarget
        {
            Preset = job.Preset,
            Destination = job.Destination,
        });
        if (errors.Count > 0)
        {
            Log($"Lavoro {job.Id} non valido nel file di coda.");
            return job with
            {
                State = ExportJobState.Failed,
                Error = "Voce non valida nel file di coda: eliminarla e riaccodare il progetto.",
                Retryable = false,
                Progress = 0,
            };
        }

        if (job.State != ExportJobState.Running)
        {
            return job;
        }

        CleanupFiles(job);
        Log($"Lavoro {job.Id} interrotto da un riavvio: torna in coda.");
        return job with
        {
            State = ExportJobState.Queued,
            Progress = 0,
            Error = null,
            InterruptedRuns = job.InterruptedRuns + 1,
        };
    }

    private (string Id, RunControl Run, ExportJob Job)? ClaimNextLocked()
    {
        if (_runs.Count >= _options.MaxConcurrent)
        {
            return null;
        }

        int index = _jobs.FindIndex(j => j.State == ExportJobState.Queued && !_runs.ContainsKey(j.Id));
        if (index < 0)
        {
            return null;
        }

        ExportJob job = _jobs[index] with
        {
            State = ExportJobState.Running,
            Progress = 0,
            Error = null,
            Retryable = false,
            Attempts = _jobs[index].Attempts + 1,
        };
        _jobs[index] = job;
        var run = new RunControl();
        _runs[job.Id] = run;
        Persist();
        return (job.Id, run, job);
    }

    private async Task RunOneAsync(string id, RunControl run, CancellationToken shutdown)
    {
        ExportJob job = Find(id) ?? throw new InvalidOperationException("Lavoro sparito dalla coda.");
        Outcome outcome;
        string step = "il rendering";
        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(shutdown, run.Token);
            CancellationToken token = linked.Token;
            MontagePlan plan = await ResolvePlanAsync(job, token).ConfigureAwait(false);
            ExportPreset preset = job.Preset.Fps == 0 ? job.Preset with { Fps = plan.Fps } : job.Preset;

            string? output = null;
            switch (job.Destination)
            {
                case LocalFileDestination local:
                    output = await RunLocalAsync(job, local, plan, preset, token).ConfigureAwait(false);
                    break;

                case YouTubeDestination youtube:
                    step = "il caricamento su YouTube";
                    await RenderToWorkAsync(job, plan, preset, token).ConfigureAwait(false);
                    string work = WorkPathFor(job);
                    var request = new YouTubeUploadRequest(
                        work,
                        RenderTitle(youtube, plan),
                        RenderDescription(youtube, plan),
                        ParsePrivacy(youtube));
                    string videoId = await _services.YouTube.UploadAsync(request, Sink(job.Id, 0.9, 1.0), token).ConfigureAwait(false);
                    Log($"Lavoro {job.Id}: caricato su YouTube (video {videoId}).");
                    break;

                case TelegramDestination telegram:
                    step = "l'invio su Telegram";
                    if (!ExportValidator.TryParseChatId(telegram.ChatId, out long chatId))
                    {
                        throw new ExportStepException("L'identificativo della chat non è valido.");
                    }

                    await RenderToWorkAsync(job, plan, preset, token).ConfigureAwait(false);
                    string video = WorkPathFor(job);
                    if (new FileInfo(video).Length > TelegramLimits.FileMaxBytes)
                    {
                        throw new ExportStepException(
                            $"Il video supera il limite di {TelegramLimits.FileMaxBytes / 1_000_000} MB per i file inviati su Telegram: scegliere un preset con risoluzione o qualità più basse.");
                    }

                    string? caption = string.IsNullOrWhiteSpace(telegram.Caption) ? null : telegram.Caption;
                    await _services.Telegram.SendVideoAsync(chatId, video, caption, Sink(job.Id, 0.9, 1.0), token).ConfigureAwait(false);
                    Log($"Lavoro {job.Id}: inviato su Telegram.");
                    break;

                default:
                    throw new ExportStepException("Tipo di destinazione non riconosciuto.");
            }

            outcome = Outcome.Success(output);
        }
        catch (OperationCanceledException) when (run.StopRequested || shutdown.IsCancellationRequested)
        {
            outcome = run.Stop switch
            {
                RunStop.Cancel => Outcome.Cancelled(),
                RunStop.Pause => Outcome.Paused(),
                _ => Outcome.Interrupted(),
            };
        }
        catch (ExportTransportException ex)
        {
            Log($"Lavoro {job.Id}: connessione interrotta ({ex.GetType().Name}): {ex.Message}");
            int percent = (int)Math.Floor(Find(id)?.Progress * 100 ?? 0);
            outcome = Outcome.Failed(
                $"Connessione interrotta durante {step} (circa {percent}%): controllare la rete, poi premere «Riprova».",
                retryable: true);
        }
        catch (ExportStepException ex)
        {
            Log($"Lavoro {job.Id}: {ex.Message}");
            outcome = Outcome.Failed(ex.Message, ex.Retryable);
        }
        catch (Exception ex)
        {
            Log($"Lavoro {job.Id}: errore imprevisto ({ex.GetType().Name}): {ex.Message}");
            outcome = Outcome.Failed("Errore imprevisto durante l'esportazione: vedere il registro.", retryable: false);
        }
        finally
        {
            CleanupFiles(job);
        }

        Finish(id, run, outcome);
    }

    private async Task<string?> RunLocalAsync(ExportJob job, LocalFileDestination local, MontagePlan plan, ExportPreset preset,
        CancellationToken token)
    {
        if (!Directory.Exists(local.Folder))
        {
            throw new ExportStepException("La cartella di destinazione non esiste o non è raggiungibile.");
        }

        if (!ExportFileNames.TryRender(local.FileNameTemplate, job.ProjectName, TodayLocal(), job.Sequence,
                out string name, out string? nameError))
        {
            throw new ExportStepException("Nome del file non valido: " + nameError);
        }

        string partial = PartialPathFor(job);
        await _services.Renderer.RenderAsync(plan, preset, partial, Sink(job.Id, 0, 0.9), token).ConfigureAwait(false);
        EnsureRendered(partial);
        return PlaceFile(partial, local.Folder, name, local.Overwrite);
    }

    private async Task RenderToWorkAsync(ExportJob job, MontagePlan plan, ExportPreset preset, CancellationToken token)
    {
        string partial = PartialPathFor(job);
        Directory.CreateDirectory(_options.WorkFolder);
        await _services.Renderer.RenderAsync(plan, preset, partial, Sink(job.Id, 0, 0.9), token).ConfigureAwait(false);
        EnsureRendered(partial);
        try
        {
            File.Move(partial, WorkPathFor(job), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExportStepException("Impossibile preparare il file per l'invio.", ex);
        }
    }

    private string PlaceFile(string partial, string folder, string name, OverwritePolicy policy)
    {
        switch (policy)
        {
            case OverwritePolicy.Overwrite:
                try
                {
                    File.Move(partial, Path.Combine(folder, name), overwrite: true);
                    return name;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new ExportStepException($"Impossibile sostituire «{name}»: il file è in uso o la cartella non è scrivibile.", ex);
                }

            case OverwritePolicy.Rename:
                for (int number = 1; number <= 999; number++)
                {
                    string candidate = number == 1 ? name : ExportFileNames.WithSuffix(name, number);
                    if (TryMoveNew(partial, folder, candidate))
                    {
                        return candidate;
                    }
                }

                throw new ExportStepException($"Ci sono già troppi file chiamati «{name}»: cambiare il modello del nome.");

            default:
                if (!TryMoveNew(partial, folder, name))
                {
                    throw new ExportStepException($"Esiste già un file «{name}»: scegliere un altro modello o un'altra politica di sovrascrittura.");
                }

                return name;
        }
    }

    /// <summary>Sposta senza sovrascrivere. False se il nome esiste già; altri errori diventano <see cref="ExportStepException"/>.</summary>
    private static bool TryMoveNew(string source, string folder, string name)
    {
        string target = Path.Combine(folder, name);
        if (File.Exists(target))
        {
            return false;
        }

        try
        {
            File.Move(source, target, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(target))
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExportStepException($"Impossibile scrivere «{name}»: la cartella non è scrivibile.", ex);
        }
    }

    private async Task<MontagePlan> ResolvePlanAsync(ExportJob job, CancellationToken token)
    {
        lock (_gate)
        {
            if (_plans.TryGetValue(job.Id, out MontagePlan? known))
            {
                return known;
            }
        }

        MontagePlan? resolved = await _services.PlanResolver.ResolveAsync(job, token).ConfigureAwait(false);
        if (resolved is null)
        {
            throw new ExportStepException("Il progetto non è più disponibile: riaccodare l'esportazione.");
        }

        if (resolved.Fingerprint() != job.PlanFingerprint)
        {
            throw new ExportStepException("Il progetto è cambiato dopo l'accodamento: riaccodare l'esportazione.");
        }

        lock (_gate)
        {
            _plans[job.Id] = resolved;
        }

        return resolved;
    }

    private string RenderTitle(YouTubeDestination youtube, MontagePlan plan)
    {
        TemplateParseResult<TitleTemplate> parsed = TitleTemplate.Parse(youtube.Title);
        if (!parsed.IsValid)
        {
            throw new ExportStepException("Il modello del titolo non è valido: correggerlo e riaccodare l'esportazione.");
        }

        string title = parsed.Template!.Render(BuildContext(plan));
        if (title.Length == 0)
        {
            throw new ExportStepException("Il titolo è vuoto dopo la sostituzione dei segnaposto.");
        }

        return title;
    }

    private string RenderDescription(YouTubeDestination youtube, MontagePlan plan)
    {
        TemplateParseResult<DescriptionTemplate> parsed = DescriptionTemplate.Parse(youtube.Description ?? string.Empty);
        if (!parsed.IsValid)
        {
            throw new ExportStepException("Il modello della descrizione non è valido: correggerlo e riaccodare l'esportazione.");
        }

        return parsed.Template!.Render(BuildContext(plan));
    }

    private static YouTubePrivacy ParsePrivacy(YouTubeDestination youtube)
    {
        if (!ExportValidator.TryParsePrivacy(youtube.Privacy, out YouTubePrivacy privacy))
        {
            throw new ExportStepException("Privacy non valida: correggerla e riaccodare l'esportazione.");
        }

        return privacy;
    }

    /// <summary>
    /// Contesto dei modelli. {meteore} e {transiti} valgono 0: il conteggio dal montaggio non esiste ancora [NV].
    /// </summary>
    private TitleContext BuildContext(MontagePlan plan)
    {
        DateTimeOffset now = TimeZoneInfo.ConvertTime(_options.Clock.UtcNow, _options.LocalZone);
        DateTime nightStart = plan.Frames.Count > 0
            ? TimeZoneInfo.ConvertTime(plan.Frames[0].TimestampUtc, _options.LocalZone).DateTime
            : now.DateTime;
        return new TitleContext(
            NowLocal: now,
            NightStartLocal: nightStart,
            Location: _options.Location,
            Frames: plan.Summary.OutputFrames,
            VideoDuration: TimeSpan.FromSeconds(Math.Max(0, plan.Summary.DurationSeconds)),
            Meteors: 0,
            Transits: 0);
    }

    private DateOnly TodayLocal() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_options.Clock.UtcNow, _options.LocalZone).DateTime);

    private static void EnsureRendered(string partial)
    {
        if (!File.Exists(partial) || new FileInfo(partial).Length == 0)
        {
            throw new ExportStepException("Il rendering non ha prodotto un file utilizzabile.");
        }
    }

    private string PartialPathFor(ExportJob job) => job.Destination switch
    {
        LocalFileDestination local => Path.Combine(local.Folder, "." + job.Id + ".part"),
        _ => Path.Combine(_options.WorkFolder, job.Id + ".part"),
    };

    private string WorkPathFor(ExportJob job) => Path.Combine(_options.WorkFolder, job.Id + ".mp4");

    private void CleanupFiles(ExportJob job)
    {
        try
        {
            TryDelete(PartialPathFor(job));
            if (job.Destination is not LocalFileDestination)
            {
                TryDelete(WorkPathFor(job));
            }
        }
        catch (ArgumentException)
        {
            // Destinazione senza percorso valido: nulla da pulire.
        }
    }

    private void Finish(string id, RunControl run, Outcome outcome)
    {
        ExportJob? updated = null;
        lock (_gate)
        {
            int index = IndexOf(id);
            if (index >= 0)
            {
                ExportJob job = _jobs[index];
                updated = outcome.Kind switch
                {
                    OutcomeKind.Succeeded => job with
                    {
                        State = ExportJobState.Succeeded,
                        Progress = 1,
                        Error = null,
                        Retryable = false,
                        ConsecutiveFailures = 0,
                        OutputFileName = outcome.OutputName ?? job.OutputFileName,
                    },
                    OutcomeKind.Failed => FailedJob(job, outcome),
                    OutcomeKind.Cancelled => job with { State = ExportJobState.Cancelled, Error = null, Retryable = false },
                    OutcomeKind.Paused => job with { State = ExportJobState.Paused, Progress = 0, Error = null, Retryable = false },
                    _ => job with
                    {
                        State = ExportJobState.Queued,
                        Progress = 0,
                        Error = null,
                        Retryable = false,
                        InterruptedRuns = job.InterruptedRuns + 1,
                    },
                };
                _jobs[index] = updated;
            }

            _runs.Remove(id);
            run.Dispose();
            Persist();
        }

        if (updated is not null)
        {
            Notify(updated);
        }
    }

    private static ExportJob FailedJob(ExportJob job, Outcome outcome)
    {
        int failures = job.ConsecutiveFailures + 1;
        bool retry = outcome.Retryable && failures < MaxConsecutiveFailures;
        string message = outcome.Message ?? "Esportazione fallita.";
        if (outcome.Retryable && !retry)
        {
            message += $" Tentativi esauriti ({MaxConsecutiveFailures}): il lavoro si ferma.";
        }

        return job with
        {
            State = ExportJobState.Failed,
            Error = ExportRedactor.Redact(message),
            Retryable = retry,
            ConsecutiveFailures = failures,
        };
    }

    private void Persist()
    {
        try
        {
            ExportQueueStore.Write(_options.StateFilePath, _jobs.ToList());
        }
        catch (StudioException ex)
        {
            Log("Coda non salvata: " + ex.Message);
        }
    }

    private void Notify(ExportJob job)
    {
        Action<ExportJob>? handler = JobChanged;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(job);
        }
        catch (Exception ex)
        {
            Log("Notifica di coda non riuscita: " + ex.GetType().Name);
        }
    }

    private void ReportProgress(string id, double value)
    {
        if (double.IsNaN(value))
        {
            return;
        }

        ExportJob? changed = null;
        lock (_gate)
        {
            int index = IndexOf(id);
            if (index < 0 || _jobs[index].State != ExportJobState.Running)
            {
                return;
            }

            // Monotono: un valore più basso di quello già mostrato viene ignorato.
            double next = Math.Max(_jobs[index].Progress, Math.Clamp(value, 0, 1));
            if (next > _jobs[index].Progress)
            {
                _jobs[index] = _jobs[index] with { Progress = next };
                changed = _jobs[index];
            }
        }

        if (changed is not null)
        {
            Notify(changed);
        }
    }

    private SinkProgress Sink(string id, double from, double to) => new(this, id, from, to);

    private int IndexOf(string id) => _jobs.FindIndex(j => j.Id == id);

    private void Log(string message) => _options.Log?.Invoke(ExportRedactor.Redact(message));

    private void MoveAside(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Move(path, path + ".corrotto", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("Coda corrotta non spostata: " + ex.GetType().Name);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ExportJob Reset(ExportJob job) => job with
    {
        State = ExportJobState.Queued,
        Progress = 0,
        Error = null,
        Retryable = false,
        Attempts = 0,
        ConsecutiveFailures = 0,
        InterruptedRuns = 0,
        OutputFileName = null,
    };

    /// <summary>Traduce l'avanzamento di un passo (0..1) nell'intervallo del lavoro.</summary>
    private sealed class SinkProgress(ExportQueue queue, string id, double from, double to) : IProgress<double>
    {
        public void Report(double value)
        {
            double clamped = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
            queue.ReportProgress(id, from + ((to - from) * clamped));
        }
    }

    private enum RunStop
    {
        None,
        Cancel,
        Pause,
    }

    /// <summary>Controllo di una esecuzione: il token e la richiesta dell'utente di fermarla.</summary>
    private sealed class RunControl : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private volatile RunStop _stop = RunStop.None;

        public CancellationToken Token => _cts.Token;

        public bool StopRequested => _stop != RunStop.None;

        public RunStop Stop => _stop;

        public void RequestStop(RunStop stop)
        {
            _stop = stop;
            _cts.Cancel();
        }

        public void Dispose() => _cts.Dispose();
    }

    private enum OutcomeKind
    {
        Succeeded,
        Failed,
        Cancelled,
        Paused,
        Interrupted,
    }

    private sealed record Outcome(OutcomeKind Kind, string? OutputName, string? Message, bool Retryable)
    {
        public static Outcome Success(string? outputName) => new(OutcomeKind.Succeeded, outputName, null, false);

        public static Outcome Failed(string message, bool retryable) => new(OutcomeKind.Failed, null, message, retryable);

        public static Outcome Cancelled() => new(OutcomeKind.Cancelled, null, null, false);

        public static Outcome Paused() => new(OutcomeKind.Paused, null, null, false);

        public static Outcome Interrupted() => new(OutcomeKind.Interrupted, null, null, false);
    }
}
