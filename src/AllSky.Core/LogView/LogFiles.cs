using System.Text;

namespace AllSky.Core.LogView;

/// <summary>Accesso ai file di log sul disco. Il file è scritto dall'Host mentre la UI lo legge: si apre sempre in condivisione.</summary>
public static class LogFiles
{
    /// <summary>Prefisso e estensione dei file giornalieri scritti da <c>DailyFileLog</c> (allsky-YYYYMMDD.log).</summary>
    public const string DailyPrefix = "allsky-";

    /// <summary>Estensione dei file di log.</summary>
    public const string DailyExtension = ".log";

    /// <summary>
    /// File giornaliero più recente nella cartella dei log, o null se non ce n'è. Il nome contiene la data (AAAAMMGG),
    /// quindi l'ordine alfabetico è quello cronologico.
    /// </summary>
    /// <param name="logsFolder">Cartella dei log.</param>
    public static string? LatestDailyLog(string logsFolder)
    {
        if (string.IsNullOrWhiteSpace(logsFolder) || !Directory.Exists(logsFolder))
        {
            return null;
        }

        return Directory.EnumerateFiles(logsFolder, DailyPrefix + "*" + DailyExtension)
            .Select(Path.GetFileName)
            .Where(name => name is not null && name.Length == DailyPrefix.Length + 8 + DailyExtension.Length)
            .OrderBy(name => name, StringComparer.Ordinal)
            .LastOrDefault() is { } latest
            ? Path.Combine(logsFolder, latest)
            : null;
    }

    /// <summary>
    /// Ultime righe di un file, leggendo al più <paramref name="maxBytes"/> dalla fine. Se il file è più lungo la prima
    /// riga parziale viene scartata. Restituisce una lista vuota se il file non esiste o non si può leggere.
    /// </summary>
    /// <param name="path">File da leggere.</param>
    /// <param name="maxBytes">Quanti byte finali leggere al massimo.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> non positivo.</exception>
    public static IReadOnlyList<string> ReadTailLines(string path, int maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Il numero di byte deve essere positivo.");
        }

        byte[] bytes;
        bool truncated;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, stream.Length - maxBytes);
            truncated = start > 0;
            stream.Seek(start, SeekOrigin.Begin);
            bytes = new byte[stream.Length - start];
            int read = 0;
            while (read < bytes.Length)
            {
                int n = stream.Read(bytes, read, bytes.Length - read);
                if (n == 0)
                {
                    break;
                }

                read += n;
            }

            Array.Resize(ref bytes, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        string text = Encoding.UTF8.GetString(bytes);
        string[] lines = text.Split('\n');
        int first = truncated ? 1 : 0;
        return lines.Skip(first).Select(l => l.TrimEnd('\r')).ToList();
    }
}
