using AllSky.Updates;

namespace AllSky.Updates.Tests;

/// <summary>Backend finto: conta le chiamate e può simulare errori.</summary>
internal sealed class FakeUpdateBackend : IUpdateBackend
{
    public bool IsInstalled { get; set; } = true;

    public string? CurrentVersion { get; set; } = "1.2.0";

    /// <summary>Versione restituita dal controllo; null se l'app è aggiornata.</summary>
    public string? NextVersion { get; set; }

    /// <summary>Se valorizzata, il controllo lancia questa eccezione.</summary>
    public Exception? CheckFailure { get; set; }

    /// <summary>Se valorizzata, il download lancia questa eccezione.</summary>
    public Exception? DownloadFailure { get; set; }

    public int CheckCalls { get; private set; }

    public int DownloadCalls { get; private set; }

    public int ApplyCalls { get; private set; }

    public Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        CheckCalls++;
        if (CheckFailure is not null)
        {
            return Task.FromException<string?>(CheckFailure);
        }

        return Task.FromResult(NextVersion);
    }

    public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        DownloadCalls++;
        if (DownloadFailure is not null)
        {
            throw DownloadFailure;
        }

        progress.Report(50);
        await Task.Yield();
        progress.Report(100);
    }

    public void ApplyAndRestartLater()
    {
        ApplyCalls++;
    }
}

/// <summary>Orologio finto: il tempo avanza solo con <see cref="Advance"/>.</summary>
internal sealed class FakeUpdateClock : IUpdateClock
{
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan delta)
    {
        UtcNow += delta;
    }
}
