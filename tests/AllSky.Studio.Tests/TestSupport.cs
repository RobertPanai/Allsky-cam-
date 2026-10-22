using Xunit;
using System.Text;
using AllSky.Core;

namespace AllSky.Studio.Tests;

/// <summary>Cartella temporanea usa e getta per i test.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "allsky-studio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string FilePath(string name) => System.IO.Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Costruisce byte JPEG con un segmento EXIF scritto a mano, in little o big endian.</summary>
internal static class JpegBuilder
{
    /// <summary>JPEG con DateTimeOriginal e/o DateTime (formato "AAAA:MM:GG HH:MM:SS"). Null = tag assente.</summary>
    public static byte[] WithExif(string? dateOriginal, string? dateTime, bool bigEndian = false, int? cutAt = null)
    {
        var tiff = BuildTiff(dateOriginal, dateTime, bigEndian);
        var bytes = new List<byte>
        {
            0xFF, 0xD8,                 // SOI
            0xFF, 0xE1,                 // APP1
        };
        int length = 2 + 6 + tiff.Count;
        bytes.Add((byte)(length >> 8));
        bytes.Add((byte)(length & 0xFF));
        bytes.AddRange("Exif\0\0"u8.ToArray());
        bytes.AddRange(tiff);
        bytes.Add(0xFF);
        bytes.Add(0xD9);                // EOI
        byte[] result = bytes.ToArray();
        if (cutAt is int cut && cut < result.Length)
        {
            Array.Resize(ref result, cut);
        }

        return result;
    }

    /// <summary>JPEG minimo senza EXIF (solo SOI, APP0 e EOI).</summary>
    public static byte[] Plain()
    {
        return
        [
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xD9,
        ];
    }

    private static List<byte> BuildTiff(string? dateOriginal, string? dateTime, bool be)
    {
        int ifd0Entries = (dateTime is null ? 0 : 1) + (dateOriginal is null ? 0 : 1);
        int exifEntries = dateOriginal is null ? 0 : 1;
        // Layout: header(8), IFD0, IFD Exif, poi le stringhe.
        int ifd0Size = 2 + (ifd0Entries * 12) + 4;
        int exifIfdOffset = 8 + ifd0Size;
        int exifSize = 2 + (exifEntries * 12) + 4;
        int dataOffset = exifIfdOffset + exifSize;
        int dtoOffset = dataOffset;
        int dtOffset = dataOffset + (dateOriginal is null ? 0 : 20);

        var t = new List<byte>();
        t.Add(be ? (byte)'M' : (byte)'I');
        t.Add(be ? (byte)'M' : (byte)'I');
        Put16(t, 42, be);
        Put32(t, 8, be);

        Put16(t, ifd0Entries, be);
        if (dateTime is not null)
        {
            Entry(t, 0x0132, 2, 20, dtOffset, be);
        }

        if (dateOriginal is not null)
        {
            Entry(t, 0x8769, 4, 1, exifIfdOffset, be);
        }

        Put32(t, 0, be);

        Put16(t, exifEntries, be);
        if (dateOriginal is not null)
        {
            Entry(t, 0x9003, 2, 20, dtoOffset, be);
        }

        Put32(t, 0, be);

        if (dateOriginal is not null)
        {
            t.AddRange(Ascii(dateOriginal));
        }

        if (dateTime is not null)
        {
            t.AddRange(Ascii(dateTime));
        }

        return t;
    }

    private static void Entry(List<byte> t, ushort tag, ushort type, uint count, int value, bool be)
    {
        Put16(t, tag, be);
        Put16(t, type, be);
        Put32(t, count, be);
        Put32(t, (uint)value, be);
    }

    private static byte[] Ascii(string value)
    {
        byte[] text = Encoding.ASCII.GetBytes(value);
        var result = new byte[text.Length + 1]; // con il NUL finale
        Array.Copy(text, result, text.Length);
        return result;
    }

    private static void Put16(List<byte> t, int v, bool be)
    {
        if (be)
        {
            t.Add((byte)(v >> 8));
            t.Add((byte)v);
        }
        else
        {
            t.Add((byte)v);
            t.Add((byte)(v >> 8));
        }
    }

    private static void Put32(List<byte> t, uint v, bool be)
    {
        byte[] b = [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
        if (!be)
        {
            Array.Reverse(b);
        }

        t.AddRange(b);
    }

    private static void Put32(List<byte> t, int v, bool be) => Put32(t, (uint)v, be);
}

/// <summary>Orologio fisso per i test dell'archivio.</summary>
internal sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now)
    {
        UtcNow = now;
    }

    public DateTimeOffset UtcNow { get; }
}

/// <summary>Esecutore di processi finto: registra gli argomenti e simula l'esito.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    /// <summary>Comportamento simulato. Riceve gli argomenti e il token.</summary>
    public Func<IReadOnlyList<string>, CancellationToken, Task<ProcessRunResult>> Behaviour { get; set; }
        = (_, _) => Task.FromResult(new ProcessRunResult(0, string.Empty));

    public int Calls { get; private set; }

    public string? LastFileName { get; private set; }

    public IReadOnlyList<string> LastArguments { get; private set; } = [];

    public Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        Calls++;
        LastFileName = fileName;
        LastArguments = arguments.ToList();
        return Behaviour(arguments, ct);
    }
}

/// <summary>Lettore d'archivio finto, con file JPEG condivisi e frame "mancanti" configurabili.</summary>
internal sealed class FakeArchiveReader : IArchiveReader
{
    private readonly string _existingFile;

    public FakeArchiveReader(string existingFile, IReadOnlyList<ArchiveFrame> frames)
    {
        _existingFile = existingFile;
        Frames = frames;
    }

    public IReadOnlyList<ArchiveFrame> Frames { get; }

    public HashSet<string> MissingIds { get; } = new(StringComparer.Ordinal);

    public List<int> PageOffsets { get; } = new();

    public int ListCalls => PageOffsets.Count;

    public IReadOnlyList<ArchiveFrame> ListFrames(string nightId, int offset, int limit)
    {
        PageOffsets.Add(offset);
        return Frames.Skip(offset).Take(limit).ToList();
    }

    public string GetFramePath(string nightId, string frameId)
    {
        return MissingIds.Contains(frameId)
            ? Path.Combine(Path.GetDirectoryName(_existingFile)!, "assente-" + frameId + ".jpg")
            : _existingFile;
    }
}
