using AllSky.Core.LogView;
using Xunit;

namespace AllSky.Core.Tests.LogView;

public sealed class LogParserTests
{
    [Fact]
    public void Parse_RiconosceFormatoDelDailyLog_ERaggruppaLeTracce()
    {
        string[] lines =
        [
            "2026-10-22 10:00:00  Avvio di AllSky Host.",
            "2026-10-22 10:00:05  Errore durante la cattura",
            "System.InvalidOperationException: boom",
            "   at AllSky.Engine.Foo()",
            "2026-10-22 10:01:00  Configurazione predefinita creata.",
        ];

        IReadOnlyList<LogRecord> records = LogParser.Parse(lines);

        Assert.Equal(3, records.Count);
        Assert.Equal(LogLevel.Info, records[0].Level);
        Assert.Equal(LogLevel.Error, records[1].Level);
        Assert.True(records[1].Text.Contains("   at AllSky.Engine.Foo()", StringComparison.Ordinal));
        Assert.Equal(new DateTime(2026, 10, 22, 10, 1, 0), records[2].Timestamp);
    }

    [Fact]
    public void Parse_RiconosceFormatoAppLog()
    {
        IReadOnlyList<LogRecord> records = LogParser.Parse(["[2026-10-21 09:15:00] Controllo degli aggiornamenti non riuscito: HttpRequestException"]);

        Assert.Single(records);
        Assert.Equal(LogLevel.Error, records[0].Level);
        Assert.Equal(new DateTime(2026, 10, 21, 9, 15, 0), records[0].Timestamp);
        Assert.False(records[0].Text.Contains("[", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MascheraISegretiNelTesto()
    {
        IReadOnlyList<LogRecord> records = LogParser.Parse(["2026-10-22 10:00:00  Richiesta con token=supersegreto"]);

        Assert.False(records[0].Text.Contains("supersegreto", StringComparison.Ordinal));
        Assert.True(records[0].Text.Contains("token=***", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_RigheInizialiSenzaData_SonoUnaVoceSenzaIstante()
    {
        IReadOnlyList<LogRecord> records = LogParser.Parse(["   at Qualcosa()", "2026-10-22 10:00:00  Ok"]);

        Assert.Equal(2, records.Count);
        Assert.Null(records[0].Timestamp);
        Assert.Equal(LogLevel.Info, records[0].Level);
    }

    [Fact]
    public void Parse_RigheVuoteInCoda_NonFannoParteDelTesto()
    {
        IReadOnlyList<LogRecord> records = LogParser.Parse(["2026-10-22 10:00:00  Ok", "", ""]);

        Assert.Equal("Ok", records[0].Text);
    }

    [Theory]
    [InlineData("Errore durante la cattura", LogLevel.Error)]
    [InlineData("System.Exception: x", LogLevel.Error)]
    [InlineData("Salvataggio non riuscito", LogLevel.Error)]
    [InlineData("ATTENZIONE: accesso dalla rete locale attivo", LogLevel.Warning)]
    [InlineData("Porta 5810 occupata: provo la successiva.", LogLevel.Warning)]
    [InlineData("Ultimo frame salvato", LogLevel.Info)]
    [InlineData("Errore nella traccia ma Attenzione alla rete", LogLevel.Error)]
    public void Classify_StimaLaGravita(string text, LogLevel expected)
    {
        Assert.Equal(expected, LogParser.Classify(text));
    }

    [Fact]
    public void Filter_PerGravitaETesto_TieneLeUltimeVoci()
    {
        IReadOnlyList<LogRecord> records = LogParser.Parse(
        [
            "2026-10-22 10:00:00  Avvio",
            "2026-10-22 10:00:01  ATTENZIONE: porta occupata",
            "2026-10-22 10:00:02  Errore: frame perso",
            "2026-10-22 10:00:03  Frame salvato",
            "2026-10-22 10:00:04  Errore: camera",
        ]);

        IReadOnlyList<LogRecord> warnings = LogFilter.Apply(records, LogLevelFilter.WarningsAndErrors, null, 100);
        Assert.Equal(3, warnings.Count);

        IReadOnlyList<LogRecord> errors = LogFilter.Apply(records, LogLevelFilter.ErrorsOnly, "CAMERA", 100);
        Assert.Single(errors);
        Assert.True(errors[0].Text.Contains("camera", StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<LogRecord> lastTwo = LogFilter.Apply(records, LogLevelFilter.All, "   ", 2);
        Assert.Equal(2, lastTwo.Count);
        Assert.Equal("Frame salvato", lastTwo[0].Text);
        Assert.Equal("Errore: camera", lastTwo[1].Text);
    }

    [Fact]
    public void Filter_LimiteNonPositivo_Lancia()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogFilter.Apply(Array.Empty<LogRecord>(), LogLevelFilter.All, null, 0));
    }

    [Fact]
    public void Passes_Tabella()
    {
        Assert.True(LogFilter.Passes(LogLevel.Info, LogLevelFilter.All));
        Assert.False(LogFilter.Passes(LogLevel.Info, LogLevelFilter.WarningsAndErrors));
        Assert.True(LogFilter.Passes(LogLevel.Warning, LogLevelFilter.WarningsAndErrors));
        Assert.False(LogFilter.Passes(LogLevel.Warning, LogLevelFilter.ErrorsOnly));
        Assert.True(LogFilter.Passes(LogLevel.Error, LogLevelFilter.ErrorsOnly));
    }
}
