using System.Net.Http;
using AllSky.Updates;
using Xunit;

namespace AllSky.Updates.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task NotInstalled_IsDisabled_AndMakesNoCalls()
    {
        var backend = new FakeUpdateBackend { IsInstalled = false, NextVersion = "2.0.0" };
        var clock = new FakeUpdateClock();
        using var service = new UpdateService(backend, clock, autoCheck: true);
        service.Start();

        UpdateStatus check = await service.CheckNowAsync();
        UpdateStatus download = await service.DownloadAsync();
        UpdateStatus apply = service.ApplyLater();
        clock.Advance(TimeSpan.FromDays(2));
        await service.TickAsync();

        Assert.False(service.IsInstalled);
        Assert.Equal(UpdateState.Disabled, check.State);
        Assert.Equal(UpdateState.Disabled, download.State);
        Assert.Equal(UpdateState.Disabled, apply.State);
        Assert.Equal(0, backend.CheckCalls);
        Assert.Equal(0, backend.DownloadCalls);
        Assert.Equal(0, backend.ApplyCalls);
    }

    [Fact]
    public async Task Check_WithNewerVersion_IsAvailable_AndDoesNotDownload()
    {
        var backend = new FakeUpdateBackend { NextVersion = "1.3.0" };
        using var service = new UpdateService(backend, new FakeUpdateClock(), autoCheck: true);

        UpdateStatus status = await service.CheckNowAsync();

        Assert.Equal(UpdateState.Available, status.State);
        Assert.Equal("1.3.0", status.AvailableVersion);
        Assert.Equal("1.2.0", status.CurrentVersion);
        Assert.Null(status.ErrorCode);
        Assert.Equal(1, backend.CheckCalls);
        // Il download dopo un controllo manuale lo chiede l'utente: il servizio non scarica da solo.
        Assert.Equal(0, backend.DownloadCalls);
    }

    [Fact]
    public async Task Check_WithoutNewerVersion_IsUpToDate()
    {
        var clock = new FakeUpdateClock();
        var backend = new FakeUpdateBackend { NextVersion = null };
        using var service = new UpdateService(backend, clock, autoCheck: false);

        UpdateStatus status = await service.CheckNowAsync();

        Assert.Equal(UpdateState.UpToDate, status.State);
        Assert.Null(status.AvailableVersion);
        Assert.Equal(clock.UtcNow, status.LastCheckUtc);
    }

    [Fact]
    public async Task NetworkFailure_BecomesNetworkError_WithoutThrowing()
    {
        var backend = new FakeUpdateBackend { CheckFailure = new HttpRequestException("connessione rifiutata") };
        using var service = new UpdateService(backend, new FakeUpdateClock(), autoCheck: true);

        UpdateStatus status = await service.CheckNowAsync();

        Assert.Equal(UpdateState.Error, status.State);
        Assert.Equal(UpdateErrorCodes.Network, status.ErrorCode);
        Assert.NotNull(status.ErrorMessage);
    }

    [Fact]
    public async Task UnknownFailure_BecomesUnknownError()
    {
        var backend = new FakeUpdateBackend { CheckFailure = new InvalidOperationException("stato imprevisto") };
        using var service = new UpdateService(backend, new FakeUpdateClock(), autoCheck: false);

        UpdateStatus status = await service.CheckNowAsync();

        Assert.Equal(UpdateState.Error, status.State);
        Assert.Equal(UpdateErrorCodes.Unknown, status.ErrorCode);
    }

    [Fact]
    public async Task ErrorMessage_DoesNotCarryPathsOrTokens()
    {
        const string secret = "C:\\Users\\robert\\AppData\\token=abc123";
        var backend = new FakeUpdateBackend { CheckFailure = new IOException(secret) };
        using var service = new UpdateService(backend, new FakeUpdateClock(), autoCheck: false);

        UpdateStatus status = await service.CheckNowAsync();

        Assert.Equal(UpdateState.Error, status.State);
        string message = status.ErrorMessage ?? string.Empty;
        Assert.False(message.Contains("C:\\", StringComparison.Ordinal));
        Assert.False(message.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.False(message.Contains("abc123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AutoCheck_RunsAfterStartupDelay_ThenEvery24Hours()
    {
        var clock = new FakeUpdateClock();
        var backend = new FakeUpdateBackend { NextVersion = null };
        using var service = new UpdateService(backend, clock, autoCheck: true);

        await service.TickAsync();
        Assert.Equal(0, backend.CheckCalls);

        clock.Advance(TimeSpan.FromSeconds(59));
        await service.TickAsync();
        Assert.Equal(0, backend.CheckCalls);

        clock.Advance(TimeSpan.FromSeconds(1));
        await service.TickAsync();
        Assert.Equal(1, backend.CheckCalls);

        clock.Advance(TimeSpan.FromHours(23));
        await service.TickAsync();
        Assert.Equal(1, backend.CheckCalls);

        clock.Advance(TimeSpan.FromHours(1));
        await service.TickAsync();
        Assert.Equal(2, backend.CheckCalls);
    }

    [Fact]
    public async Task AutoCheckOff_NoAutomaticCheck_ButManualCheckWorks()
    {
        var clock = new FakeUpdateClock();
        var backend = new FakeUpdateBackend { NextVersion = null };
        using var service = new UpdateService(backend, clock, autoCheck: false);

        clock.Advance(TimeSpan.FromDays(3));
        await service.TickAsync();
        Assert.Equal(0, backend.CheckCalls);

        UpdateStatus status = await service.CheckNowAsync();
        Assert.Equal(1, backend.CheckCalls);
        Assert.Equal(UpdateState.UpToDate, status.State);
    }

    [Fact]
    public async Task AutoCheck_WhenTurnedOff_StopsAutomaticChecks()
    {
        var clock = new FakeUpdateClock();
        var backend = new FakeUpdateBackend { NextVersion = null };
        using var service = new UpdateService(backend, clock, autoCheck: true);

        service.SetAutoCheck(false);
        clock.Advance(TimeSpan.FromDays(2));
        await service.TickAsync();

        Assert.False(service.AutoCheckEnabled);
        Assert.Equal(0, backend.CheckCalls);
    }

    [Fact]
    public async Task AutoCheck_FindsUpdate_DownloadsIt_ButNeverApplies()
    {
        var clock = new FakeUpdateClock();
        var backend = new FakeUpdateBackend { NextVersion = "1.3.0" };
        using var service = new UpdateService(backend, clock, autoCheck: true);

        clock.Advance(StartupDelayPlusOneSecond());
        await service.TickAsync();

        UpdateStatus status = service.GetStatus();
        Assert.Equal(UpdateState.ReadyToInstall, status.State);
        Assert.Equal("1.3.0", status.AvailableVersion);
        Assert.Equal(1, backend.DownloadCalls);
        // Nessuna installazione senza un'azione dell'utente.
        Assert.Equal(0, backend.ApplyCalls);

        // Solo ora, su richiesta dell'utente, l'installazione viene programmata.
        UpdateStatus applied = service.ApplyLater();
        Assert.Equal(UpdateState.ReadyToInstall, applied.State);
        Assert.Equal(1, backend.ApplyCalls);
    }

    [Fact]
    public async Task ApplyLater_WithoutDownloadedUpdate_DoesNothing()
    {
        var backend = new FakeUpdateBackend { NextVersion = "1.3.0" };
        using var service = new UpdateService(backend, new FakeUpdateClock(), autoCheck: false);
        await service.CheckNowAsync();

        UpdateStatus status = service.ApplyLater();

        Assert.Equal(UpdateState.Available, status.State);
        Assert.Equal(0, backend.ApplyCalls);
    }

    [Fact]
    public async Task ManualDownload_ReachesReadyToInstall()
    {
        var backend = new FakeUpdateBackend { NextVersion = "1.3.0" };
        using var service = new UpdateService(backend, new FakeUpdateClock(), autoCheck: false);
        await service.CheckNowAsync();

        UpdateStatus status = await service.DownloadAsync();

        Assert.Equal(UpdateState.ReadyToInstall, status.State);
        Assert.Equal(100, status.ProgressPercent);
        Assert.Equal(1, backend.DownloadCalls);
    }

    [Fact]
    public async Task DownloadFailure_BecomesError_AndNothingIsApplied()
    {
        var backend = new FakeUpdateBackend
        {
            NextVersion = "1.3.0",
            DownloadFailure = new HttpRequestException("interrotto"),
        };
        using var service = new UpdateService(backend, new FakeUpdateClock(), autoCheck: false);
        await service.CheckNowAsync();

        UpdateStatus status = await service.DownloadAsync();

        Assert.Equal(UpdateState.Error, status.State);
        Assert.Equal(UpdateErrorCodes.Network, status.ErrorCode);
        Assert.Equal(0, backend.ApplyCalls);
    }

    [Fact]
    public async Task CheckWhileReadyToInstall_KeepsDownloadedUpdate()
    {
        var backend = new FakeUpdateBackend { NextVersion = "1.3.0" };
        using var service = new UpdateService(backend, new FakeUpdateClock(), autoCheck: false);
        await service.CheckNowAsync();
        await service.DownloadAsync();

        UpdateStatus status = await service.CheckNowAsync();

        Assert.Equal(UpdateState.ReadyToInstall, status.State);
        Assert.Equal(1, backend.CheckCalls);
    }

    [Fact]
    public async Task DownloadWithoutAvailableUpdate_DoesNothing()
    {
        var backend = new FakeUpdateBackend { NextVersion = null };
        using var service = new UpdateService(backend, new FakeUpdateClock(), autoCheck: true);

        UpdateStatus status = await service.DownloadAsync();

        Assert.Equal(UpdateState.Idle, status.State);
        Assert.Equal(0, backend.DownloadCalls);
    }

    private static TimeSpan StartupDelayPlusOneSecond() => UpdateService.StartupDelay + TimeSpan.FromSeconds(1);
}
