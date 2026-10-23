// TEMPORANEO: impronta dell'output del processore prima delle modifiche di P14.2. Da rimuovere.
using System.Security.Cryptography;
using System.Text;
using AllSky.Astro;
using AllSky.Core;
using AllSky.Imaging.Jpeg;
using Xunit;

namespace AllSky.Processing.Tests;

public class ZzBaselineHashTemp
{
    [Fact]
    public void PrintBaselineHash()
    {
        var cfg = new AppConfig { Overlay = new AppConfig.OverlayConfig { Enabled = true, Label = "Test" } };
        var proc = new FrameProcessor(cfg, new BaselineJpegEncoder());
        DateTimeOffset night = new(2026, 10, 19, 0, 30, 0, TimeSpan.Zero);
        DateTimeOffset day = new(2026, 10, 19, 11, 0, 0, TimeSpan.Zero);
        var sb = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            bool isDay = i >= 12;
            DateTimeOffset ts = isDay ? day.AddSeconds(30 * (i - 12)) : night.AddSeconds(30 * i);
            double exp = isDay ? 2 : (i % 8 < 4 ? 30000 : 60000);
            int w = 320, h = 240;
            var rnd = new Random(100 + i);
            var px = new ushort[w * h];
            for (int k = 0; k < px.Length; k++)
            {
                px[k] = (ushort)Math.Clamp((isDay ? 9000 : 3000) + rnd.Next(-60, 61) + (k % 97 == 0 ? 20000 : 0), 0, 65535);
            }
            if (i == 5)
            {
                cfg = cfg with { Color = cfg.Color with { Smoothing = 0.5 } };
                proc.UpdateConfig(cfg);
            }
            var raw = new RawFrame(w, h, px, BayerPattern.RGGB, ts, exp, 250, 15.0);
            ProcessedFrame f = proc.Process(raw);
            sb.Append(Convert.ToHexString(SHA256.HashData(f.Rgb8))).Append(' ');
            sb.Append(Convert.ToHexString(SHA256.HashData(f.Jpeg))).Append(' ');
            sb.Append(Convert.ToHexString(SHA256.HashData(f.ThumbJpeg))).Append('\n');
        }
        string all = sb.ToString();
        Console.WriteLine("BASELINE-HASH-BEGIN\n" + all + "BASELINE-HASH-END");
        File.WriteAllText("/home/claude/allsky-scratch/baseline/" + Environment.GetEnvironmentVariable("BASELINE_TAG") + ".txt", all);
    }
}
