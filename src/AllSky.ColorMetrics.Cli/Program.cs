using System.Globalization;
using System.Text.Json;
using AllSky.Archive;
using AllSky.ColorMetrics;
using AllSky.Timelapse;

namespace AllSky.ColorMetrics.Cli;

/// <summary>
/// Strumento di diagnosi del colore. Uso:
/// <c>AllSky.ColorMetrics.Cli &lt;cartella nights&gt; [--night &lt;id&gt;] [--out &lt;report.md&gt;] [--max-frames N] [--nota &lt;testo&gt;]</c>
/// Legge i JPEG e i sidecar dell'archivio in sola lettura. Scrive solo il report (e il JSON accanto, con lo stesso nome).
/// </summary>
internal static class Program
{
    private const string Usage =
        "Uso: AllSky.ColorMetrics.Cli <cartella nights> [--night <id>] [--out <report.md>] [--max-frames N] [--nota <testo>]";

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Errore negli argomenti: {ex.Message}");
            Console.Error.WriteLine(Usage);
            return 2;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Errore di lettura o scrittura: {ex.Message}");
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        string? folder = null;
        string? night = null;
        string? outPath = null;
        int maxFrames = 0;
        var notes = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--night":
                    night = Next(args, ref i, a);
                    break;
                case "--out":
                    outPath = Next(args, ref i, a);
                    break;
                case "--max-frames":
                    if (!int.TryParse(Next(args, ref i, a), NumberStyles.Integer, CultureInfo.InvariantCulture, out maxFrames) || maxFrames < 1)
                    {
                        throw new ArgumentException("--max-frames richiede un intero positivo.");
                    }

                    break;
                case "--nota":
                    notes.Add(Next(args, ref i, a));
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException($"opzione sconosciuta: {a}");
                    }

                    if (folder is not null)
                    {
                        throw new ArgumentException("troppe cartelle indicate.");
                    }

                    folder = a;
                    break;
            }
        }

        if (folder is null)
        {
            throw new ArgumentException("manca la cartella nights.");
        }

        string nightsRoot = Path.GetFullPath(folder);
        if (!Directory.Exists(nightsRoot))
        {
            throw new ArgumentException($"cartella non trovata: {nightsRoot}");
        }

        if (outPath is not null)
        {
            string full = Path.GetFullPath(outPath);
            string dataRoot = Path.GetFullPath(Path.Combine(nightsRoot, ".."));
            if (full.StartsWith(dataRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("il report non può stare nella cartella dati dell'archivio.");
            }

            outPath = full;
        }

        string[] nightIds = night is not null
            ? [night]
            : NightReader.ListNights(nightsRoot);
        if (nightIds.Length == 0)
        {
            Console.Error.WriteLine("Nessuna notte trovata nella cartella indicata.");
            return 1;
        }

        var results = new List<NightMetricsResult>();
        var series = new List<NightSeries>();
        foreach (string id in nightIds)
        {
            if (!NightArchive.IsValidNightId(id))
            {
                throw new ArgumentException($"identificativo di notte non valido: {id}");
            }

            (NightMetricsResult metrics, IReadOnlyList<FrameRow> rows) = AnalyzeNight(nightsRoot, id, maxFrames);
            results.Add(metrics);
            series.Add(new NightSeries(metrics, rows));
        }

        string markdown = MarkdownReport.Render(results, notes, DateTimeOffset.UtcNow);
        if (outPath is null)
        {
            Console.Out.Write(markdown);
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, markdown);
        string jsonPath = Path.ChangeExtension(outPath, ".json");
        var json = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            parameters = MarkdownReport.ParametersText,
            nights = series,
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(json, new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        }));
        Console.WriteLine($"Report: {outPath}");
        Console.WriteLine($"JSON:   {jsonPath}");
        return 0;
    }

    /// <summary>Metriche di una notte con la serie per frame (per ricontrollare i numeri nel JSON).</summary>
    private sealed record NightSeries(NightMetricsResult Metrics, IReadOnlyList<FrameRow> Frames);

    /// <summary>Misure di un frame: fondo cielo, fase, esposizione, guadagno e stelle.</summary>
    private sealed record FrameRow(
        DateTimeOffset TimeUtc,
        double MeanLuminance,
        double SkyR,
        double SkyG,
        double SkyB,
        string? Mode,
        double? ExposureMs,
        double? Gain,
        int Stars,
        double? StarRg,
        double? StarBg);

    private static (NightMetricsResult Metrics, IReadOnlyList<FrameRow> Rows) AnalyzeNight(string nightsRoot, string nightId, int maxFrames)
    {
        string nightDir = Path.Combine(nightsRoot, nightId);
        string framesDir = Path.Combine(nightDir, "frames");
        if (!Directory.Exists(framesDir))
        {
            throw new ArgumentException($"la notte {nightId} non ha la cartella frames.");
        }

        string[] jpgs = NightReader.ListFrameJpegs(framesDir, maxFrames);
        var samples = new List<FrameSample>(jpgs.Length);
        var stars = new List<StarColorFrame>(jpgs.Length);
        var rows = new List<FrameRow>(jpgs.Length);
        int skipped = 0;

        foreach (string jpg in jpgs)
        {
            FrameSidecar? sidecar = NightReader.ReadSidecar(jpg);
            if (sidecar is null || !JpegSize.TryRead(jpg, out int w, out int h))
            {
                skipped++;
                continue;
            }

            byte[]? pixels = FrameDecoder.Decode(jpg, w, h);
            if (pixels is null)
            {
                skipped++;
                continue;
            }

            var image = new Rgb8Image(w, h, pixels);
            FrameSample sample = FrameSampler.Create(image, sidecar);
            StarColorFrame star = StarColor.Measure(image, sample);
            samples.Add(sample);
            stars.Add(star);
            rows.Add(new FrameRow(sample.TimeUtc, sample.MeanLuminance, sample.SkyR, sample.SkyG, sample.SkyB, sample.Mode,
                sample.ExposureMs, sample.Gain, star.Stars, star.MedianRg, star.MedianBg));
        }

        (string? json, string note) = Ffprobe.ProbeColorMetadata(Path.Combine(nightDir, "timelapse.mp4"));
        EncoderColorInfo? encoder = json is null ? null : EncoderColorMetadata.Parse(json);
        return (NightMetrics.Compute(nightId, samples, stars, skipped, encoder, note), rows);
    }

    private static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} richiede un valore.");
        }

        i++;
        return args[i];
    }
}
