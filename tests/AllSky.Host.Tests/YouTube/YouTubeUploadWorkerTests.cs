using AllSky.Host;
using Xunit;

namespace AllSky.Host.Tests.YouTube;

/// <summary>Ciclo in background con tick finti: nessuna rete, nessun segreto.</summary>
public sealed class YouTubeUploadWorkerTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condizione non raggiunta: " + what);
            }

            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task PartaSubito_PoiAdOgniIntervallo()
    {
        int ticks = 0;
        var worker = new YouTubeUploadWorker(_ => { Interlocked.Increment(ref ticks); return Task.CompletedTask; }, () => true, TimeSpan.FromMilliseconds(20), _ => { });

        worker.Start();
        await WaitUntilAsync(() => Volatile.Read(ref ticks) >= 3, "tre tick");
        await worker.StopAsync();

        Assert.True(Volatile.Read(ref ticks) >= 3);
    }

    [Fact]
    public async Task SenzaAttivazioneOAccount_NessunTick()
    {
        int ticks = 0;
        var worker = new YouTubeUploadWorker(_ => { Interlocked.Increment(ref ticks); return Task.CompletedTask; }, () => false, TimeSpan.FromMilliseconds(10), _ => { });

        worker.Start();
        await Task.Delay(100);
        await worker.StopAsync();

        Assert.Equal(0, Volatile.Read(ref ticks));
    }

    [Fact]
    public async Task ErroreNelTick_NonFermaIlCiclo_E_NonLoggaIlMessaggio()
    {
        int ticks = 0;
        var logged = new List<string>();
        var worker = new YouTubeUploadWorker(
            _ =>
            {
                int n = Interlocked.Increment(ref ticks);
                if (n == 1)
                {
                    throw new InvalidOperationException("SEGRETO-DI-PROVA-123");
                }

                return Task.CompletedTask;
            },
            () => true,
            TimeSpan.FromMilliseconds(20),
            logged.Add);

        worker.Start();
        await WaitUntilAsync(() => Volatile.Read(ref ticks) >= 2, "tick dopo l'errore");
        await worker.StopAsync();

        Assert.NotEmpty(logged);
        Assert.All(logged, line => Assert.DoesNotContain("SEGRETO-DI-PROVA-123", line));
        Assert.Contains(logged, line => line.Contains("InvalidOperationException", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stop_AnnullaIlTickInCorso_ePoiNonRipartono()
    {
        var started = new TaskCompletionSource();
        int ticks = 0;
        var worker = new YouTubeUploadWorker(
            async ct =>
            {
                Interlocked.Increment(ref ticks);
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            },
            () => true,
            TimeSpan.FromMilliseconds(10),
            _ => { });

        worker.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task stop = worker.StopAsync();
        Assert.True(await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5))) == stop, "StopAsync deve completare");
        await stop;

        int afterStop = Volatile.Read(ref ticks);
        await Task.Delay(60);
        Assert.Equal(afterStop, Volatile.Read(ref ticks));
    }

    [Fact]
    public async Task StopSenzaStart_EIdempotente()
    {
        var worker = new YouTubeUploadWorker(_ => Task.CompletedTask, () => true, TimeSpan.FromMinutes(10), _ => { });

        await worker.StopAsync();
        await worker.StopAsync();
    }
}
