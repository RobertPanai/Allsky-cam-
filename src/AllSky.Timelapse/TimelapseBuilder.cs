using System.ComponentModel;
using System.Diagnostics;

namespace AllSky.Timelapse;

/// <summary>
/// Costruisce il timelapse MP4 lanciando ffmpeg come processo esterno e inviandogli i JPEG su stdin.
/// <list type="bullet">
/// <item>I JPEG vengono letti uno alla volta e copiati su stdin: nessun accumulo in memoria.</item>
/// <item>L'uscita va nel file <c>&lt;output&gt;.tmp.mp4</c>, poi viene spostata in modo atomico sul file finale.</item>
/// <item>Se <see cref="TimelapseOptions.Encoder"/> e' null, l'encoder viene scelto con <see cref="EncoderProbe.GetWorkingAsync"/>
/// (codifica di prova). Se la codifica reale fallisce con il primo candidato, ritenta UNA volta con il successivo.</item>
/// <item>Se l'encoder e' impostato esplicitamente non c'e' prova e non c'e' retry.</item>
/// <item>In caso di errore o cancellazione il processo viene terminato e il temporaneo eliminato.</item>
/// <item>Le dimensioni sono quelle del primo JPEG; tutti i frame devono avere la stessa dimensione
/// (gli altri vengono comunque scalati da ffmpeg alle dimensioni del primo).</item>
/// </list>
/// </summary>
public sealed class TimelapseBuilder : ITimelapseBuilder
{
    private const int BufferSize = 64 * 1024;
    private const int StderrTailLines = 20;

    private readonly EncoderPolicy _policy;

    /// <summary>Crea il builder.</summary>
    /// <param name="policy">Regole per la scelta automatica dell'encoder. Null = <see cref="EncoderPolicy"/> di default.</param>
    public TimelapseBuilder(EncoderPolicy? policy = null)
    {
        _policy = policy ?? new EncoderPolicy();
    }

    /// <inheritdoc />
    public async Task BuildAsync(
        IReadOnlyList<string> jpegPaths,
        string outputMp4,
        TimelapseOptions options,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jpegPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputMp4);
        ArgumentNullException.ThrowIfNull(options);
        FfmpegArguments.ValidateOptions(options);

        if (jpegPaths.Count == 0)
        {
            throw new ArgumentException("Serve almeno un JPEG.", nameof(jpegPaths));
        }

        if (string.IsNullOrWhiteSpace(options.FfmpegPath))
        {
            throw new TimelapseException("ffmpeg non trovato: impostare il percorso o installare ffmpeg.");
        }

        ct.ThrowIfCancellationRequested();

        string output = Path.GetFullPath(outputMp4);
        string temp = output + ".tmp.mp4";

        if (!JpegSize.TryRead(jpegPaths[0], out int sourceWidth, out int sourceHeight))
        {
            throw new TimelapseException($"impossibile leggere le dimensioni del JPEG '{jpegPaths[0]}'.");
        }

        (int width, int height) = FfmpegArguments.OutputSize(sourceWidth, sourceHeight, options.MaxWidth);

        string? directory = Path.GetDirectoryName(output);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        IReadOnlyList<string> encoders = await ResolveEncodersAsync(options, ct);
        bool autoPicked = string.IsNullOrWhiteSpace(options.Encoder);
        int attempts = autoPicked ? Math.Min(2, encoders.Count) : 1;

        var failures = new List<string>();
        string tail = string.Empty;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            string encoder = encoders[attempt];
            AttemptResult result = await RunAttemptAsync(options, encoder, jpegPaths, output, temp, width, height, progress, ct);
            if (result.Success)
            {
                return;
            }

            DeleteQuietly(temp);
            failures.Add($"encoder {encoder}: {result.Detail}");
            tail = result.Tail;
        }

        throw new TimelapseException(
            "codifica del timelapse fallita. " + string.Join("; ", failures),
            tail);
    }

    /// <summary>Encoder da provare in ordine: quello esplicito, oppure i candidati verificati con la prova.</summary>
    private async Task<IReadOnlyList<string>> ResolveEncodersAsync(TimelapseOptions options, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(options.Encoder))
        {
            return [options.Encoder];
        }

        WorkingEncoders working = await EncoderProbe.GetWorkingAsync(options.FfmpegPath, _policy, ct);
        if (!working.IsAvailable)
        {
            throw new TimelapseException(working.Message ?? "nessun encoder H.264 disponibile.");
        }

        return working.Encoders.Select(e => e.Encoder!).ToList();
    }

    /// <summary>Un tentativo di codifica con un solo encoder. Restituisce l'esito; lancia solo per errori fatali o cancellazione.</summary>
    private static async Task<AttemptResult> RunAttemptAsync(
        TimelapseOptions options,
        string encoder,
        IReadOnlyList<string> jpegPaths,
        string output,
        string temp,
        int width,
        int height,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        IReadOnlyList<string> arguments = FfmpegArguments.Build(options with { Encoder = encoder }, temp, width, height);
        DeleteQuietly(temp);

        var startInfo = new ProcessStartInfo
        {
            FileName = options.FfmpegPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new TimelapseException("impossibile avviare ffmpeg.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new TimelapseException($"impossibile avviare ffmpeg: {ex.Message}", ex);
        }

        var stderrTail = new Queue<string>();
        Task stderrTask = ReadTailAsync(process.StandardError, stderrTail);
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();

        // Cancellazione: termina ffmpeg subito; la pulizia avviene nel catch sotto.
        using CancellationTokenRegistration registration = ct.Register(() => ProcessSafety.RequestKill(process));

        try
        {
            bool inputComplete = await FeedFramesAsync(process, jpegPaths, options.Overlay, progress, ct);

            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stderrTask, stdoutTask);

            string tail = TailText(stderrTail);

            if (!inputComplete)
            {
                return AttemptResult.Failed("ffmpeg ha chiuso l'ingresso prima della fine dei frame.", tail);
            }

            if (process.ExitCode != 0)
            {
                return AttemptResult.Failed($"ffmpeg è terminato con codice {process.ExitCode}.", tail);
            }

            ct.ThrowIfCancellationRequested();

            var tempInfo = new FileInfo(temp);
            if (!tempInfo.Exists || tempInfo.Length == 0)
            {
                return AttemptResult.Failed("ffmpeg non ha prodotto il file MP4.", tail);
            }

            File.Move(temp, output, overwrite: true);
            return AttemptResult.Ok;
        }
        catch (Exception)
        {
            ProcessSafety.KillTree(process);
            DeleteQuietly(temp);
            throw;
        }
    }

    /// <summary>
    /// Copia i JPEG su stdin, uno alla volta. Con una maschera, ogni frame viene composto prima dell'invio.
    /// Restituisce false se ffmpeg ha chiuso l'ingresso prima della fine.
    /// Gli errori di lettura dei file (es. file mancante) non vengono nascosti: si propagano.
    /// </summary>
    private static async Task<bool> FeedFramesAsync(
        Process process,
        IReadOnlyList<string> jpegPaths,
        FrameOverlayPlan? overlay,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        Stream stdin = process.StandardInput.BaseStream;
        var buffer = new byte[BufferSize];

        try
        {
            for (int i = 0; i < jpegPaths.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                bool written = overlay is null
                    ? await CopyFrameAsync(stdin, jpegPaths[i], buffer, ct)
                    : await WriteComposedAsync(stdin, await FrameOverlayStep.ComposeAsync(jpegPaths[i], i, overlay, ct), ct);
                if (!written)
                {
                    return false;
                }

                progress?.Report((double)(i + 1) / jpegPaths.Count);
            }

            try
            {
                await stdin.FlushAsync(ct);
            }
            catch (IOException)
            {
                return false;
            }

            return true;
        }
        finally
        {
            // Chiude lo stdin: per ffmpeg e' la fine dell'ingresso.
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Copia un JPEG su disco verso stdin a blocchi. False se ffmpeg ha chiuso l'ingresso.</summary>
    private static async Task<bool> CopyFrameAsync(Stream stdin, string path, byte[] buffer, CancellationToken ct)
    {
        await using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0)
            {
                return true;
            }

            try
            {
                await stdin.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            catch (IOException)
            {
                // ffmpeg ha chiuso lo stdin: il codice di uscita e lo stderr spiegano il motivo.
                return false;
            }
        }
    }

    /// <summary>Invia un JPEG gia' composto in memoria. False se ffmpeg ha chiuso l'ingresso.</summary>
    private static async Task<bool> WriteComposedAsync(Stream stdin, byte[] jpeg, CancellationToken ct)
    {
        try
        {
            await stdin.WriteAsync(jpeg.AsMemory(), ct);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Legge stderr fino alla fine, conservando solo le ultime righe.</summary>
    private static async Task ReadTailAsync(StreamReader reader, Queue<string> tail)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            if (tail.Count == StderrTailLines)
            {
                tail.Dequeue();
            }

            tail.Enqueue(line);
        }
    }

    private static string TailText(Queue<string> tail)
    {
        return string.Join(Environment.NewLine, tail);
    }

    private static void DeleteQuietly(string path)
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
            // File ancora in uso o non eliminabile: nessun'altra azione possibile.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Esito di un tentativo: successo, oppure motivo e ultime righe di stderr.</summary>
    private sealed record AttemptResult(bool Success, string Detail, string Tail)
    {
        public static AttemptResult Ok { get; } = new(true, string.Empty, string.Empty);

        public static AttemptResult Failed(string detail, string tail) => new(false, detail, tail);
    }
}
