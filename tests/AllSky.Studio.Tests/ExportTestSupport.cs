using System.Collections.Concurrent;
using AllSky.YouTube;
using Xunit;

namespace AllSky.Studio.Tests;

/// <summary>Orologio fisso per i test di esportazione.</summary>
internal sealed class ExportTestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);
}

/// <summary>Chiamata al rendering finto: percorso temporaneo, avanzamento e token.</summary>
internal sealed record RenderCall(string OutputPath, IProgress<double> Progress, CancellationToken Token);

/// <summary>
/// Renderer finto. Di default scrive un file di pochi byte. Con <see cref="Behavior"/> il test decide cosa fare
/// (per esempio scrivere un file parziale e aspettare l'annullamento).
/// </summary>
internal sealed class FakeRenderer : IVideoRenderer
{
    private readonly object _gate = new();
    private int _running;

    public Func<RenderCall, Task>? Behavior { get; set; }

    public int Calls { get; private set; }

    public int MaxParallel { get; private set; }

    public List<ExportPreset> Presets { get; } = [];

    public async Task RenderAsync(MontagePlan plan, ExportPreset preset, string outputPath, IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        int now;
        lock (_gate)
        {
            Calls++;
            now = ++_running;
            MaxParallel = Math.Max(MaxParallel, now);
            Presets.Add(preset);
        }

        try
        {
            if (Behavior is null)
            {
                progress.Report(0);
                await File.WriteAllBytesAsync(outputPath, "FAKE-VIDEO"u8.ToArray(), cancellationToken);
                progress.Report(0.5);
                progress.Report(1);
            }
            else
            {
                await Behavior(new RenderCall(outputPath, progress, cancellationToken));
            }
        }
        finally
        {
            lock (_gate)
            {
                _running--;
            }
        }
    }
}

/// <summary>Caricamento YouTube finto. Registra le richieste; il comportamento è configurabile.</summary>
internal sealed class FakeYouTube : IYouTubeUploader
{
    public Func<YouTubeUploadRequest, IProgress<double>?, CancellationToken, Task<string>>? Behavior { get; set; }

    public List<YouTubeUploadRequest> Requests { get; } = [];

    public int Calls => Requests.Count;

    public Task<string> UploadAsync(YouTubeUploadRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Behavior is not null)
        {
            return Behavior(request, progress, cancellationToken);
        }

        progress?.Report(0.5);
        return Task.FromResult("video-finto-1");
    }
}

/// <summary>Invio Telegram finto. Registra chat, didascalia e file.</summary>
internal sealed class FakeTelegram : ITelegramPublisher
{
    public Func<long, string, IProgress<double>?, CancellationToken, Task>? Behavior { get; set; }

    public List<(long ChatId, string? Caption, string FileName)> Sent { get; } = [];

    public int Calls { get; private set; }

    public Task SendVideoAsync(long chatId, string filePath, string? caption, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        Calls++;
        Sent.Add((chatId, caption, Path.GetFileName(filePath)));
        return Behavior is null ? Task.CompletedTask : Behavior(chatId, filePath, progress, cancellationToken);
    }
}

/// <summary>Risolutore del piano finto: restituisce sempre il piano dato.</summary>
internal sealed class FakePlanResolver(MontagePlan plan) : IExportPlanResolver
{
    public Task<MontagePlan?> ResolveAsync(ExportJob job, CancellationToken cancellationToken) =>
        Task.FromResult<MontagePlan?>(plan);
}

/// <summary>
/// Ambiente di prova: cartelle temporanee (destinazione, lavoro, stato), piano sintetico, fake e una coda.
/// Ogni test lo crea e lo butta via con <see cref="Dispose"/>.
/// </summary>
internal sealed class ExportHarness : IDisposable
{
    private readonly ConcurrentQueue<string> _log = new();

    public ExportHarness(int maxConcurrent = 1)
    {
        Tmp = new TempFolder();
        Folder = Path.Combine(Tmp.Root, "uscita");
        WorkFolder = Path.Combine(Tmp.Root, "lavoro");
        StateFile = Tmp.FilePath("coda.json");
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(WorkFolder);
        Plan = MontagePlanner.Plan(MontageFixtures.Frames(10), [], MontageFixtures.UniformStats()).Plan!;
        Renderer = new FakeRenderer();
        YouTube = new FakeYouTube();
        Telegram = new FakeTelegram();
        Resolver = new FakePlanResolver(Plan);
        MaxConcurrent = maxConcurrent;
        Queue = Open();
    }

    public TempFolder Tmp { get; }

    /// <summary>Cartella di destinazione dei file locali.</summary>
    public string Folder { get; }

    /// <summary>Cartella dei temporanei per YouTube e Telegram.</summary>
    public string WorkFolder { get; }

    /// <summary>File di stato della coda.</summary>
    public string StateFile { get; }

    public MontagePlan Plan { get; }

    public FakeRenderer Renderer { get; }

    public FakeYouTube YouTube { get; }

    public FakeTelegram Telegram { get; }

    public FakePlanResolver Resolver { get; }

    public ExportTestClock Clock { get; } = new();

    public int MaxConcurrent { get; }

    public ExportQueue Queue { get; private set; }

    /// <summary>Tutte le righe del registro, nell'ordine di scrittura.</summary>
    public IReadOnlyList<string> Lines => _log.ToArray();

    /// <summary>Riapre la coda dallo stesso file, come dopo un riavvio.</summary>
    public ExportQueue Reopen()
    {
        Queue = Open();
        return Queue;
    }

    /// <summary>Costruisce un lavoro per il progetto di prova.</summary>
    public ExportJob Job(ExportDestination destination, int sequence = 1, ExportPreset? preset = null) =>
        ExportJobFactory.Create(
            "Notte di prova",
            Path.Combine(Tmp.Root, "progetto.asbproject"),
            Plan.Fingerprint(),
            sequence,
            new ExportTarget { Preset = preset ?? ExportPresets.Hd, Destination = destination },
            Clock.UtcNow);

    public ExportJob LocalJob(string template = "{project} {date}", OverwritePolicy policy = OverwritePolicy.Fail, int sequence = 1) =>
        Job(new LocalFileDestination { Folder = Folder, FileNameTemplate = template, Overwrite = policy }, sequence);

    public QueueResult Enqueue(ExportJob job) => Queue.Enqueue(job, Plan);

    /// <summary>Accoda e esegue una volta; restituisce il lavoro finale.</summary>
    public async Task<ExportJob> RunOnceAsync(ExportJob job)
    {
        QueueResult result = Enqueue(job);
        Assert.True(result.Ok, result.Message);
        await Queue.RunPendingAsync(CancellationToken.None);
        return Queue.Find(job.Id)!;
    }

    public void Dispose() => Tmp.Dispose();

    private ExportQueue Open() => ExportQueue.Open(
        new ExportQueueOptions(
            StateFile,
            WorkFolder,
            Clock,
            TimeZoneInfo.Utc,
            "Osservatorio di prova",
            MaxConcurrent,
            line => _log.Enqueue(line)),
        new ExportServices(Renderer, YouTube, Telegram, Resolver));
}
