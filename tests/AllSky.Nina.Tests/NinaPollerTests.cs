using Xunit;
using AllSky.Nina;

namespace AllSky.Nina.Tests;

public sealed class NinaPollerTests
{
    private static readonly DateTimeOffset Ora = new(2026, 10, 22, 20, 0, 0, TimeSpan.Zero);

    private static NinaState Connesso() => new(true, Ora, null, null, null, null);

    private static NinaState NonConnesso() => NinaState.Offline(Ora, "connessione rifiutata (mount)");

    private static string Secondi(IEnumerable<TimeSpan> valori) =>
        string.Join(",", valori.Select(t => t.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Backoff_CresceESiLimita()
    {
        TimeSpan max = TimeSpan.FromSeconds(60);
        Assert.Equal(5.0, NinaBackoff.Delay(1, max).TotalSeconds, 1e-9);
        Assert.Equal(10.0, NinaBackoff.Delay(2, max).TotalSeconds, 1e-9);
        Assert.Equal(20.0, NinaBackoff.Delay(3, max).TotalSeconds, 1e-9);
        Assert.Equal(40.0, NinaBackoff.Delay(4, max).TotalSeconds, 1e-9);
        Assert.Equal(60.0, NinaBackoff.Delay(5, max).TotalSeconds, 1e-9);
        Assert.Equal(60.0, NinaBackoff.Delay(6, max).TotalSeconds, 1e-9);
        Assert.Equal(60.0, NinaBackoff.Delay(1000, max).TotalSeconds, 1e-9);
    }

    [Fact]
    public void Backoff_NonScendeSottoCinqueSecondi_NeSalePerMassimoBassi()
    {
        Assert.Equal(5.0, NinaBackoff.Delay(1, TimeSpan.FromSeconds(1)).TotalSeconds, 1e-9);
        Assert.Equal(5.0, NinaBackoff.Delay(4, TimeSpan.FromSeconds(5)).TotalSeconds, 1e-9);
        Assert.Equal(5.0, NinaBackoff.Delay(0, TimeSpan.FromSeconds(600)).TotalSeconds, 1e-9);
        Assert.Equal(600.0, NinaBackoff.Delay(100, TimeSpan.FromSeconds(600)).TotalSeconds, 1e-9);
    }

    [Fact]
    public async Task LetturePersiste_AttesaCresceEPoiSiLimita()
    {
        var source = new NinaScriptedSource(_ => NonConnesso());
        var attese = new List<TimeSpan>();
        NinaPoller? poller = null;
        poller = new NinaPoller(source, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60), delay: (t, _) =>
        {
            attese.Add(t);
            if (attese.Count == 6)
            {
                poller!.Dispose();
            }

            return Task.CompletedTask;
        });

        poller.Start();
        await poller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("5,10,20,40,60,60", Secondi(attese));
        Assert.Equal(6, source.Letture);
    }

    [Fact]
    public async Task LetturaRiuscita_AzzeraIlBackoffEUsaLIntervallo()
    {
        var stati = new[] { NonConnesso(), NonConnesso(), Connesso(), NonConnesso() };
        var source = new NinaScriptedSource(n => n <= stati.Length ? stati[n - 1] : Connesso());
        var attese = new List<TimeSpan>();
        NinaPoller? poller = null;
        poller = new NinaPoller(source, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60), delay: (t, _) =>
        {
            attese.Add(t);
            if (attese.Count == 4)
            {
                poller!.Dispose();
            }

            return Task.CompletedTask;
        });

        poller.Start();
        await poller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("5,10,2,5", Secondi(attese));
    }

    [Fact]
    public async Task PubblicaUltimoStatoEEvento()
    {
        var source = new NinaScriptedSource(_ => Connesso());
        var ricevuti = new List<NinaState>();
        NinaPoller? poller = null;
        poller = new NinaPoller(source, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60), delay: (_, _) =>
        {
            if (source.Letture == 3)
            {
                poller!.Dispose();
            }

            return Task.CompletedTask;
        });
        poller.StateChanged += (_, s) => ricevuti.Add(s);

        Assert.Null(poller.Latest);
        poller.Start();
        await poller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, ricevuti.Count);
        Assert.NotNull(poller.Latest);
        Assert.Same(ricevuti[^1], poller.Latest);
    }

    [Fact]
    public async Task EccezioneDellaSorgente_DiventaStatoSenzaDettagli_IlCicloContinua()
    {
        var source = new NinaScriptedSource(n =>
        {
            if (n == 1)
            {
                throw new InvalidOperationException("SEGRETO-456");
            }

            return Connesso();
        });
        var ricevuti = new List<NinaState>();
        NinaPoller? poller = null;
        poller = new NinaPoller(source, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60), delay: (_, _) =>
        {
            if (source.Letture == 2)
            {
                poller!.Dispose();
            }

            return Task.CompletedTask;
        });
        poller.StateChanged += (_, s) => ricevuti.Add(s);

        poller.Start();
        await poller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, source.Letture);
        Assert.False(ricevuti[0].Connected);
        Assert.Equal("errore interno", ricevuti[0].Error);
        Assert.DoesNotContain("SEGRETO", ricevuti[0].Error!);
        Assert.True(ricevuti[1].Connected);
    }

    [Fact]
    public async Task UnGestoreDifettoso_NonFermaIlCiclo()
    {
        var source = new NinaScriptedSource(_ => Connesso());
        NinaPoller? poller = null;
        poller = new NinaPoller(source, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60), delay: (_, _) =>
        {
            if (source.Letture == 2)
            {
                poller!.Dispose();
            }

            return Task.CompletedTask;
        });
        poller.StateChanged += (_, _) => throw new InvalidOperationException("gestore difettoso");

        poller.Start();
        await poller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, source.Letture);
    }

    [Fact]
    public async Task Dispose_FermaIlCicloSenzaBloccare()
    {
        var primaLettura = new TaskCompletionSource();
        var source = new NinaScriptedSource(_ =>
        {
            primaLettura.TrySetResult();
            return Connesso();
        });
        using var poller = new NinaPoller(source, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60));

        poller.Start();
        await primaLettura.Task.WaitAsync(TimeSpan.FromSeconds(5));
        int letturePrimaDelloStop = source.Letture;

        poller.Dispose();
        await poller.Completion.WaitAsync(TimeSpan.FromSeconds(3));

        await Task.Delay(100);
        Assert.Equal(letturePrimaDelloStop, source.Letture);
    }

    [Fact]
    public void IntervalloFuoriIntervallo_ErroreDiCostruzione()
    {
        var source = new NinaScriptedSource(_ => Connesso());
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
        {
            _ = new NinaPoller(source, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        });
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
        {
            _ = new NinaPoller(source, TimeSpan.FromSeconds(301), TimeSpan.FromSeconds(60));
        });
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
        {
            _ = new NinaPoller(source, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(4));
        });
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
        {
            _ = new NinaPoller(source, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(601));
        });
    }

    [Fact]
    public void DopoDispose_StartLanciaObjectDisposed()
    {
        var source = new NinaScriptedSource(_ => Connesso());
        var poller = new NinaPoller(source, NinaPoller.DefaultInterval, TimeSpan.FromSeconds(60));
        poller.Dispose();

        Assert.ThrowsAny<ObjectDisposedException>(() => poller.Start());
    }
}
