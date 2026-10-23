using System.Text.Json;
using AllSky.Archive;

namespace AllSky.ColorMetrics.Cli;

/// <summary>Lettura in sola lettura della cartella nights: notti, JPEG dei frame e sidecar.</summary>
internal static class NightReader
{
    /// <summary>Identificativi delle notti (cartelle con nome valido e una sottocartella frames), in ordine.</summary>
    public static string[] ListNights(string nightsRoot) =>
        Directory.GetDirectories(nightsRoot)
            .Select(Path.GetFileName)
            .Where(n => n is not null && NightArchive.IsValidNightId(n) && Directory.Exists(Path.Combine(nightsRoot, n, "frames")))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// JPEG dei frame in ordine di nome (che è in ordine di tempo). Con <paramref name="maxFrames"/> &gt; 0 e più frame,
    /// sceglie <paramref name="maxFrames"/> frame distribuiti uniformemente.
    /// </summary>
    public static string[] ListFrameJpegs(string framesDir, int maxFrames)
    {
        string[] all = Directory.GetFiles(framesDir, "*.jpg")
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
            .ToArray();
        if (maxFrames <= 0 || all.Length <= maxFrames)
        {
            return all;
        }

        if (maxFrames == 1)
        {
            return [all[0]];
        }

        var picked = new string[maxFrames];
        for (int k = 0; k < maxFrames; k++)
        {
            int idx = (int)Math.Round(k * (double)(all.Length - 1) / (maxFrames - 1));
            picked[k] = all[idx];
        }

        return picked;
    }

    /// <summary>Legge il sidecar accanto al JPEG (stesso nome, estensione .json). Null se manca o non è valido.</summary>
    public static FrameSidecar? ReadSidecar(string jpgPath)
    {
        string json = Path.ChangeExtension(jpgPath, ".json");
        if (!File.Exists(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FrameSidecar>(File.ReadAllText(json), FrameSidecars.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
