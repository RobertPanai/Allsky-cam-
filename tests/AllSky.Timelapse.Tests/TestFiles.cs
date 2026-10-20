using Xunit;

namespace AllSky.Timelapse.Tests;

/// <summary>Helper dei test: cartelle temporanee, JPEG di prova e ricerca di ffmpeg/ffprobe.</summary>
internal static class TestFiles
{
    /// <summary>JPEG baseline 64x48 generato con ffmpeg (testsrc), 1700 byte.</summary>
    private const string Jpeg64x48Base64 =
        "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";

    /// <summary>Byte di un JPEG 64x48 di prova.</summary>
    public static byte[] Jpeg64x48 => Convert.FromBase64String(Jpeg64x48Base64);

    /// <summary>Scrive <paramref name="count"/> copie del JPEG di prova nella cartella e restituisce i percorsi in ordine.</summary>
    public static IReadOnlyList<string> WriteJpegSequence(string directory, int count)
    {
        byte[] bytes = Jpeg64x48;
        var paths = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            string path = Path.Combine(directory, $"frame_{i:D03}.jpg");
            File.WriteAllBytes(path, bytes);
            paths.Add(path);
        }

        return paths;
    }

    /// <summary>Percorso di ffprobe accanto a ffmpeg, oppure null se non c'e'.</summary>
    public static string? FfprobeNextTo(string ffmpegPath)
    {
        string? dir = Path.GetDirectoryName(ffmpegPath);
        if (string.IsNullOrEmpty(dir))
        {
            return null;
        }

        string name = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        string candidate = Path.Combine(dir, name);
        return File.Exists(candidate) ? candidate : null;
    }
}

/// <summary>
/// Ffmpeg finto (script sh, solo Linux). Comportamento guidato da file nella propria cartella:
/// <c>fail_trial.txt</c> = encoder che falliscono la prova; <c>fail_real.txt</c> = encoder che falliscono la codifica reale
/// (senza leggere stdin). <c>used.txt</c> registra ogni codifica reale tentata. Con <c>-encoders</c> elenca h264_mf, h264_nvenc, libx264.
/// </summary>
internal static class FakeFfmpeg
{
    private const string Script = """
        #!/bin/sh
        dir=$(dirname "$0")
        enc=""
        prev=""
        mode=real
        last=""
        for a in "$@"; do
          if [ "$a" = "-encoders" ]; then
            echo "Encoders:"
            echo " V..... = Video"
            echo " V....D h264_mf              Media Foundation H.264"
            echo " V....D h264_nvenc           NVENC H.264"
            echo " V....D libx264              x264 (codec h264)"
            exit 0
          fi
          if [ "$a" = "lavfi" ]; then mode=trial; fi
          if [ "$prev" = "-c:v" ]; then enc="$a"; fi
          prev="$a"
          last="$a"
        done
        if [ "$mode" = "trial" ]; then
          if [ -f "$dir/fail_trial.txt" ] && grep -qw -- "$enc" "$dir/fail_trial.txt"; then exit 1; fi
          exit 0
        fi
        echo "$enc" >> "$dir/used.txt"
        if [ -f "$dir/fail_real.txt" ] && grep -qw -- "$enc" "$dir/fail_real.txt"; then exit 1; fi
        cat > "$last"
        exit 0
        """;

    /// <summary>Scrive lo script in <paramref name="directory"/>, lo rende eseguibile e restituisce il percorso.</summary>
    public static string Create(string directory)
    {
        string path = Path.Combine(directory, "ffmpeg");
        File.WriteAllText(path, Script.Replace("\r", string.Empty) + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    /// <summary>Imposta gli encoder che devono fallire in un file di configurazione del finto ffmpeg.</summary>
    public static void SetFailing(string directory, string file, params string[] encoders)
    {
        File.WriteAllText(Path.Combine(directory, file), string.Join(" ", encoders));
    }

    /// <summary>Elenco delle codifiche reali tentate, nell'ordine di esecuzione.</summary>
    public static string[] Used(string directory)
    {
        string path = Path.Combine(directory, "used.txt");
        return File.Exists(path)
            ? File.ReadAllLines(path).Where(l => l.Length > 0).ToArray()
            : Array.Empty<string>();
    }
}

/// <summary>Cartella temporanea univoca, eliminata alla fine del test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "allsky-tl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Percorso assoluto della cartella.</summary>
    public string Path { get; }

    /// <summary>Elimina la cartella e il suo contenuto.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>Progress sincrono: il callback viene eseguito nel punto di Report, senza passare dal contesto di sincronizzazione.</summary>
internal sealed class SyncProgress : IProgress<double>
{
    private readonly Action<double> _onReport;

    public SyncProgress(Action<double> onReport)
    {
        _onReport = onReport;
    }

    public void Report(double value)
    {
        _onReport(value);
    }
}
