using System.Diagnostics;

namespace AllSky.ColorMetrics.Cli;

/// <summary>
/// Decodifica di un JPEG dell'archivio in RGB 8 bit tramite ffmpeg (in PATH). Il progetto ha il solo codificatore JPEG
/// (AllSky.Imaging.Jpeg): non esiste un decodificatore riusabile fuori da Windows, quindi si usa ffmpeg, già richiesto
/// dal timelapse. Nessun file viene scritto: l'output va su stdout.
/// </summary>
internal static class FrameDecoder
{
    /// <summary>Restituisce i pixel RGB (width*height*3) o null se ffmpeg fallisce o la dimensione non torna.</summary>
    public static byte[]? Decode(string jpgPath, int width, int height)
    {
        var psi = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(jpgPath);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("rawvideo");
        psi.ArgumentList.Add("-pix_fmt");
        psi.ArgumentList.Add("rgb24");
        psi.ArgumentList.Add("-");

        using Process process = Process.Start(psi)
            ?? throw new IOException("ffmpeg non avviabile (serve nel PATH per decodificare i JPEG).");
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var ms = new MemoryStream(width * height * 3);
        process.StandardOutput.BaseStream.CopyTo(ms);
        process.WaitForExit();
        _ = stderr.Result;
        if (process.ExitCode != 0 || ms.Length != (long)width * height * 3)
        {
            return null;
        }

        return ms.ToArray();
    }
}
