using AllSky.Archive;
using AllSky.Core;

namespace AllSky.Host.Tests.YouTube;

/// <summary>Orologio fisso per i test: nessuna attesa reale.</summary>
internal sealed class FixedCoreClock : AllSky.Core.IClock
{
    public FixedCoreClock(DateTimeOffset now) => UtcNow = now;

    public DateTimeOffset UtcNow { get; set; }
}

/// <summary>Cartella temporanea per l'archivio, rimossa alla chiusura.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "allsky-yt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // File ancora aperto: la cartella temporanea di sistema viene ripulita dopo.
        }
    }
}

/// <summary>Crea notte con due frame e, opzionalmente, il timelapse MP4 e lo stato.</summary>
internal static class NightFixtures
{
    public static void AddNight(NightArchive archive, string nightId, bool withTimelapse, TimelapseState? state = null)
    {
        // Frame di prova: l'archivio tratta i JPEG come byte opachi.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
        DateTimeOffset start = DateTimeOffset.Parse(nightId + "T22:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        archive.SaveFrame(start, "night", jpeg, jpeg, new FrameMeta(30, 250, 100));
        archive.SaveFrame(start.AddMinutes(1), "night", jpeg, jpeg, new FrameMeta(30, 250, 100));

        if (withTimelapse)
        {
            File.WriteAllBytes(archive.GetProductPath(nightId, NightProduct.Timelapse), [0x00, 0x01]);
        }

        if (state is not null)
        {
            archive.SetTimelapseState(nightId, state.Value);
        }
    }
}
