using Xunit;
using static AllSky.Studio.Tests.MontageFixtures;

namespace AllSky.Studio.Tests;

public sealed class MontageProjectTests : IDisposable
{
    private readonly TempFolder _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static MontagePlan PlanOk(StudioProject project, IReadOnlyList<StudioFrame> frames)
    {
        MontagePlanOutcome outcome = MontagePlanner.Plan(frames, project.Operations, UniformStats());
        Assert.True(outcome.IsValid, string.Join(" ", outcome.Errors));
        return outcome.Plan!;
    }

    private static StudioProject SampleProject() => new()
    {
        Name = "Montaggio di prova",
        LocalTimeZoneId = "Europe/Rome",
        Operations =
        [
            Op("interval", """{ "from": "2026-10-22T20:00:02Z", "to": "2026-10-22T20:00:59Z" }"""),
            Op("removeFrames", """{ "fileNames": ["frame_0005.jpg"] }"""),
            Op("speed", """{ "factor": 2, "fps": 25 }"""),
            Op("crop", """{ "x": 0, "y": 0, "width": 2, "height": 2 }"""),
            Op("rotate", """{ "degrees": 90 }"""),
            Op("titleCard", """{ "text": "Notte", "durationSeconds": 2, "position": "start" }"""),
            Op("fade", """{ "kind": "in", "durationSeconds": 1 }"""),
        ],
    };

    [Fact]
    public void ProgettoSalvatoERicaricato_StessoPianoStessaImpronta()
    {
        List<StudioFrame> frames = Frames(60);
        MontagePlan original = PlanOk(SampleProject(), frames);

        string path = _tmp.FilePath("montaggio.asbproject");
        StudioProjectStore.Save(path, SampleProject());
        StudioProject loaded = StudioProjectStore.Load(path);
        MontagePlan reloaded = PlanOk(loaded, frames);

        Assert.Equal(original.Fingerprint(), reloaded.Fingerprint());
        Assert.Equal(original.Frames.Count, reloaded.Frames.Count);
        Assert.Equal(original.Summary.DurationSeconds, reloaded.Summary.DurationSeconds);
    }

    [Fact]
    public void CampiSconosciuti_ConservatiENonCambianoIlPiano()
    {
        const string withExtra = """
        {
          "version": 1,
          "name": "Con campi nuovi",
          "futuroProgetto": { "x": [1, 2] },
          "sources": [],
          "operations": [
            { "type": "speed", "parameters": { "factor": 2 }, "nota": "ciao" },
            { "type": "crop", "parameters": { "x": 0, "y": 0, "width": 2, "height": 2, "nuovo": true } }
          ]
        }
        """;
        const string withoutExtra = """
        {
          "version": 1,
          "name": "Con campi nuovi",
          "sources": [],
          "operations": [
            { "type": "speed", "parameters": { "factor": 2 } },
            { "type": "crop", "parameters": { "x": 0, "y": 0, "width": 2, "height": 2 } }
          ]
        }
        """;
        string extraPath = _tmp.FilePath("con-campi.asbproject");
        string plainPath = _tmp.FilePath("senza-campi.asbproject");
        File.WriteAllText(extraPath, withExtra);
        File.WriteAllText(plainPath, withoutExtra);

        StudioProject extra = StudioProjectStore.Load(extraPath);
        StudioProject plain = StudioProjectStore.Load(plainPath);
        List<StudioFrame> frames = Frames(20);

        Assert.Equal(PlanOk(plain, frames).Fingerprint(), PlanOk(extra, frames).Fingerprint());

        string resaved = _tmp.FilePath("riscritto.asbproject");
        StudioProjectStore.Save(resaved, extra);
        StudioProject reloaded = StudioProjectStore.Load(resaved);
        Assert.Equal("ciao", reloaded.Operations[0].UnknownFields["nota"].GetString());
        Assert.True(reloaded.Operations[1].Parameters["nuovo"].GetBoolean());
    }

    [Fact]
    public void ProgettoConTipoSconosciuto_ErroreDelPianoNonDelCaricamento()
    {
        string path = _tmp.FilePath("futuro.asbproject");
        File.WriteAllText(path, """
        {
          "version": 1,
          "name": "Operazione del futuro",
          "operations": [ { "type": "sharpen", "parameters": { "amount": 2 } } ]
        }
        """);

        StudioProject project = StudioProjectStore.Load(path);
        MontagePlanOutcome outcome = MontagePlanner.Plan(Frames(5), project.Operations, UniformStats());

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.Plan);
        Assert.Contains("operazione 1: tipo di operazione non riconosciuto: «sharpen»", Assert.Single(outcome.Errors));
    }
}
