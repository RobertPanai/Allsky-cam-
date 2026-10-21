using AllSky.Core;
using AllSky.Publish;
using Xunit;

namespace AllSky.Publish.Tests.Settings;

public sealed class PublishLogTests : IDisposable
{
    private readonly TempFolder _dir = new();

    public void Dispose() => _dir.Dispose();

    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 21, 30, 0, 123, TimeSpan.Zero);

    private string TodayFile => Path.Combine(_dir.Path, "publish-20261021.log");

    private static string[] ReadLines(string path) => File.ReadAllLines(path);

    [Fact]
    public void Info_WritesExpectedLineFormat()
    {
        var log = new PublishLog(_dir.Path, new FakeClock(T0));

        log.Info("avvio");

        string[] lines = ReadLines(TodayFile);
        Assert.Single(lines);
        Assert.Equal("2026-10-21T21:30:00.123Z [INFO] avvio", lines[0]);
    }

    [Fact]
    public void Warn_AndError_UseTheirLevels()
    {
        var log = new PublishLog(_dir.Path, new FakeClock(T0));

        log.Warn("attenzione");
        log.Error("guasto");

        string[] lines = ReadLines(TodayFile);
        Assert.Equal(2, lines.Length);
        Assert.Equal("2026-10-21T21:30:00.123Z [WARN] attenzione", lines[0]);
        Assert.Equal("2026-10-21T21:30:00.123Z [ERROR] guasto", lines[1]);
    }

    [Fact]
    public void Error_WithException_AddsTypeAndMessageOnly()
    {
        var log = new PublishLog(_dir.Path, new FakeClock(T0));

        log.Error("invio fallito", new InvalidOperationException("boom"));

        string text = File.ReadAllText(TodayFile);
        Assert.Contains("[ERROR] invio fallito | System.InvalidOperationException: boom", text);
        Assert.DoesNotContain("   at ", text);
    }

    [Fact]
    public void Sanitize_IsAppliedToTextAndExceptionMessage()
    {
        var log = new PublishLog(_dir.Path, new FakeClock(T0), s => s.Replace("SEGRETO", "***"));

        log.Error("token SEGRETO", new Exception("errore con SEGRETO dentro"));

        string text = File.ReadAllText(TodayFile);
        Assert.DoesNotContain("SEGRETO", text);
        Assert.Contains("token ***", text);
        Assert.Contains("errore con *** dentro", text);
    }

    [Fact]
    public void FailingSanitizer_WritesPlaceholderNotOriginal()
    {
        var log = new PublishLog(_dir.Path, new FakeClock(T0), _ => throw new InvalidOperationException("x"));

        log.Info("SEGRETO-ORIGINALE");

        string text = File.ReadAllText(TodayFile);
        Assert.DoesNotContain("SEGRETO-ORIGINALE", text);
        Assert.Contains("(testo non registrabile)", text);
    }

    [Fact]
    public void Newlines_BecomeSingleLine()
    {
        var log = new PublishLog(_dir.Path, new FakeClock(T0));

        log.Info("riga uno\r\nriga due\nriga tre");

        string[] lines = ReadLines(TodayFile);
        Assert.Single(lines);
        Assert.Equal("2026-10-21T21:30:00.123Z [INFO] riga uno riga due riga tre", lines[0]);
    }

    [Fact]
    public void LongText_IsTruncatedTo500Characters()
    {
        var log = new PublishLog(_dir.Path, new FakeClock(T0));

        log.Info(new string('x', 1000));

        string line = ReadLines(TodayFile).Single();
        string message = line.Substring(line.IndexOf("[INFO] ", StringComparison.Ordinal) + "[INFO] ".Length);
        Assert.True(message.Length <= 500, "Lunghezza messaggio: " + message.Length);
        Assert.EndsWith("...", message);
    }

    [Fact]
    public void AsInfo_ReturnsWorkingCallback()
    {
        var log = new PublishLog(_dir.Path, new FakeClock(T0));
        Action<string> callback = log.AsInfo();

        callback("dal callback");

        Assert.Equal("2026-10-21T21:30:00.123Z [INFO] dal callback", ReadLines(TodayFile).Single());
    }

    [Fact]
    public void Rotation_DeletesFilesOlderThan14Days_KeepsOthers()
    {
        // Oggi e' 2026-10-21: il 2026-10-06 ha 15 giorni (da eliminare), il 2026-10-07 ne ha 14 (da tenere).
        string old = Path.Combine(_dir.Path, "publish-20261006.log");
        string edge = Path.Combine(_dir.Path, "publish-20261007.log");
        string other = Path.Combine(_dir.Path, "altro-20261001.log");
        string wrongPattern = Path.Combine(_dir.Path, "publish-2026.log");
        File.WriteAllText(old, "vecchio");
        File.WriteAllText(edge, "limite");
        File.WriteAllText(other, "non nostro");
        File.WriteAllText(wrongPattern, "non nostro");

        var log = new PublishLog(_dir.Path, new FakeClock(T0));
        log.Info("primo del giorno");

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(edge));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(wrongPattern));
        Assert.True(File.Exists(TodayFile));
    }

    [Fact]
    public void Rotation_UsesClockNotFileTimes()
    {
        string file = Path.Combine(_dir.Path, "publish-20260101.log");
        File.WriteAllText(file, "molto vecchio");

        var clock = new FakeClock(T0);
        var log = new PublishLog(_dir.Path, clock);
        log.Info("oggi");

        Assert.False(File.Exists(file));
    }

    [Fact]
    public void UnwritableFolder_DoesNotThrow()
    {
        // La cartella e' sotto un file normale: non puo' essere creata.
        string blocker = Path.Combine(_dir.Path, "blocco");
        File.WriteAllText(blocker, "file");
        var log = new PublishLog(Path.Combine(blocker, "sotto"), new FakeClock(T0));

        log.Info("non deve esplodere");
        log.Warn("nemmeno questo");
        log.Error("ne' questo", new Exception("x"));
        log.AsInfo()("e nemmeno il callback");

        Assert.True(File.Exists(blocker));
    }

    [Fact]
    public void ThrowingClock_DoesNotThrow()
    {
        var log = new PublishLog(_dir.Path, new ThrowingClock());

        log.Info("test");

        Assert.False(File.Exists(TodayFile));
    }

    private sealed class ThrowingClock : IClock
    {
        public DateTimeOffset UtcNow => throw new InvalidOperationException("orologio guasto");
    }
}
