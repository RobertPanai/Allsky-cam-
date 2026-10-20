using Xunit;

namespace AllSky.Cameras.Ascom.Tests;

public class AscomComThreadTests
{
    [Fact]
    public async Task RunAsync_RestituisceIlRisultato()
    {
        using var thread = new AscomComThread(TimeSpan.FromSeconds(5));

        int value = await thread.RunAsync(() => 42, CancellationToken.None);

        Assert.Equal(42, value);
    }

    [Fact]
    public async Task RunAsync_EsegueTuttiILavoriSUnSoloThreadDiversoDalChiamante()
    {
        using var thread = new AscomComThread(TimeSpan.FromSeconds(5));

        int first = await thread.RunAsync(() => Environment.CurrentManagedThreadId, CancellationToken.None);
        int second = await thread.RunAsync(() => Environment.CurrentManagedThreadId, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.NotEqual(Environment.CurrentManagedThreadId, first);
    }

    [Fact]
    public async Task RunAsync_PropagaLeEccezioniDelLavoro()
    {
        using var thread = new AscomComThread(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => thread.RunAsync<int>(() => throw new InvalidOperationException("prova"), CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_Timeout_LanciaMessaggioItalianoEIlLavoroRestaInEsecuzione()
    {
        using var thread = new AscomComThread(TimeSpan.FromSeconds(5));
        using var gate = new ManualResetEventSlim(false);
        try
        {
            TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(
                () => thread.RunAsync(
                    () =>
                    {
                        gate.Wait();
                        return 1;
                    },
                    CancellationToken.None,
                    TimeSpan.FromMilliseconds(50),
                    "prova di blocco"));

            Assert.Contains("il driver non risponde", ex.Message);
            Assert.Contains("prova di blocco", ex.Message);
        }
        finally
        {
            // Sblocca il thread prima del Dispose, che attende la terminazione.
            gate.Set();
        }
    }

    [Fact]
    public async Task RunAsync_AnnullatoPrimaDellAvvio_NonEsegueIlLavoro()
    {
        using var thread = new AscomComThread(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool ran = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => thread.RunAsync(() => ran = true, cts.Token));

        Assert.False(ran);
    }

    [Fact]
    public async Task RunAsync_DopoDispose_LanciaObjectDisposed()
    {
        var thread = new AscomComThread(TimeSpan.FromSeconds(5));
        thread.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => thread.RunAsync(() => 1, CancellationToken.None));
    }

    [Fact]
    public void Costruttore_TimeoutNonPositivo_LanciaErrore()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AscomComThread(TimeSpan.Zero));
    }
}
