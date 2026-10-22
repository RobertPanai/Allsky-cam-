using System.Text;
using Xunit;

namespace AllSky.Desktop.Tests;

/// <summary>
/// Diagnostica leggibile dal log del runner GitHub: righe nel formato dei comandi di workflow
/// (<c>::notice</c> ed <c>::error</c>). Con la variabile <c>ALLSKY_WRITE_SHOTS</c> impostata, le stesse righe
/// finiscono anche in <c>docs/ralph/shots/ci-report.txt</c>.
/// </summary>
internal static class CiReport
{
    /// <summary>Variabile d'ambiente che abilita la scrittura di ci-report.txt nel repository.</summary>
    public const string WriteShotsVariable = "ALLSKY_WRITE_SHOTS";

    private static readonly object Gate = new();
    private static readonly List<string> Lines = new();

    /// <summary>Esito positivo di un caso o un'informazione.</summary>
    public static void Notice(string title, string message, ITestOutputHelper? output) =>
        Write("notice", title, message, output);

    /// <summary>Esito negativo di un caso, con messaggio ed estratto dello stack.</summary>
    public static void Error(string title, string message, ITestOutputHelper? output) =>
        Write("error", title, message, output);

    /// <summary>Escape dei dati di un comando di workflow: <c>%</c>, CR e LF.</summary>
    public static string EscapeData(string text) =>
        text.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);

    /// <summary>Escape di una proprietà (titolo) di un comando di workflow: come i dati, più <c>:</c> e <c>,</c>.</summary>
    public static string EscapeProperty(string text) =>
        EscapeData(text).Replace(":", "%3A", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal);

    /// <summary>
    /// Scrive ci-report.txt solo se <c>ALLSKY_WRITE_SHOTS</c> è impostata. Senza la variabile non fa nulla.
    /// </summary>
    public static void FlushIfRequested(ITestOutputHelper? output)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WriteShotsVariable)))
        {
            return;
        }

        string? root = FindRepositoryRoot();
        if (root is null)
        {
            Notice("ci-report", "Radice del repository non trovata: ci-report.txt non scritto.", output);
            return;
        }

        string folder = Path.Combine(root, "docs", "ralph", "shots");
        Directory.CreateDirectory(folder);
        lock (Gate)
        {
            File.WriteAllLines(Path.Combine(folder, "ci-report.txt"), Lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static void Write(string level, string title, string message, ITestOutputHelper? output)
    {
        string line = $"::{level} title={EscapeProperty(title)}::{EscapeData(message)}";
        lock (Gate)
        {
            Lines.Add(line);
        }

        Console.Out.WriteLine(line);
        output?.WriteLine(line);
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AllSky.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
