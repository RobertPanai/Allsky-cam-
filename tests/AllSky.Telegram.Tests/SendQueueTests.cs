using AllSky.Telegram;
using Xunit;

namespace AllSky.Telegram.Tests;

public sealed class SendQueueTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private static TelegramResult Ok() => TelegramResult.Success("ok");

    private static TelegramResult Fail(TelegramErrorKind kind, int? retryAfter = null)
        => TelegramResult.Failure(kind, "esito di prova", retryAfter);

    /// <summary>Operazione che registra l'istante di inizio di ogni tentativo e restituisce gli esiti in sequenza (l'ultimo si ripete).</summary>
    private static (Func<CancellationToken, Task<TelegramResult>> Op, List<DateTimeOffset> Starts, List<int> Calls) Scripted(
        FakeClock clock, params TelegramResult[] outcomes)
    {
        var starts = new List<DateTimeOffset>();
        var calls = new List<int>();
        int next = 0;
        Func<CancellationToken, Task<TelegramResult>> op = _ =>
        {
            starts.Add(clock.UtcNow);
            calls.Add(next);
            TelegramResult r = outcomes[Math.Min(next, outcomes.Length - 1)];
            next++;
            return Task.FromResult(r);
        };
        return (op, starts, calls);
    }

    [Fact]
    public async Task UnMessaggioAlSecondo_PerChat()
    {
        var rig = new TelegramRig();
        DateTimeOffset t0 = rig.Clock.UtcNow;
        var (op, starts, _) = Scripted(rig.Clock, Ok());

        await rig.Queue.EnqueueAsync(5, op);
        await rig.Queue.EnqueueAsync(5, op);
        await rig.Queue.EnqueueAsync(5, op);

        Assert.Equal(
            new[] { t0, t0 + OneSecond, t0 + OneSecond + OneSecond },
            starts.ToArray());
        Assert.Equal(new[] { OneSecond, OneSecond }, rig.Delay.Delays.ToArray());
    }

    [Fact]
    public async Task ChatIndipendenti_NonSiRallentanoAVicenda()
    {
        var rig = new TelegramRig();
        var (op, _, _) = Scripted(rig.Clock, Ok());

        await rig.Queue.EnqueueAsync(1, op);
        await rig.Queue.EnqueueAsync(2, op);

        Assert.Empty(rig.Delay.Delays);

        await rig.Queue.EnqueueAsync(1, op);
        Assert.Equal(new[] { OneSecond }, rig.Delay.Delays.ToArray());
    }

    [Fact]
    public async Task LimiteGiornaliero_ScartaEConta()
    {
        var rig = new TelegramRig(dailyLimit: 2);
        var (op, _, calls) = Scripted(rig.Clock, Ok());

        TelegramResult first = await rig.Queue.EnqueueAsync(1, op);
        TelegramResult second = await rig.Queue.EnqueueAsync(1, op);
        TelegramResult third = await rig.Queue.EnqueueAsync(1, op);

        Assert.True(first.Ok);
        Assert.True(second.Ok);
        Assert.False(third.Ok);
        Assert.Equal(TelegramErrorKind.RateLimited, third.ErrorKind);
        Assert.Equal(2, calls.Count);
        Assert.Equal(2, rig.Queue.AdmittedToday);
        Assert.Equal(1, rig.Queue.DiscardedToday);
    }

    [Fact]
    public async Task LimiteGiornaliero_AvvisoUnicoAlGiorno()
    {
        var rig = new TelegramRig(dailyLimit: 1);
        var (op, _, _) = Scripted(rig.Clock, Ok());

        await rig.Queue.EnqueueAsync(1, op);
        for (int i = 0; i < 4; i++)
            await rig.Queue.EnqueueAsync(1, op);

        Assert.Equal(4, rig.Queue.DiscardedToday);
        Assert.Equal(1, rig.LogLines.Count(l => l.StartsWith("[AVVISO] Limite giornaliero", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LimiteGiornaliero_NuovoGiornoRiazzeraEAvvisa()
    {
        var rig = new TelegramRig(dailyLimit: 1);
        var (op, _, calls) = Scripted(rig.Clock, Ok());

        await rig.Queue.EnqueueAsync(1, op);
        await rig.Queue.EnqueueAsync(1, op);
        Assert.Equal(1, rig.Queue.DiscardedToday);

        rig.Clock.Advance(TimeSpan.FromDays(1));
        TelegramResult next = await rig.Queue.EnqueueAsync(1, op);

        Assert.True(next.Ok);
        Assert.Equal(0, rig.Queue.DiscardedToday);
        Assert.Equal(1, rig.Queue.AdmittedToday);
        Assert.Equal(2, calls.Count);

        await rig.Queue.EnqueueAsync(1, op);
        Assert.Equal(2, rig.LogLines.Count(l => l.StartsWith("[AVVISO] Limite giornaliero", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Retry429_ConRetryAfter3_AspettaTreSecondi()
    {
        var rig = new TelegramRig();
        DateTimeOffset t0 = rig.Clock.UtcNow;
        var (op, starts, _) = Scripted(rig.Clock, Fail(TelegramErrorKind.RateLimited, 3), Ok());

        TelegramResult r = await rig.Queue.EnqueueAsync(1, op);

        Assert.True(r.Ok);
        Assert.Equal(new[] { t0, t0 + TimeSpan.FromSeconds(3) }, starts.ToArray());
        Assert.Equal(new[] { TimeSpan.FromSeconds(3) }, rig.Delay.Delays.ToArray());
    }

    [Fact]
    public async Task Retry429_SenzaRetryAfter_BackoffUnoDueQuattro()
    {
        var rig = new TelegramRig();
        var (op, _, calls) = Scripted(rig.Clock,
            Fail(TelegramErrorKind.RateLimited), Fail(TelegramErrorKind.RateLimited),
            Fail(TelegramErrorKind.RateLimited), Ok());

        TelegramResult r = await rig.Queue.EnqueueAsync(1, op);

        Assert.True(r.Ok);
        Assert.Equal(4, calls.Count);
        Assert.Equal(
            new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) },
            rig.Delay.Delays.ToArray());
    }

    [Theory]
    [InlineData("RateLimited")]
    [InlineData("Network")]
    public async Task CinqueTentativiPoiErrore_BackoffFinoAOtto(string kind)
    {
        var rig = new TelegramRig();
        var failure = Fail(Enum.Parse<TelegramErrorKind>(kind));
        var (op, _, calls) = Scripted(rig.Clock, failure);

        TelegramResult r = await rig.Queue.EnqueueAsync(1, op);

        Assert.False(r.Ok);
        Assert.Equal(Enum.Parse<TelegramErrorKind>(kind), r.ErrorKind);
        Assert.Equal(SendQueue.MaxAttempts, calls.Count);
        Assert.Equal(
            new[] { 1, 2, 4, 8 }.Select(s => TimeSpan.FromSeconds(s)).ToArray(),
            rig.Delay.Delays.ToArray());
    }

    [Fact]
    public async Task ErroreDiRete_PoiOk_RiprovaDopoUnSecondo()
    {
        var rig = new TelegramRig();
        var (op, _, calls) = Scripted(rig.Clock, Fail(TelegramErrorKind.Network), Ok());

        TelegramResult r = await rig.Queue.EnqueueAsync(1, op);

        Assert.True(r.Ok);
        Assert.Equal(2, calls.Count);
        Assert.Equal(new[] { OneSecond }, rig.Delay.Delays.ToArray());
    }

    [Theory]
    [InlineData("Unauthorized")]
    [InlineData("Rejected")]
    [InlineData("TooLarge")]
    [InlineData("InvalidInput")]
    public async Task ErroriPermanenti_NonRitentati(string kind)
    {
        var rig = new TelegramRig();
        var (op, _, calls) = Scripted(rig.Clock, Fail(Enum.Parse<TelegramErrorKind>(kind)), Ok());

        TelegramResult r = await rig.Queue.EnqueueAsync(1, op);

        Assert.Equal(Enum.Parse<TelegramErrorKind>(kind), r.ErrorKind);
        Assert.Single(calls);
        Assert.Empty(rig.Delay.Delays);
    }

    [Fact]
    public async Task OperazioneCheLancia_DiventaNetwork_EPoiRiprova()
    {
        var rig = new TelegramRig();
        int calls = 0;
        Func<CancellationToken, Task<TelegramResult>> op = _ =>
        {
            calls++;
            if (calls == 1)
                throw new InvalidOperationException("dettaglio interno");
            return Task.FromResult(Ok());
        };

        TelegramResult r = await rig.Queue.EnqueueAsync(1, op);

        Assert.True(r.Ok);
        Assert.Equal(2, calls);
        Assert.DoesNotContain("dettaglio interno", string.Join("\n", rig.LogLines));
    }

    [Fact]
    public async Task AnnullamentoPrimaDellaCoda_NessunaEsecuzioneNeSConto()
    {
        var rig = new TelegramRig(dailyLimit: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (op, _, calls) = Scripted(rig.Clock, Ok());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Queue.EnqueueAsync(1, op, cts.Token));

        Assert.Empty(calls);
        Assert.Equal(0, rig.Queue.AdmittedToday);
    }

    [Fact]
    public void LimiteGiornalieroNonValido_Rifiutato()
    {
        var clock = new FakeClock();
        var delay = new FakeDelay(clock);
        var log = new TelegramLog(_ => { }, new AllSky.Publish.Redactor(() => Array.Empty<string>()));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = new SendQueue(clock, delay.Run, log, dailyLimit: 0);
        });
    }
}
