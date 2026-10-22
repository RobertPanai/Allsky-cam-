using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AllSky.Archive;
using AllSky.Core;
using AllSky.Desktop.Views.Live;
using AllSky.Engine;
using AllSky.Nina;
using Xunit;

namespace AllSky.Desktop.Tests;

/// <summary>
/// Pannello NINA della vista Live con un servizio fake: senza stato NINA e con uno stato sintetico.
/// Gira solo sul runner Windows, sul thread STA di <see cref="StaRunner"/>.
/// </summary>
[Collection(DesktopCollection.Name)]
public sealed class NinaStatusPanelTests
{
    private readonly DesktopFixture _fixture;

    public NinaStatusPanelTests(DesktopFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void SenzaStatoNina_MostraSoloNinaNonAttivo()
    {
        var service = new FakeNinaService(null);

        List<string> texts = StaRunner.Run(() => TextsOf(new NinaStatusPanel(service, _fixture.Texts)));

        Assert.Equal(new[] { "NINA", "NINA non attivo" }, texts);
    }

    [Fact]
    public void ConStatoSintetico_MostraSoggettoParkEConnessione()
    {
        var service = new FakeNinaService(SyntheticState(connected: true, atPark: false, tracking: "Sidereal"));

        List<string> texts = StaRunner.Run(() => TextsOf(new NinaStatusPanel(service, _fixture.Texts)));

        Assert.Contains("NINA", texts);
        Assert.Contains("Soggetto", texts);
        Assert.Contains("M31 Andromeda", texts);
        Assert.Contains("Park", texts);
        Assert.Contains("in inseguimento", texts);
        Assert.Contains("Connessione", texts);
        Assert.Contains("collegata", texts);
        Assert.DoesNotContain("NINA non attivo", texts);
        Assert.DoesNotContain("non collegata", texts);
    }

    [Fact]
    public void StatoParcheggiatoNonCollegato_MostraNonCollegataSenzaDettagliDiErrore()
    {
        var service = new FakeNinaService(new NinaState(
            Connected: false,
            ReadAtUtc: DateTimeOffset.UtcNow,
            Mount: null,
            Target: null,
            SequenceState: null,
            Error: "timeout su http://192.168.1.20:1888/v2/api/mount"));

        List<string> texts = StaRunner.Run(() => TextsOf(new NinaStatusPanel(service, _fixture.Texts)));

        Assert.Contains("non collegata", texts);
        Assert.Contains("non disponibile", texts);
        Assert.Contains("n/d", texts);
        // Nessun percorso, indirizzo o porta dal campo Error.
        Assert.DoesNotContain(texts, t => t.Contains("192.168", StringComparison.Ordinal) || t.Contains("1888", StringComparison.Ordinal));
    }

    private static NinaState SyntheticState(bool connected, bool atPark, string? tracking) => new(
        Connected: connected,
        ReadAtUtc: DateTimeOffset.UtcNow,
        Mount: new NinaMountState(AtPark: atPark, Slewing: false, TrackingMode: tracking, RaDeg: 10.68, DecDeg: 41.27),
        Target: new NinaTargetState("M31 Andromeda", 10.68, 41.27),
        SequenceState: "Running",
        Error: null);

    /// <summary>Testo di tutti i <see cref="TextBlock"/> discendenti, nell'ordine dell'albero visuale.</summary>
    private static List<string> TextsOf(DependencyObject root)
    {
        var result = new List<string>();
        Collect(root, result);
        return result;
    }

    private static void Collect(DependencyObject node, List<string> result)
    {
        if (node is TextBlock block && !string.IsNullOrEmpty(block.Text))
        {
            result.Add(block.Text);
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            Collect(VisualTreeHelper.GetChild(node, i), result);
        }
    }

    /// <summary>Servizio fake: espone solo <see cref="LatestNinaState"/>; il resto non serve al pannello.</summary>
    private sealed class FakeNinaService : IAllSkyService
    {
        public FakeNinaService(NinaState? state)
        {
            LatestNinaState = state;
        }

        public NinaState? LatestNinaState { get; }

        public StatusDto GetStatus() => throw new NotSupportedException();

        public event Action<StatusDto>? StatusChanged { add { } remove { } }

        public event Action<FrameEvent>? FrameProduced { add { } remove { } }

        public byte[]? GetLatestJpeg(bool raw) => null;

        public Task<IReadOnlyList<CameraCandidate>> DiscoverCamerasAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ConnectResult> ConnectCameraAsync(string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisconnectCameraAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public AppConfig GetConfig() => throw new NotSupportedException();

        public ConfigUpdateResult UpdateConfig(AppConfig config) => throw new NotSupportedException();

        public IReadOnlyList<NightDto> ListNights() => Array.Empty<NightDto>();

        public IReadOnlyList<FrameDto> ListFrames(string nightId, int offset, int limit) => Array.Empty<FrameDto>();

        public string GetFramePath(string nightId, string frameId) => throw new NotSupportedException();

        public string GetThumbPath(string nightId, string frameId) => throw new NotSupportedException();

        public string GetProductPath(string nightId, NightProduct kind) => throw new NotSupportedException();

        public Task<bool> RequestTimelapseAsync(string nightId) => Task.FromResult(false);

        public bool DeleteNight(string nightId) => false;
    }
}
