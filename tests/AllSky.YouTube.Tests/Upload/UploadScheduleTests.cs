using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class UploadScheduleTests
{
    private static readonly TimeOnly Slot = new(12, 30);
    private static readonly TimeSpan Window = TimeSpan.FromHours(12);

    [Fact]
    public void PrimaDellOra_LaProvaEAllOraDiOggi()
    {
        DateTimeOffset now = UploadHarness.Local(2026, 10, 22, 9, 0).ToUniversalTime();

        DateTimeOffset next = UploadSchedule.NextAttemptUtc(now, UploadHarness.Zone, Slot, Window, lastSlotDate: null);

        Assert.Equal(UploadHarness.Local(2026, 10, 22, 12, 30).ToUniversalTime(), next);
    }

    [Fact]
    public void OltreLaFinestra_LaProssimaProvaEDomani()
    {
        // Ore 14:00 con finestra di 1 ora: lo slot delle 12:30 e' scaduto da 1 ora e mezza, quindi si salta a domani.
        DateTimeOffset now = UploadHarness.Local(2026, 10, 22, 14, 0).ToUniversalTime();

        DateTimeOffset next = UploadSchedule.NextAttemptUtc(now, UploadHarness.Zone, Slot, TimeSpan.FromHours(1), lastSlotDate: null);

        Assert.Equal(UploadHarness.Local(2026, 10, 23, 12, 30).ToUniversalTime(), next);
    }

    [Fact]
    public void DopoLOraNellaFinestraDiRecupero_EAdesso()
    {
        DateTimeOffset now = UploadHarness.Local(2026, 10, 22, 18, 0).ToUniversalTime();

        DateTimeOffset next = UploadSchedule.NextAttemptUtc(now, UploadHarness.Zone, Slot, Window, lastSlotDate: null);

        Assert.Equal(now, next);
    }

    [Fact]
    public void GiaElaboratoOggi_LaProssimaEDomani()
    {
        DateTimeOffset now = UploadHarness.Local(2026, 10, 22, 9, 0).ToUniversalTime();

        DateTimeOffset next = UploadSchedule.NextAttemptUtc(now, UploadHarness.Zone, Slot, Window, lastSlotDate: "2026-10-22");

        Assert.Equal(UploadHarness.Local(2026, 10, 23, 12, 30).ToUniversalTime(), next);
    }

    [Fact]
    public void DopoLOra_ConFinestraNulla_NonRecuperaMaiOggi()
    {
        DateTimeOffset now = UploadHarness.Local(2026, 10, 22, 12, 31).ToUniversalTime();

        DateTimeOffset next = UploadSchedule.NextAttemptUtc(now, UploadHarness.Zone, Slot, TimeSpan.Zero, lastSlotDate: null);

        Assert.Equal(UploadHarness.Local(2026, 10, 23, 12, 30).ToUniversalTime(), next);
    }
}
