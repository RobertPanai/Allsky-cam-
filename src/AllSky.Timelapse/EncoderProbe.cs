using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace AllSky.Timelapse;

/// <summary>Encoder che hanno superato la codifica di prova, in ordine di preferenza.</summary>
/// <param name="Encoders">Encoder utilizzabili; vuoto se nessuno. Il primo e' quello preferito.</param>
/// <param name="Message">Motivo se la lista e' vuota, oppure null.</param>
public sealed record WorkingEncoders(IReadOnlyList<EncoderSelection> Encoders, string? Message)
{
    /// <summary>True se almeno un encoder ha superato la prova.</summary>
    public bool IsAvailable => Encoders.Count > 0;
}

/// <summary>
/// Sceglie l'encoder H.264 leggendo l'output di <c>ffmpeg -hide_banner -encoders</c>.
/// <see cref="Select"/> e' pura (lavora su una stringa). <see cref="GetWorkingAsync"/> aggiunge una
/// codifica di prova per ogni candidato: un encoder elencato ma non funzionante (es. h264_nvenc senza driver)
/// viene scartato. Il risultato e' messo in cache per ffmpeg+policy, quindi la prova gira una sola volta.
/// </summary>
public static class EncoderProbe
{
    /// <summary>Tempo massimo per ogni comando di ffmpeg usato dalla scelta (elenco o prova).</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private const string GplEncoder = "libx264";
    private const string Mpeg4Encoder = "mpeg4";
    private const string TrialSource = "color=c=black:s=128x72:r=5";

    private static readonly ConcurrentDictionary<string, Lazy<Task<WorkingEncoders>>> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Estrae i nomi degli encoder video dall'output di <c>ffmpeg -encoders</c>.
    /// Le righe valide hanno sei caratteri di flag che iniziano con 'V' (es. " V....D h264_mf ...").
    /// </summary>
    /// <param name="encodersOutput">Testo prodotto da ffmpeg su stdout.</param>
    public static IReadOnlySet<string> ParseVideoEncoders(string encodersOutput)
    {
        ArgumentNullException.ThrowIfNull(encodersOutput);

        var names = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StringReader(encodersOutput);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
            {
                continue;
            }

            // Legenda ("V..... = Video") e intestazioni non sono encoder.
            string flags = tokens[0];
            if (flags.Length != 6 || flags[0] != 'V' || tokens[1] == "=")
            {
                continue;
            }

            names.Add(tokens[1]);
        }

        return names;
    }

    /// <summary>
    /// Sceglie l'encoder dato l'output di <c>ffmpeg -encoders</c>, senza eseguire nulla. Funzione pura.
    /// Non verifica che l'encoder funzioni davvero: per questo serve <see cref="GetWorkingAsync"/>.
    /// </summary>
    /// <param name="encodersOutput">Testo di <c>ffmpeg -hide_banner -encoders</c>.</param>
    /// <param name="policy">Regole di scelta.</param>
    public static EncoderSelection Select(string encodersOutput, EncoderPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(encodersOutput);

        IReadOnlyList<EncoderSelection> candidates = Candidates(ParseVideoEncoders(encodersOutput), policy, out bool gplBlocked);
        if (candidates.Count > 0)
        {
            return candidates[0];
        }

        return new EncoderSelection(null, false, UnavailableMessage(gplBlocked));
    }

    /// <summary>
    /// Esegue <c>ffmpeg -encoders</c> e sceglie il primo candidato (senza prova). Non lancia se ffmpeg manca o non parte.
    /// </summary>
    /// <param name="ffmpegPath">Percorso di ffmpeg, oppure null.</param>
    /// <param name="policy">Regole di scelta.</param>
    /// <param name="ct">Cancellazione.</param>
    public static async Task<EncoderSelection> ProbeAsync(string? ffmpegPath, EncoderPolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        (string? output, string? error) = await ListEncodersAsync(ffmpegPath, ct);
        if (output is null)
        {
            return new EncoderSelection(null, false, error);
        }

        return Select(output, policy);
    }

    /// <summary>
    /// Restituisce gli encoder che superano una codifica di prova (5 frame 128x72), in ordine di preferenza.
    /// Ogni candidato viene provato, ciascuno con timeout <see cref="ProbeTimeout"/>, cosi' il builder ha gia' pronto il
    /// candidato successivo per il retry. Il risultato e' in cache per coppia (ffmpeg, policy) per la durata del processo.
    /// La prova usa <see cref="CancellationToken.None"/> internamente: <paramref name="ct"/> annulla solo l'attesa del chiamante.
    /// </summary>
    /// <param name="ffmpegPath">Percorso di ffmpeg, oppure null.</param>
    /// <param name="policy">Regole di scelta.</param>
    /// <param name="ct">Cancellazione dell'attesa.</param>
    public static Task<WorkingEncoders> GetWorkingAsync(string? ffmpegPath, EncoderPolicy policy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return Task.FromResult(new WorkingEncoders([], "ffmpeg non trovato: impossibile scegliere l'encoder."));
        }

        string path = ffmpegPath;
        string key = path + '\u001f' + PolicyKey(policy);
        Lazy<Task<WorkingEncoders>> lazy = Cache.GetOrAdd(
            key,
            _ => new Lazy<Task<WorkingEncoders>>(() => ComputeWorkingAsync(path, policy), LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value.WaitAsync(ct);
    }

    /// <summary>
    /// Candidati nell'ordine di preferenza: encoder della policy presenti in ffmpeg (libx264 solo con AllowGpl),
    /// poi mpeg4 come ultima voce se AllowFallbackMpeg4. Ogni voce ha il messaggio di avviso adeguato.
    /// </summary>
    internal static IReadOnlyList<EncoderSelection> Candidates(IReadOnlySet<string> available, EncoderPolicy policy, out bool gplBlocked)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(policy.Preferred);

        var list = new List<EncoderSelection>();
        gplBlocked = false;

        foreach (string name in policy.Preferred)
        {
            if (!available.Contains(name) || list.Any(c => c.Encoder == name))
            {
                continue;
            }

            if (name == GplEncoder && !policy.AllowGpl)
            {
                gplBlocked = true;
                continue;
            }

            bool isH264 = IsH264(name);
            list.Add(new EncoderSelection(name, isH264, isH264 ? null : $"Attenzione: l'encoder {name} non produce H.264."));
        }

        if (policy.AllowFallbackMpeg4 && available.Contains(Mpeg4Encoder) && !list.Any(c => c.Encoder == Mpeg4Encoder))
        {
            list.Add(new EncoderSelection(
                Mpeg4Encoder,
                false,
                "Attenzione: nessun encoder H.264 disponibile, uso mpeg4 (MPEG-4 Parte 2), che non è H.264."));
        }

        return list;
    }

    private static async Task<WorkingEncoders> ComputeWorkingAsync(string ffmpegPath, EncoderPolicy policy)
    {
        (string? output, string? error) = await ListEncodersAsync(ffmpegPath, CancellationToken.None);
        if (output is null)
        {
            return new WorkingEncoders([], error);
        }

        IReadOnlyList<EncoderSelection> candidates = Candidates(ParseVideoEncoders(output), policy, out bool gplBlocked);

        var working = new List<EncoderSelection>();
        var failed = new List<string>();
        foreach (EncoderSelection candidate in candidates)
        {
            if (await TrialEncodeAsync(ffmpegPath, candidate.Encoder!, CancellationToken.None))
            {
                working.Add(candidate);
            }
            else
            {
                failed.Add(candidate.Encoder!);
            }
        }

        if (working.Count > 0)
        {
            return new WorkingEncoders(working, null);
        }

        string message = UnavailableMessage(gplBlocked);
        if (failed.Count > 0)
        {
            message += " Encoder provati senza successo: " + string.Join(", ", failed) + ".";
        }

        return new WorkingEncoders([], message);
    }

    /// <summary>Esegue la codifica di prova di un encoder. True se ffmpeg termina con exit 0 entro il timeout.</summary>
    private static async Task<bool> TrialEncodeAsync(string ffmpegPath, string encoder, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in new[]
                 {
                     "-hide_banner", "-loglevel", "error",
                     "-f", "lavfi", "-i", TrialSource,
                     "-frames:v", "5",
                     "-c:v", encoder,
                     "-pix_fmt", "yuv420p",
                     "-f", "null", "-",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return false;
            }
        }
        catch (Win32Exception)
        {
            return false;
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProbeTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            ProcessSafety.KillTree(process);
        }

        await stdoutTask;
        await stderrTask;
        return process.ExitCode == 0;
    }

    /// <summary>Esegue <c>ffmpeg -hide_banner -encoders</c>. Restituisce l'output, oppure il motivo del fallimento.</summary>
    private static async Task<(string? Output, string? Error)> ListEncodersAsync(string? ffmpegPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return (null, "ffmpeg non trovato: impossibile elencare gli encoder.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-encoders");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return (null, "impossibile avviare ffmpeg per elencare gli encoder.");
            }
        }
        catch (Win32Exception ex)
        {
            return (null, $"impossibile avviare ffmpeg per elencare gli encoder: {ex.Message}");
        }

        // Entrambi i flussi vanno letti, altrimenti ffmpeg puo' bloccarsi su un buffer pieno.
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        bool timedOut = false;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ProbeTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
        }
        finally
        {
            ProcessSafety.KillTree(process);
        }

        if (timedOut)
        {
            return (null, $"ffmpeg -encoders non ha risposto entro {ProbeTimeout.TotalSeconds:0} secondi.");
        }

        string output = await stdoutTask;
        await stderrTask;

        if (process.ExitCode != 0)
        {
            return (null, $"ffmpeg -encoders è terminato con codice {process.ExitCode}.");
        }

        return (output, null);
    }

    private static string UnavailableMessage(bool gplBlocked)
    {
        string message =
            "nessun encoder H.264 disponibile: su Windows edizione N installare il Media Feature Pack, " +
            "oppure usare un ffmpeg con h264_nvenc, h264_qsv o h264_amf.";
        if (gplBlocked)
        {
            message += " libx264 è presente ma non viene usato perché AllowGpl è false (licenza GPL).";
        }

        return message;
    }

    private static string PolicyKey(EncoderPolicy policy)
    {
        return string.Join(",", policy.Preferred) + "|" + policy.AllowGpl + "|" + policy.AllowFallbackMpeg4;
    }

    /// <summary>True se il nome di encoder produce H.264.</summary>
    private static bool IsH264(string name)
    {
        return name.StartsWith("h264_", StringComparison.Ordinal) || name == GplEncoder;
    }
}
