using Xunit;
using Xunit.Abstractions;

// Il riepilogo ("Riepilogo_viste") gira per ultimo: legge gli esiti di tutti gli altri casi della classe.
[assembly: TestCaseOrderer("AllSky.Desktop.Tests.SummaryLastOrderer", "AllSky.Desktop.Tests")]

namespace AllSky.Desktop.Tests;

/// <summary>Ordina i casi di test mettendo il riepilogo in fondo; gli altri restano nell'ordine di xunit.</summary>
public sealed class SummaryLastOrderer : ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : ITestCase =>
        testCases.OrderBy(testCase => testCase.DisplayName.Contains(".Riepilogo_", StringComparison.Ordinal) ? 1 : 0);
}
