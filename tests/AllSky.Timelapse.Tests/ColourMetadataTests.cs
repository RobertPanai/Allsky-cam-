using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Xunit;

namespace AllSky.Timelapse.Tests;

/// <summary>
/// Metadati colore e decodifica del timelapse con ffmpeg reale (P14.3).
/// <para>
/// Dieci frame piatti di colore noto (JPEG generati con ffmpeg, RGB esatto) passano dal <see cref="TimelapseBuilder"/>
/// del progetto. Il test legge i metadati con ffprobe, decodifica il video in rgb24 usando la STESSA matrice dichiarata
/// nel file e misura l'errore medio assoluto per canale (0..255) rispetto al colore sorgente.
/// </para>
/// <para>
/// Tolleranza <see cref="ToleranceChannel"/> = 2.0 per canale. Perché: la misura attuale è circa 0.9 (con
/// <c>accurate_rnd+full_chroma_int</c>); il residuo viene da due arrotondamenti a 8 bit (JPEG-&gt;YUV BT.709 tv e
/// YUV-&gt;RGB) e dal sottocampionamento 4:2:0, che sui colori piatti è esatto. Una matrice sbagliata invece dà errori
/// di 3.8 (matrice 601 letta su un video 709) e di 9.3 (tag 709 senza conversione), quindi la tolleranza le separa.
/// </para>
/// <para>
/// Se ffmpeg, ffprobe o libx264 non si trovano il test scrive «SKIP» con il motivo e ritorna senza fallire
/// (come gli altri test d'integrazione del progetto: lo shim di test non ha un Skip dinamico).
/// Le misure «prima» e «dopo» sono stampate sulla console del test.
/// </para>
/// </summary>
public class ColourMetadataTests
{
    private const int Width = 640;
    private const int Height = 480;
    private const int Fps = 25;
    private const double ToleranceChannel = 2.0;

    // Dieci colori piatti noti (RGB), un frame ciascuno. Il nome serve solo ai messaggi.
    private static readonly (string Name, int R, int G, int B)[] Colours =
    [
        ("rosso", 255, 0, 0),
        ("verde", 0, 255, 0),
        ("blu", 0, 0, 255),
        ("grigio 50%", 128, 128, 128),
        ("bianco", 255, 255, 255),
        ("nero", 0, 0, 0),
        ("desaturato", 150, 160, 170),
        ("blu notte", 10, 20, 50),
        ("arancio", 230, 120, 40),
        ("giallo tenue", 240, 220, 60),
    ];

    [Fact]
    public async Task Video_DeclaresBt709AndDecodesToSourceColours()
    {
        (string Ffmpeg, string Ffprobe)? tools = await RequireToolsAsync();
        if (tools is null)
        {
            return;
        }

        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = await WriteColourFramesAsync(tools.Value.Ffmpeg, dir.Path);
        string output = Path.Combine(dir.Path, "colori.mp4");

        await new TimelapseBuilder().BuildAsync(
            jpegs,
            output,
            new TimelapseOptions { Fps = Fps, Encoder = "libx264", FfmpegPath = tools.Value.Ffmpeg },
            null,
            CancellationToken.None);

        Dictionary<string, string> declared = await ProbeColourAsync(tools.Value.Ffprobe, output);
        Assert.Equal("yuv420p", declared["pix_fmt"]);
        Assert.Equal("tv", declared["color_range"]);
        Assert.Equal("bt709", declared["color_space"]);
        Assert.Equal("bt709", declared["color_transfer"]);
        Assert.Equal("bt709", declared["color_primaries"]);

        // Decodifica con la matrice dichiarata nel file (bt709).
        byte[] rgb = await DecodeRgbAsync(tools.Value.Ffmpeg, output, MatrixOf(declared["color_space"]));
        double[][] errors = MeanAbsoluteError(rgb);

        Console.WriteLine("DOPO (scale bt709 tv + tag bt709, libx264, decodifica bt709):");
        PrintErrors(errors);
        double[] mean = MeanPerChannel(errors);
        for (int c = 0; c < 3; c++)
        {
            Assert.True(
                mean[c] <= ToleranceChannel,
                $"canale {"RGB"[c]}: errore medio {mean[c].ToString("F2", CultureInfo.InvariantCulture)} oltre la tolleranza {ToleranceChannel}");
        }
    }

    [Fact]
    public async Task Prima_DichiaravaBt470bgSenzaTrasferimentoNeiPrimari()
    {
        // Ricostruisce gli argomenti PRIMA della modifica P14.3 (scale senza matrice, nessun tag colore,
        // nessun bitstream filter) per misurare il punto di partenza con lo stesso sorgente.
        (string Ffmpeg, string Ffprobe)? tools = await RequireToolsAsync();
        if (tools is null)
        {
            return;
        }

        using var dir = new TempDirectory();
        IReadOnlyList<string> jpegs = await WriteColourFramesAsync(tools.Value.Ffmpeg, dir.Path);
        string output = Path.Combine(dir.Path, "prima.mp4");
        byte[] stdin = jpegs.SelectMany(path => File.ReadAllBytes(path)).ToArray();

        string[] before =
        [
            "-hide_banner", "-loglevel", "warning", "-y",
            "-f", "image2pipe", "-framerate", Fps.ToString(CultureInfo.InvariantCulture), "-c:v", "mjpeg", "-i", "-",
            "-vf", $"scale={Width}:{Height}",
            "-r", Fps.ToString(CultureInfo.InvariantCulture),
            "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "23",
            "-movflags", "+faststart", "-f", "mp4", output,
        ];
        ProcessOutput encoded = await RunAsync(tools.Value.Ffmpeg, before, stdin);
        Assert.True(encoded.ExitCode == 0, "codifica PRIMA fallita: " + encoded.Stderr);

        Dictionary<string, string> declared = await ProbeColourAsync(tools.Value.Ffprobe, output);
        Assert.Equal("bt470bg", declared["color_space"]);
        Assert.Equal(string.Empty, declared["color_transfer"]);
        Assert.Equal(string.Empty, declared["color_primaries"]);

        byte[] rgb = await DecodeRgbAsync(tools.Value.Ffmpeg, output, MatrixOf(declared["color_space"]));
        double[][] errors = MeanAbsoluteError(rgb);

        Console.WriteLine("PRIMA (matrice bt470bg, nessun trasferimento o primari, decodifica bt601):");
        PrintErrors(errors);
    }

    // ---- Strumenti ----

    private static async Task<(string Ffmpeg, string Ffprobe)?> RequireToolsAsync()
    {
        string? ffmpeg = FfmpegLocator.Locate();
        if (ffmpeg is null)
        {
            Console.WriteLine("SKIP ColourMetadataTests: ffmpeg non trovato (percorso, tools/ffmpeg o PATH).");
            return null;
        }

        string? ffprobe = TestFiles.FfprobeNextTo(ffmpeg);
        if (ffprobe is null)
        {
            Console.WriteLine("SKIP ColourMetadataTests: ffprobe non trovato accanto a ffmpeg.");
            return null;
        }

        var policy = new EncoderPolicy { Preferred = ["libx264"], AllowGpl = true };
        WorkingEncoders working = await EncoderProbe.GetWorkingAsync(ffmpeg, policy);
        if (!working.IsAvailable)
        {
            Console.WriteLine("SKIP ColourMetadataTests: libx264 non disponibile in questo ffmpeg.");
            return null;
        }

        return (ffmpeg, ffprobe);
    }

    /// <summary>Genera un JPEG piatto per ogni colore con ffmpeg (lavfi color, yuvj420p, qualità massima).</summary>
    private static async Task<IReadOnlyList<string>> WriteColourFramesAsync(string ffmpeg, string directory)
    {
        var paths = new List<string>(Colours.Length);
        for (int i = 0; i < Colours.Length; i++)
        {
            (_, int r, int g, int b) = Colours[i];
            string path = Path.Combine(directory, $"colore_{i:D02}.jpg");
            string colour = $"0x{r:X2}{g:X2}{b:X2}";
            ProcessOutput result = await RunAsync(ffmpeg, [
                "-hide_banner", "-loglevel", "error", "-y",
                "-f", "lavfi", "-i", $"color=c={colour}:s={Width}x{Height}",
                "-frames:v", "1", "-q:v", "1", "-pix_fmt", "yuvj420p", path,
            ]);
            Assert.True(result.ExitCode == 0, "generazione del frame di prova fallita: " + result.Stderr);
            paths.Add(path);
        }

        return paths;
    }

    /// <summary>Parser minimo di ffprobe: valori dei campi colore del primo stream video (vuoto = non dichiarato).</summary>
    private static async Task<Dictionary<string, string>> ProbeColourAsync(string ffprobe, string file)
    {
        ProcessOutput result = await RunAsync(ffprobe, [
            "-v", "error", "-select_streams", "v:0",
            "-show_entries", "stream=pix_fmt,color_range,color_space,color_transfer,color_primaries",
            "-of", "json", file,
        ]);
        Assert.True(result.ExitCode == 0, "ffprobe fallito: " + result.Stderr);

        using JsonDocument document = JsonDocument.Parse(result.Stdout);
        JsonElement stream = document.RootElement.GetProperty("streams")[0];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in new[] { "pix_fmt", "color_range", "color_space", "color_transfer", "color_primaries" })
        {
            values[key] = stream.TryGetProperty(key, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;
        }

        return values;
    }

    /// <summary>Decodifica tutti i frame in rgb24 (range pieno) usando la matrice indicata per l'ingresso (range tv).</summary>
    private static async Task<byte[]> DecodeRgbAsync(string ffmpeg, string file, string matrix)
    {
        string filter = $"scale=in_color_matrix={matrix}:in_range=tv:out_range=pc,format=rgb24";
        ProcessOutput result = await RunAsync(ffmpeg, [
            "-hide_banner", "-loglevel", "error", "-i", file,
            "-vf", filter, "-f", "rawvideo", "-pix_fmt", "rgb24", "-",
        ]);
        Assert.True(result.ExitCode == 0, "decodifica fallita: " + result.Stderr);
        return result.Stdout;
    }

    /// <summary>Matrice da passare alla decodifica: bt470bg (SD, 601) e bt709 sono gli unici valori attesi.</summary>
    private static string MatrixOf(string colorSpace) => colorSpace switch
    {
        "bt709" => "bt709",
        "bt470bg" => "bt601",
        _ => throw new InvalidOperationException($"matrice dichiarata inattesa: '{colorSpace}'"),
    };

    /// <summary>Errore medio assoluto per frame e per canale: [frame][canale], confronto con il colore piatto nominale.</summary>
    private static double[][] MeanAbsoluteError(byte[] rgb)
    {
        int frameBytes = Width * Height * 3;
        Assert.True(rgb.Length == Colours.Length * frameBytes, "numero di frame decodificati diverso da quello atteso");

        var errors = new double[Colours.Length][];
        for (int k = 0; k < Colours.Length; k++)
        {
            (_, int r, int g, int b) = Colours[k];
            int[] expected = [r, g, b];
            var sums = new double[3];
            int start = k * frameBytes;
            for (int p = start; p < start + frameBytes; p += 3)
            {
                for (int c = 0; c < 3; c++)
                {
                    sums[c] += Math.Abs(rgb[p + c] - expected[c]);
                }
            }

            errors[k] = sums.Select(s => s / (Width * Height)).ToArray();
        }

        return errors;
    }

    private static double[] MeanPerChannel(double[][] errors)
    {
        var mean = new double[3];
        foreach (double[] frame in errors)
        {
            for (int c = 0; c < 3; c++)
            {
                mean[c] += frame[c] / errors.Length;
            }
        }

        return mean;
    }

    private static void PrintErrors(double[][] errors)
    {
        double[] mean = MeanPerChannel(errors);
        Console.WriteLine(
            "  media per canale: R=" + mean[0].ToString("F2", CultureInfo.InvariantCulture) +
            " G=" + mean[1].ToString("F2", CultureInfo.InvariantCulture) +
            " B=" + mean[2].ToString("F2", CultureInfo.InvariantCulture));
        for (int k = 0; k < Colours.Length; k++)
        {
            double[] e = errors[k];
            Console.WriteLine(
                $"  {Colours[k].Name,-12} R={e[0].ToString("F2", CultureInfo.InvariantCulture)} " +
                $"G={e[1].ToString("F2", CultureInfo.InvariantCulture)} B={e[2].ToString("F2", CultureInfo.InvariantCulture)}");
        }
    }

    private sealed record ProcessOutput(int ExitCode, byte[] Stdout, string Stderr);

    /// <summary>Esegue un processo senza shell (ArgumentList) e raccoglie stdout (binario) e stderr.</summary>
    private static async Task<ProcessOutput> RunAsync(string executable, IReadOnlyList<string> arguments, byte[]? stdin = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
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

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"impossibile avviare {executable}");
        var stdout = new MemoryStream();
        Task copyOut = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        Task<string> readErr = process.StandardError.ReadToEndAsync();

        if (stdin is not null)
        {
            await process.StandardInput.BaseStream.WriteAsync(stdin);
        }

        process.StandardInput.Close();
        await Task.WhenAll(copyOut, readErr);
        await process.WaitForExitAsync();
        return new ProcessOutput(process.ExitCode, stdout.ToArray(), await readErr);
    }
}
