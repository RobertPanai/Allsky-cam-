using System.Diagnostics;

namespace AllSky.ColorMetrics.Cli;

/// <summary>Esegue ffprobe sul timelapse per i metadati colore. Se manca il file o ffprobe, restituisce una nota «non verificabile».</summary>
internal static class Ffprobe
{
    private const string ShowEntries = "stream=codec_name,pix_fmt,color_range,color_space,color_transfer,color_primaries,width,height,r_frame_rate";

    /// <summary>Restituisce il JSON di ffprobe, oppure null con il motivo.</summary>
    public static (string? Json, string Note) ProbeColorMetadata(string mp4Path)
    {
        if (!File.Exists(mp4Path))
        {
            return (null, "timelapse.mp4 assente nella cartella della notte");
        }

        string? exe = FindOnPath(OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        if (exe is null)
        {
            return (null, "ffprobe non trovato nel PATH");
        }

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string a in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", ShowEntries, "-of", "json", mp4Path })
        {
            psi.ArgumentList.Add(a);
        }

        using Process process = Process.Start(psi) ?? throw new IOException("ffprobe non avviabile.");
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = stderr.Result;
        if (process.ExitCode != 0)
        {
            return (null, $"ffprobe ha restituito codice {process.ExitCode}");
        }

        return (stdout, "ffprobe eseguito");
    }

    private static string? FindOnPath(string fileName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir.Trim('"'), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
