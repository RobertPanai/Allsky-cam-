using System.Text.Json;

namespace AllSky.Studio.Tests;

/// <summary>Dati sintetici per i test di montaggio: frame con nomi e istanti noti, operazioni da JSON.</summary>
internal static class MontageFixtures
{
    /// <summary>Primo istante della sequenza sintetica (UTC).</summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    /// <summary>Frame "frame_0000.jpg", "frame_0001.jpg", ... distanziati di <paramref name="step"/> (1 s di default).</summary>
    public static List<StudioFrame> Frames(int count, TimeSpan? step = null)
    {
        TimeSpan gap = step ?? TimeSpan.FromSeconds(1);
        var frames = new List<StudioFrame>(count);
        for (int i = 0; i < count; i++)
        {
            string name = $"frame_{i:0000}.jpg";
            frames.Add(new StudioFrame(name, "/dati/sintetici/" + name, Start + TimeSpan.FromTicks(gap.Ticks * i)));
        }

        return frames;
    }

    /// <summary>Operazione del progetto con i parametri scritti come JSON oggetto.</summary>
    public static StudioOperation Op(string type, string parametersJson = "{}")
    {
        using JsonDocument document = JsonDocument.Parse(parametersJson);
        var parameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            parameters[property.Name] = property.Value.Clone();
        }

        return new StudioOperation { Type = type, Parameters = parameters };
    }

    /// <summary>Statistiche uguali per tutti i frame.</summary>
    public static Func<StudioFrame, FrameStats> UniformStats(double mean = 100, double stdDev = 20)
    {
        return _ => new FrameStats(mean, stdDev);
    }

    /// <summary>Statistiche che fanno fallire il test se vengono richieste.</summary>
    public static Func<StudioFrame, FrameStats> NoStatsExpected()
    {
        return _ => throw new InvalidOperationException("le statistiche non dovevano essere richieste.");
    }
}
