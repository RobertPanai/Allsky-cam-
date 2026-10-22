using Xunit;
using System.Net;
using AllSky.Nina;

namespace AllSky.Nina.Tests;

public sealed class NinaApiSourceTests
{
    // Nomi di campo e unita' come in NinaFieldNames [NV]: se cambiano dopo P11.13, cambiano qui.
    private const string MontaturaValida =
        "{\"AtPark\":true,\"Slewing\":false,\"TrackingMode\":\"Sidereal\",\"RightAscension\":6.5,\"Declination\":-16.7}";

    private const string SequenzaValida =
        "{\"Name\":\"Root\",\"Items\":[{\"Name\":\"Target 1\",\"TargetName\":\"M42\"," +
        "\"InputCoordinates\":{\"Coordinates\":{\"RA\":5.588,\"Dec\":-5.391}}}]}";

    private static readonly DateTimeOffset Ora = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    private static NinaEndpoint Loopback(int porta = 1888) => new("127.0.0.1", porta);

    private static NinaApiSource Sorgente(NinaScriptedHandler handler, TimeSpan? timeout = null, NinaFakeTime? tempo = null) =>
        new(Loopback(), timeout ?? TimeSpan.FromSeconds(5), handler, tempo ?? new NinaFakeTime { Now = Ora });

    private static NinaScriptedHandler Tutto(NinaScriptedHandler h) => h
        .Risponde(NinaApiSource.MountInfoPath, MontaturaValida)
        .Risponde(NinaApiSource.SequenceJsonPath, SequenzaValida)
        .Risponde(NinaApiSource.SequenceStatePath, "\"Running\"");

    [Fact]
    public async Task RispostaValida_LeggeCampiNoti()
    {
        var handler = Tutto(new NinaScriptedHandler());
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.True(stato.Connected);
        Assert.Null(stato.Error);
        Assert.Equal(Ora, stato.ReadAtUtc);
        Assert.Equal("Running", stato.SequenceState);

        Assert.NotNull(stato.Mount);
        Assert.Equal(true, stato.Mount!.AtPark);
        Assert.Equal(false, stato.Mount.Slewing);
        Assert.Equal("Sidereal", stato.Mount.TrackingMode);
        Assert.Equal(97.5, stato.Mount.RaDeg!.Value, 1e-6);
        Assert.Equal(-16.7, stato.Mount.DecDeg!.Value, 1e-6);

        Assert.NotNull(stato.Target);
        Assert.Equal("M42", stato.Target!.Name);
        Assert.Equal(5.588 * 15.0, stato.Target.RaDeg!.Value, 1e-6);
        Assert.Equal(-5.391, stato.Target.DecDeg!.Value, 1e-6);
    }

    [Fact]
    public async Task CampiMancanti_DiventanoNull()
    {
        var handler = new NinaScriptedHandler()
            .Risponde(NinaApiSource.MountInfoPath, "{}")
            .Risponde(NinaApiSource.SequenceJsonPath, "{\"Items\":[]}")
            .Risponde(NinaApiSource.SequenceStatePath, "{}");
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.True(stato.Connected);
        Assert.Null(stato.Error);
        Assert.NotNull(stato.Mount);
        Assert.Null(stato.Mount!.AtPark);
        Assert.Null(stato.Mount.Slewing);
        Assert.Null(stato.Mount.TrackingMode);
        Assert.Null(stato.Mount.RaDeg);
        Assert.Null(stato.Mount.DecDeg);
        Assert.Null(stato.Target);
        Assert.Null(stato.SequenceState);
    }

    [Fact]
    public async Task TipiSbagliatiEValoriFuoriIntervallo_DiventanoNull()
    {
        var handler = new NinaScriptedHandler()
            .Risponde(NinaApiSource.MountInfoPath, "{\"AtPark\":\"si\",\"Slewing\":1,\"RightAscension\":\"6\",\"Declination\":120,\"TrackingMode\":42}")
            .Risponde(NinaApiSource.SequenceJsonPath, "{\"TargetName\":\"M31\",\"InputCoordinates\":{\"Coordinates\":{\"RA\":30,\"Dec\":\"x\"}}}");
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.NotNull(stato.Mount);
        Assert.Null(stato.Mount!.AtPark);
        Assert.Null(stato.Mount.Slewing);
        Assert.Null(stato.Mount.TrackingMode);
        Assert.Null(stato.Mount.RaDeg);
        Assert.Null(stato.Mount.DecDeg);
        Assert.NotNull(stato.Target);
        Assert.Equal("M31", stato.Target!.Name);
        Assert.Null(stato.Target.RaDeg);
        Assert.Null(stato.Target.DecDeg);
    }

    [Fact]
    public async Task JsonNonValido_DiventaNullEErrore_SenzaCorpo()
    {
        var handler = Tutto(new NinaScriptedHandler())
            .Risponde(NinaApiSource.MountInfoPath, "<html>segreto-nel-corpo</html>");
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.True(stato.Connected);
        Assert.Null(stato.Mount);
        Assert.NotNull(stato.Error);
        Assert.Contains("risposta non JSON", stato.Error!);
        Assert.DoesNotContain("segreto-nel-corpo", stato.Error!);
        Assert.NotNull(stato.Target);
    }

    [Fact]
    public async Task Timeout_SegnalaTimeoutEGliAltriDatiRestano()
    {
        var handler = Tutto(new NinaScriptedHandler()).Blocca(NinaApiSource.MountInfoPath);
        using var src = Sorgente(handler, timeout: TimeSpan.FromMilliseconds(100));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        NinaState stato = await src.ReadAsync(CancellationToken.None);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "la lettura non deve bloccarsi");
        Assert.True(stato.Connected);
        Assert.Null(stato.Mount);
        Assert.Equal("timeout (mount)", stato.Error);
        Assert.NotNull(stato.Target);
        Assert.Equal("Running", stato.SequenceState);
    }

    [Fact]
    public async Task ConnessioneRifiutata_NonConnesso_TuttoNull()
    {
        var handler = new NinaScriptedHandler()
            .Rifiutata(NinaApiSource.MountInfoPath)
            .Rifiutata(NinaApiSource.SequenceJsonPath)
            .Rifiutata(NinaApiSource.SequenceStatePath);
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.False(stato.Connected);
        Assert.Null(stato.Mount);
        Assert.Null(stato.Target);
        Assert.Null(stato.SequenceState);
        Assert.Equal("connessione rifiutata (mount, sequenza, stato)", stato.Error);
    }

    [Fact]
    public async Task ErroreHttp500_RegistratoSenzaCorpo()
    {
        var handler = Tutto(new NinaScriptedHandler())
            .Risponde(NinaApiSource.MountInfoPath, "dettaglio interno", HttpStatusCode.InternalServerError);
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.True(stato.Connected);
        Assert.Null(stato.Mount);
        Assert.Equal("HTTP 500 (mount)", stato.Error);
    }

    [Fact]
    public async Task SequenzaNonInizializzata409_NonEUnErrore()
    {
        var handler = Tutto(new NinaScriptedHandler())
            .Risponde(NinaApiSource.SequenceStatePath, "{\"Error\":\"non inizializzata\"}", HttpStatusCode.Conflict);
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.True(stato.Connected);
        Assert.Null(stato.SequenceState);
        Assert.Null(stato.Error);
        Assert.NotNull(stato.Mount);
    }

    [Fact]
    public async Task EccezioneInattesa_NonSiPropagaENonEsponeIlMessaggio()
    {
        var handler = Tutto(new NinaScriptedHandler())
            .Lancia(NinaApiSource.MountInfoPath, new InvalidOperationException("SEGRETO-123"));
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.Null(stato.Mount);
        Assert.Equal("errore interno (mount)", stato.Error);
        Assert.DoesNotContain("SEGRETO", stato.Error!);
    }

    [Fact]
    public async Task RispostaTroppoGrande_Scartata()
    {
        string enorme = "\"" + new string('a', NinaApiSource.MaxBodyBytes + 1) + "\"";
        var handler = Tutto(new NinaScriptedHandler())
            .Risponde(NinaApiSource.MountInfoPath, enorme);
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.Null(stato.Mount);
        Assert.Equal("risposta troppo grande (mount)", stato.Error);
    }

    [Fact]
    public async Task SoloGetSuTreEndpointAmmessi_NessunaViolazione()
    {
        var handler = Tutto(new NinaScriptedHandler());
        using var src = Sorgente(handler);

        await src.ReadAsync(CancellationToken.None);
        await src.ReadAsync(CancellationToken.None);

        Assert.Empty(handler.Violazioni);
        Assert.Equal(6, handler.Richieste.Count);
        string[] attese =
        {
            "GET http://127.0.0.1:1888/v2/api/equipment/mount/info",
            "GET http://127.0.0.1:1888/v2/api/sequence/json",
            "GET http://127.0.0.1:1888/v2/api/sequence/state",
        };
        foreach (string atteso in attese)
        {
            Assert.Equal(2, handler.Richieste.Count(r => r == atteso));
        }
    }

    [Fact]
    public async Task HostLocalhost_UsaLoBstessoPercorso()
    {
        var handler = Tutto(new NinaScriptedHandler());
        using var src = new NinaApiSource(new NinaEndpoint("localhost", 1999), TimeSpan.FromSeconds(5), handler);

        await src.ReadAsync(CancellationToken.None);

        Assert.Contains("GET http://localhost:1999/v2/api/sequence/json", handler.Richieste);
        Assert.Empty(handler.Violazioni);
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.2")]
    [InlineData("example.com")]
    [InlineData("")]
    public void HostNonAmmesso_ErroreDiCostruzione(string host)
    {
        Assert.ThrowsAny<ArgumentException>(() => { _ = new NinaEndpoint(host, 1888); });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void PortaFuoriIntervallo_ErroreDiCostruzione(int porta)
    {
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => { _ = new NinaEndpoint("127.0.0.1", porta); });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void TimeoutFuoriIntervallo_ErroreDiCostruzione(int secondi)
    {
        var handler = new NinaScriptedHandler();
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
        {
            _ = new NinaApiSource(Loopback(), TimeSpan.FromSeconds(secondi), handler);
        });
    }

    private static string Avvolto(string dati, bool successo = true, int codice = 200) =>
        "{\"Response\":" + dati + ",\"Error\":\"\",\"StatusCode\":" + codice + ",\"Success\":" +
        (successo ? "true" : "false") + ",\"Type\":\"API\"}";

    [Fact]
    public async Task RispostaAvvolta_LeggeIDatiDentroResponse()
    {
        var handler = new NinaScriptedHandler()
            .Risponde(NinaApiSource.MountInfoPath, Avvolto(MontaturaValida))
            .Risponde(NinaApiSource.SequenceJsonPath, Avvolto("[" + SequenzaValida + "]"))
            .Risponde(NinaApiSource.SequenceStatePath, Avvolto("\"Running\""));
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.True(stato.Connected);
        Assert.Null(stato.Error);
        Assert.NotNull(stato.Mount);
        Assert.Equal(true, stato.Mount!.AtPark);
        Assert.Equal(97.5, stato.Mount.RaDeg!.Value, 1e-6);
        Assert.NotNull(stato.Target);
        Assert.Equal("M42", stato.Target!.Name);
        Assert.Equal("Running", stato.SequenceState);
        Assert.Empty(handler.Violazioni);
    }

    [Fact]
    public async Task RispostaAvvoltaConSuccessFalse_NullEErroreBreveSenzaDettagli()
    {
        var handler = Tutto(new NinaScriptedHandler())
            .Risponde(NinaApiSource.MountInfoPath, Avvolto("\"segreto-interno\"", successo: false, codice: 500));
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.True(stato.Connected);
        Assert.Null(stato.Mount);
        Assert.NotNull(stato.Error);
        Assert.Contains("errore NINA", stato.Error!);
        Assert.DoesNotContain("segreto-interno", stato.Error!);
        Assert.NotNull(stato.Target);
    }

    [Fact]
    public async Task RispostaAvvoltaConStatusCode500_EErrore()
    {
        var handler = Tutto(new NinaScriptedHandler())
            .Risponde(NinaApiSource.SequenceJsonPath, Avvolto("null", successo: true, codice: 500));
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.Null(stato.Target);
        Assert.NotNull(stato.Error);
        Assert.Contains("errore NINA", stato.Error!);
    }

    [Fact]
    public async Task SequenzaNonInizializzataAvvolta409_NonEUnErrore()
    {
        var handler = Tutto(new NinaScriptedHandler())
            .Risponde(NinaApiSource.SequenceStatePath, Avvolto("null", successo: false, codice: 409));
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.True(stato.Connected);
        Assert.Null(stato.SequenceState);
        Assert.Null(stato.Error);
    }

    [Fact]
    public async Task RispostaNonAvvolta_ContinuaAEssereLetta()
    {
        var handler = Tutto(new NinaScriptedHandler());
        using var src = Sorgente(handler);

        NinaState stato = await src.ReadAsync(CancellationToken.None);

        Assert.NotNull(stato.Mount);
        Assert.Equal(true, stato.Mount!.AtPark);
        Assert.NotNull(stato.Target);
        Assert.Equal("Running", stato.SequenceState);
        Assert.Null(stato.Error);
    }

    [Fact]
    public async Task Annullamento_PrimaDellaLettura_LanciaOperationCanceled()
    {
        var handler = Tutto(new NinaScriptedHandler());
        using var src = Sorgente(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => src.ReadAsync(cts.Token));
        Assert.Empty(handler.Richieste);
    }

    [Fact]
    public async Task Annullamento_DuranteLaLettura_PulitoEBloccoRilasciato()
    {
        var handler = Tutto(new NinaScriptedHandler()).Blocca(NinaApiSource.SequenceJsonPath);
        using var src = Sorgente(handler, timeout: TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => src.ReadAsync(cts.Token));
    }
}
