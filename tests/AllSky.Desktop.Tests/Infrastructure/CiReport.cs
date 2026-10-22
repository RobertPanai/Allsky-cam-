using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

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

    /// <summary>Riga nel formato <c>::{livello}::{testo}</c>, senza titolo: l'errore o la notizia è il testo stesso.</summary>
    public static void Raw(string level, string text, ITestOutputHelper? output) =>
        WriteRaw($"::{level}::{EscapeData(text)}", output);

    /// <summary>
    /// Errore non gestito di un test o della fixture: <c>::error::{dove}: {tipo}: {prima riga} @ {file:riga}</c>.
    /// Il chiamante deve poi rilanciare l'eccezione, salvo dove la fixture pulisce e non deve far fallire la sessione.
    /// </summary>
    public static void Fault(string where, Exception ex, ITestOutputHelper? output) =>
        Raw("error", $"{where}: {ex.GetType().FullName}: {FirstLine(ex.Message)} @ {FirstFrame(ex)}", output);

    private static void Write(string level, string title, string message, ITestOutputHelper? output) =>
        WriteRaw($"::{level} title={EscapeProperty(title)}::{EscapeData(message)}", output);

    private static void WriteRaw(string line, ITestOutputHelper? output)
    {
        lock (Gate)
        {
            Lines.Add(line);
        }

        Console.Out.WriteLine(line);
        output?.WriteLine(line);
    }

    private static string FirstLine(string? text) =>
        (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// Prima riga dello stack con file e numero di riga (percorso completo); se nessun frame ha il file, il primo metodo.
    /// </summary>
    private static string FirstFrame(Exception ex)
    {
        var trace = new System.Diagnostics.StackTrace(ex, fNeedFileInfo: true);
        if (trace.FrameCount == 0)
        {
            return "stack vuoto";
        }

        for (int i = 0; i < trace.FrameCount; i++)
        {
            System.Diagnostics.StackFrame? frame = trace.GetFrame(i);
            string? file = frame?.GetFileName();
            if (frame is not null && file is not null)
            {
                return $"{file}:{frame.GetFileLineNumber()}";
            }
        }

        return trace.GetFrame(0)?.GetMethod()?.ToString() ?? "metodo sconosciuto";
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
