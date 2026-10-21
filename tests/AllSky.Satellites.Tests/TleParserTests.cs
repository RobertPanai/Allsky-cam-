using AllSky.Satellites;
using Xunit;

namespace AllSky.Satellites.Tests;

public sealed class TleParserTests
{
    private static readonly SatelliteTarget Iss = new(25544, "ISS (ZARYA)");
    private static readonly DateTimeOffset Adesso = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DueRighe_UsaIlNomeDelBersaglio()
    {
        string testo = $"{TleFixtures.Linea1(25544)}\n{TleFixtures.Linea2(25544)}\n";

        bool ok = TleParser.TryParse(testo, Iss, Adesso, out TleSet? set, out string? errore);

        Assert.True(ok);
        Assert.Null(errore);
        Assert.NotNull(set);
        Assert.Equal("ISS (ZARYA)", set!.Name);
    }

    [Fact]
    public void Testo_NonTle_Rifiutato()
    {
        bool ok = TleParser.TryParse("<html>Not Found</html>", Iss, Adesso, out TleSet? set, out string? errore);

        Assert.False(ok);
        Assert.Null(set);
        Assert.NotNull(errore);
    }

    [Fact]
    public void Testo_Vuoto_Rifiutato()
    {
        bool ok = TleParser.TryParse("", Iss, Adesso, out TleSet? set, out _);

        Assert.False(ok);
        Assert.Null(set);
    }

    [Fact]
    public void RigaDueConChecksumErrato_Rifiutata()
    {
        string riga2 = TleFixtures.Linea2(25544);
        string errata = riga2[..68] + ((riga2[68] - '0' + 5) % 10);

        bool ok = TleParser.TryParse($"{TleFixtures.Linea1(25544)}\n{errata}\n", Iss, Adesso, out TleSet? set, out string? errore);

        Assert.False(ok);
        Assert.Null(set);
        Assert.Contains("riga 2", errore!);
    }
}
