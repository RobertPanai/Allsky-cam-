using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace AllSky.Desktop.Tests;

public sealed partial class DesktopUiTests
{
    /// <summary>Tutti i nomi che il riepilogo si aspetta di vedere. È un metodo perché <see cref="ViewNames"/> sta in un altro file parziale.</summary>
    private static string[] ExpectedNames() => [.. ViewNames, MainWindowName, "Risorse tema"];

    /// <summary>
    /// Riepilogo: elenco delle viste superate e fallite, più quelle non ancora eseguite. xunit non garantisce che
    /// questo caso giri per ultimo, quindi il riepilogo riporta i casi raccolti nel momento in cui gira; le viste
    /// mancanti sono indicate come "non ancora eseguite". Non fa fallire il test: gli esiti singoli sono già falliti se serve.
    /// </summary>
    [Fact]
    public void Riepilogo_viste()
    {
        IReadOnlyList<CaseRecord> records = _fixture.Snapshot();
        List<string> names = records.Select(record => record.View).Distinct().ToList();

        var passed = new List<string>();
        var failed = new List<string>();
        foreach (string name in names)
        {
            bool allPassed = records.Where(record => record.View == name).All(record => record.Passed);
            (allPassed ? passed : failed).Add(name);
        }

        List<string> notRun = ExpectedNames().Where(name => !names.Contains(name)).ToList();
        string text = string.Join("\n",
            $"Viste superate ({passed.Count}): {Join(passed)}",
            $"Viste fallite ({failed.Count}): {Join(failed)}",
            $"Viste non ancora eseguite ({notRun.Count}): {Join(notRun)}",
            $"Casi registrati: {records.Count}");

        CiReport.Notice("Riepilogo", text, _output);

        IReadOnlyCollection<string> unhandled = StaRunner.UnhandledErrors;
        if (unhandled.Count > 0)
        {
            CiReport.Error(
                "Eccezioni del thread UI",
                $"{unhandled.Count} eccezioni non gestite durante i test. Prima: {unhandled.First()}",
                _output);
        }

        CiReport.FlushIfRequested(_output);
    }

    private static string Join(IEnumerable<string> items)
    {
        string joined = string.Join(", ", items);
        return joined.Length == 0 ? "nessuna" : joined;
    }
}
