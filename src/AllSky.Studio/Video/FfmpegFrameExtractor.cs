using System.Globalization;

namespace AllSky.Studio;

/// <summary>
/// Estrae frame con ffmpeg esterno: <c>-vf fps=1/&lt;n&gt;</c>, JPEG numerati <c>frame_000001.jpg</c>, ...
/// <list type="bullet">
/// <item>ffmpeg non viene mai incluso nell'app: si usa solo quello trovato con
/// <see cref="StudioFfmpeg.Locate"/>, con le stesse regole del modulo Timelapse.</item>
/// <item>Gli argomenti sono una lista, mai una stringa concatenata.</item>
/// <item>Prima dell'estrazione si rimuovono solo i file che seguono il nome <c>frame_NNNNNN.jpg</c> nella cartella di output.</item>
/// <item>Il frame numero n (a partire da 1) cade a (n-1) × intervallo secondi dall'inizio del video.</item>
/// </list>
/// </summary>
public sealed class FfmpegFrameExtractor : IFrameExtractor
{
    /// <summary>Modello dei nomi di output, nella sintassi di ffmpeg.</summary>
    public const string FramePattern = "frame_%06d.jpg";

    /// <summary>Intervallo minimo consentito, in secondi.</summary>
    public const double MinIntervalSeconds = 0.1;

    /// <summary>Intervallo massimo consentito, in secondi.</summary>
    public const double MaxIntervalSeconds = 3600;

    private const string Prefix = "frame_";
    private const string Suffix = ".jpg";
    private const int DigitsLength = 6;

    private readonly string? _ffmpegPath;
    private readonly IProcessRunner _runner;

    /// <summary>Crea l'estrattore.</summary>
    /// <param name="ffmpegPath">Percorso di ffmpeg, da <see cref="StudioFfmpeg.Locate"/>. Null = ffmpeg non disponibile.</param>
    /// <param name="runner">Esecutore dei processi.</param>
    public FfmpegFrameExtractor(string? ffmpegPath, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _ffmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? null : ffmpegPath;
        _runner = runner;
    }

    /// <summary>Argomenti di ffmpeg per estrarre un frame ogni <paramref name="intervalSeconds"/> secondi.</summary>
    /// <param name="videoPath">Video di ingresso.</param>
    /// <param name="outputPattern">Modello di output (es. cartella + <see cref="FramePattern"/>).</param>
    /// <param name="intervalSeconds">Intervallo in secondi, da 0,1 a 3600.</param>
    public static IReadOnlyList<string> BuildArguments(string videoPath, string outputPattern, double intervalSeconds)
    {
        ValidateInterval(intervalSeconds);
        string interval = intervalSeconds.ToString("R", CultureInfo.InvariantCulture);
        return
        [
            "-hide_banner",
            "-loglevel", "warning",
            "-nostdin",
            "-y",
            "-i", videoPath,
            "-vf", $"fps=1/{interval}",
            "-q:v", "2",
            outputPattern,
        ];
    }

    /// <inheritdoc />
    public async Task<ExtractResult> ExtractAsync(string videoPath, string outputFolder, double intervalSeconds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);
        ValidateInterval(intervalSeconds);
        ct.ThrowIfCancellationRequested();

        if (_ffmpegPath is null)
        {
            throw new StudioException("ffmpeg non trovato: impostare il percorso o installare ffmpeg.");
        }

        if (!File.Exists(videoPath))
        {
            throw new StudioException($"video non trovato: {Path.GetFileName(videoPath)}.");
        }

        string output = Path.GetFullPath(outputFolder);
        if (output.Contains('%'))
        {
            throw new StudioException("la cartella di output non può contenere il carattere «%».");
        }

        Directory.CreateDirectory(output);
        RemoveStaleFrames(output);

        IReadOnlyList<string> arguments = BuildArguments(
            Path.GetFullPath(videoPath),
            Path.Combine(output, FramePattern),
            intervalSeconds);

        ProcessRunResult run = await _runner.RunAsync(_ffmpegPath, arguments, ct);
        if (run.ExitCode != 0)
        {
            throw new StudioException($"ffmpeg è terminato con codice {run.ExitCode}.") { StderrTail = run.StderrTail };
        }

        var frames = new List<ExtractedFrame>();
        foreach (string file in Directory.EnumerateFiles(output).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            if (!IsFrameName(name))
            {
                continue;
            }

            int number = int.Parse(name.AsSpan(Prefix.Length, DigitsLength), NumberStyles.None, CultureInfo.InvariantCulture);
            frames.Add(new ExtractedFrame(file, TimeSpan.FromSeconds((number - 1) * intervalSeconds)));
        }

        if (frames.Count == 0)
        {
            throw new StudioException("ffmpeg non ha prodotto frame: il video potrebbe essere illeggibile.");
        }

        return new ExtractResult(frames);
    }

    private static void ValidateInterval(double intervalSeconds)
    {
        if (!double.IsFinite(intervalSeconds) || intervalSeconds < MinIntervalSeconds || intervalSeconds > MaxIntervalSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds), intervalSeconds, "L'intervallo deve essere tra 0,1 e 3600 secondi.");
        }
    }

    private static bool IsFrameName(string name)
    {
        if (name.Length != Prefix.Length + DigitsLength + Suffix.Length
            || !name.StartsWith(Prefix, StringComparison.Ordinal)
            || !name.EndsWith(Suffix, StringComparison.Ordinal))
        {
            return false;
        }

        for (int i = Prefix.Length; i < Prefix.Length + DigitsLength; i++)
        {
            if (name[i] < '0' || name[i] > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static void RemoveStaleFrames(string output)
    {
        foreach (string file in Directory.EnumerateFiles(output))
        {
            if (IsFrameName(Path.GetFileName(file)))
            {
                File.Delete(file);
            }
        }
    }
}
