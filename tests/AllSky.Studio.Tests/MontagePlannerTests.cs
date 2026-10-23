using System.Globalization;
using Xunit;
using static AllSky.Studio.Tests.MontageFixtures;

namespace AllSky.Studio.Tests;

public sealed class MontagePlannerTests
{
    private static MontagePlan PlanOk(
        IReadOnlyList<StudioFrame> frames,
        Func<StudioFrame, FrameStats> stats,
        params StudioOperation[] operations)
    {
        MontagePlanOutcome outcome = MontagePlanner.Plan(frames, operations, stats);
        Assert.True(outcome.IsValid, string.Join(" ", outcome.Errors));
        return outcome.Plan!;
    }

    private static string WarningsOf(MontagePlan plan) => string.Join("\n", plan.Warnings);

    /// <summary>Indice del frame dal nome "frame_NNNN.jpg".</summary>
    private static int IndexOf(StudioFrame frame) => int.Parse(frame.FileName.Substring(6, 4), CultureInfo.InvariantCulture);

    [Fact]
    public void Velocita_CentoFrameFattoreQuattro_VenticinqueFrame()
    {
        MontagePlan plan = PlanOk(Frames(100), NoStatsExpected(), Op("speed", """{ "factor": 4 }"""));

        Assert.Equal(25, plan.Frames.Count);
        Assert.Equal(25, plan.Fps);
        Assert.Equal(1.0, plan.Summary.DurationSeconds);
        Assert.Equal("frame_0000.jpg", plan.Frames[0].FileName);
        Assert.Equal("frame_0004.jpg", plan.Frames[1].FileName);
        Assert.Equal("frame_0096.jpg", plan.Frames[24].FileName);
        Assert.Equal(75, plan.Summary.RemovedByReason[RemovalReason.SpeedSkipped]);
        Assert.Equal(0, plan.Summary.RepeatedFrames);
    }

    [Fact]
    public void Velocita_FattoreMezzo_RipeteOgniFrameDueVolte()
    {
        MontagePlan plan = PlanOk(Frames(100), NoStatsExpected(), Op("speed", """{ "factor": 0.5 }"""));

        Assert.Equal(200, plan.Frames.Count);
        Assert.Equal("frame_0000.jpg", plan.Frames[0].FileName);
        Assert.Equal("frame_0000.jpg", plan.Frames[1].FileName);
        Assert.Equal("frame_0001.jpg", plan.Frames[2].FileName);
        Assert.Equal(100, plan.Summary.RepeatedFrames);
        Assert.Equal(0, plan.Summary.RemovedByReason[RemovalReason.SpeedSkipped]);
        Assert.Equal(8.0, plan.Summary.DurationSeconds);
    }

    [Fact]
    public void Velocita_FpsDueDieci_DurataCinqueSecondi()
    {
        MontagePlan plan = PlanOk(Frames(100), NoStatsExpected(), Op("speed", """{ "factor": 2, "fps": 10 }"""));

        Assert.Equal(50, plan.Frames.Count);
        Assert.Equal(10, plan.Fps);
        Assert.Equal(5.0, plan.Summary.DurationSeconds);
    }

    [Fact]
    public void VenticinqueFrameVenticinqueFps_DurataUnSecondo()
    {
        MontagePlan plan = PlanOk(Frames(25), NoStatsExpected(), Op("speed", """{ "factor": 1, "fps": 25 }"""));

        Assert.Equal(25, plan.Frames.Count);
        Assert.Equal(1.0, plan.Summary.DurationSeconds);
    }

    [Fact]
    public void SenzaVelocita_FpsDiDefaultVenticinque_DurataDalNumeroDiFrame()
    {
        MontagePlan plan = PlanOk(Frames(100), NoStatsExpected());

        Assert.Equal(25, plan.Fps);
        Assert.Equal(4.0, plan.Summary.DurationSeconds);
        Assert.Empty(plan.Segments);
        Assert.Empty(plan.PixelOps);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void SchedaTitoloDueSecondi_VenticinqueFps_CinquantaFrame()
    {
        MontagePlan plan = PlanOk(
            Frames(100),
            NoStatsExpected(),
            Op("titleCard", """{ "text": "Notte", "durationSeconds": 2, "position": "start" }"""));

        MontageSegment segment = Assert.Single(plan.Segments);
        Assert.Equal(MontageSegmentKind.TitleCardAtStart, segment.Kind);
        Assert.Equal(50, segment.DurationFrames);
        Assert.Equal("Notte", segment.Text);
        Assert.Equal(50, plan.Summary.TitleCardFrames);
        Assert.Equal(6.0, plan.Summary.DurationSeconds);
    }

    [Fact]
    public void SchedaTitoloInFine_TipoAlleFine()
    {
        MontagePlan plan = PlanOk(
            Frames(10),
            NoStatsExpected(),
            Op("titleCard", """{ "text": "Fine", "durationSeconds": 1, "position": "end" }"""));

        Assert.Equal(MontageSegmentKind.TitleCardAtEnd, Assert.Single(plan.Segments).Kind);
    }

    [Fact]
    public void DissolvenzaInMezzoSecondo_TredicifFrame_NonAllungaIlMontaggio()
    {
        MontagePlan plan = PlanOk(
            Frames(100),
            NoStatsExpected(),
            Op("fade", """{ "kind": "in", "durationSeconds": 0.5 }"""));

        MontageSegment segment = Assert.Single(plan.Segments);
        Assert.Equal(MontageSegmentKind.FadeIn, segment.Kind);
        Assert.Equal(13, segment.DurationFrames);
        Assert.Null(segment.Text);
        Assert.Equal(0, plan.Summary.TitleCardFrames);
        Assert.Equal(4.0, plan.Summary.DurationSeconds);
    }

    [Fact]
    public void DissolvenzaIncrociata_SenzaSchedeTitolo_AvvisoENessunSegmento()
    {
        MontagePlan plan = PlanOk(
            Frames(100),
            NoStatsExpected(),
            Op("fade", """{ "kind": "crossfade", "durationSeconds": 0.5 }"""));

        Assert.Empty(plan.Segments);
        Assert.Contains("senza schede titolo", WarningsOf(plan));
    }

    [Fact]
    public void DissolvenzaIncrociata_ConSchedaTitolo_SegmentoSulBordo()
    {
        MontagePlan plan = PlanOk(
            Frames(100),
            NoStatsExpected(),
            Op("titleCard", """{ "text": "Notte", "durationSeconds": 1, "position": "start" }"""),
            Op("fade", """{ "kind": "crossfade", "durationSeconds": 0.5 }"""));

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(MontageSegmentKind.TitleCardAtStart, plan.Segments[0].Kind);
        Assert.Equal(25, plan.Segments[0].DurationFrames);
        Assert.Equal(MontageSegmentKind.Crossfade, plan.Segments[1].Kind);
        Assert.Equal(13, plan.Segments[1].DurationFrames);
    }

    [Fact]
    public void Intervallo_EstremiInclusivi_DieciFrame()
    {
        MontagePlan plan = PlanOk(
            Frames(100),
            NoStatsExpected(),
            Op("interval", """{ "from": "2026-10-22T20:00:10Z", "to": "2026-10-22T20:00:19Z" }"""));

        Assert.Equal(10, plan.Frames.Count);
        Assert.Equal("frame_0010.jpg", plan.Frames[0].FileName);
        Assert.Equal("frame_0019.jpg", plan.Frames[9].FileName);
        Assert.Equal(90, plan.Summary.RemovedByReason[RemovalReason.Interval]);
    }

    [Fact]
    public void RimozioneManuale_IgnoraMaiuscoleEAvvisaNomiMancanti()
    {
        MontagePlan plan = PlanOk(
            Frames(10),
            NoStatsExpected(),
            Op("removeFrames", """{ "fileNames": ["FRAME_0003.JPG", "frame_0004.jpg", "non_c_e.jpg"] }"""));

        Assert.Equal(8, plan.Frames.Count);
        Assert.Equal(2, plan.Summary.RemovedByReason[RemovalReason.ManualRemoval]);
        Assert.Contains("1 nomi nell'elenco di rimozione non sono presenti", WarningsOf(plan));
    }

    [Fact]
    public void FrameVuoti_StrettaDisuguaglianzaSullaSoglia()
    {
        var stdDev = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["frame_0000.jpg"] = 0.5,
            ["frame_0001.jpg"] = 1.9,
            ["frame_0002.jpg"] = 2.0,
            ["frame_0003.jpg"] = 1.99,
            ["frame_0004.jpg"] = 3.0,
        };
        Func<StudioFrame, FrameStats> stats = frame => new FrameStats(100, stdDev[frame.FileName]);

        MontagePlan plan = PlanOk(Frames(5), stats, Op("autoRemove", """{ "blankFrames": true }"""));

        Assert.Equal("frame_0002.jpg|frame_0004.jpg", string.Join("|", plan.Frames.Select(f => f.FileName)));
        Assert.Equal(3, plan.Summary.RemovedByReason[RemovalReason.BlankFrame]);
    }

    [Fact]
    public void EsposizioneAnomala_MedianaEMadRimuovonoSoloLeCoseFuori()
    {
        // Mediana 100, MAD 1 (scarti 0,1,1,0,2,2,60): con k = 4 il limite è 4. Il 160 esce, il 98 (scarto 2) resta.
        double[] means = [100, 101, 99, 100, 102, 98, 160];
        Func<StudioFrame, FrameStats> stats = frame => new FrameStats(means[IndexOf(frame)], 20);

        MontagePlan plan = PlanOk(Frames(7), stats, Op("autoRemove", """{ "exposureOutliers": true }"""));

        Assert.Equal(6, plan.Frames.Count);
        Assert.DoesNotContain(plan.Frames, f => f.FileName == "frame_0006.jpg");
        Assert.Equal(1, plan.Summary.RemovedByReason[RemovalReason.ExposureOutlier]);
    }

    [Fact]
    public void EsposizioneCostante_NessunaRimozioneConAvviso()
    {
        MontagePlan plan = PlanOk(
            Frames(5),
            UniformStats(mean: 100, stdDev: 20),
            Op("autoRemove", """{ "exposureOutliers": true }"""));

        Assert.Equal(5, plan.Frames.Count);
        Assert.Contains("dispersione nulla", WarningsOf(plan));
    }

    [Fact]
    public void EsposizioneConMenoDiTreFrame_AvvisoEssunaRimozione()
    {
        MontagePlan plan = PlanOk(
            Frames(2),
            UniformStats(),
            Op("autoRemove", """{ "exposureOutliers": true }"""));

        Assert.Equal(2, plan.Frames.Count);
        Assert.Contains("meno di tre frame", WarningsOf(plan));
    }

    [Fact]
    public void StatisticheRichiesteUnaVoltaPerFrame_SoloSeServono()
    {
        int calls = 0;
        Func<StudioFrame, FrameStats> stats = _ =>
        {
            calls++;
            return new FrameStats(100, 20);
        };

        PlanOk(Frames(7), stats, Op("autoRemove", """{ "blankFrames": true, "exposureOutliers": true }"""));
        Assert.Equal(7, calls);

        calls = 0;
        PlanOk(Frames(3), stats, Op("speed", """{ "factor": 1 }"""));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Nuvole_AvvisoNonSilenziosoENessunaRimozione()
    {
        MontagePlan plan = PlanOk(
            Frames(4),
            UniformStats(),
            Op("autoRemove", """{ "clouds": true }"""));

        Assert.Equal(4, plan.Frames.Count);
        Assert.Contains("rilevazione delle nuvole non ancora disponibile", WarningsOf(plan));
    }

    [Fact]
    public void MaschereEColore_AvvisiESenzaOperazioniPixel()
    {
        MontagePlan plan = PlanOk(
            Frames(4),
            NoStatsExpected(),
            Op("infoMask", """{ "sole": true }"""),
            Op("colorCorrection", """{ "tinta": 3 }"""));

        Assert.Contains("maschera informativa", WarningsOf(plan));
        Assert.Contains("correzione del colore", WarningsOf(plan));
        Assert.Empty(plan.PixelOps);
        Assert.Equal(4, plan.Frames.Count);
    }

    [Fact]
    public void OperazioniPixel_NellOrdineDelProgetto_RotazioneZeroSaltata()
    {
        MontagePlan plan = PlanOk(
            Frames(3),
            NoStatsExpected(),
            Op("crop", """{ "x": 1, "y": 0, "width": 2, "height": 2 }"""),
            Op("rotate", """{ "degrees": 0 }"""),
            Op("rotate", """{ "degrees": 90 }"""),
            Op("mirror", """{ "horizontal": true }"""));

        Assert.Equal("crop:1,0,2,2;rotate:90;mirror:True,False", string.Join(";", plan.PixelOps.Select(op => op.Canonical)));
    }

    [Fact]
    public void OperazioneNonValida_RestituisceErroreConNumeroENessunPiano()
    {
        MontagePlanOutcome outcome = MontagePlanner.Plan(
            Frames(10),
            [Op("interval", "{}"), Op("speed", """{ "factor": 0 }""")],
            NoStatsExpected());

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.Plan);
        string error = Assert.Single(outcome.Errors);
        Assert.StartsWith("operazione 2: ", error);
        Assert.Contains("«factor» è fuori dall'intervallo", error);
    }

    [Fact]
    public void PiuVelocita_FpsDellUltimaEAvviso()
    {
        MontagePlan plan = PlanOk(
            Frames(100),
            NoStatsExpected(),
            Op("speed", """{ "factor": 2, "fps": 10 }"""),
            Op("speed", """{ "factor": 1, "fps": 20 }"""));

        Assert.Equal(50, plan.Frames.Count);
        Assert.Equal(20, plan.Fps);
        Assert.Equal(2.5, plan.Summary.DurationSeconds);
        Assert.Contains("più operazioni di velocità", WarningsOf(plan));
    }

    [Fact]
    public void Bilancio_EntrataMenoRimossiPiuRipetutiUgualeUscita()
    {
        MontagePlan plan = PlanOk(
            Frames(100),
            NoStatsExpected(),
            Op("interval", """{ "from": "2026-10-22T20:00:10Z", "to": "2026-10-22T20:00:59Z" }"""),
            Op("removeFrames", """{ "fileNames": ["frame_0010.jpg", "frame_0011.jpg", "frame_0012.jpg", "frame_0013.jpg", "frame_0014.jpg"] }"""),
            Op("speed", """{ "factor": 0.5 }"""));

        MontageSummary summary = plan.Summary;
        Assert.Equal(100, summary.InputFrames);
        Assert.Equal(50, summary.RemovedByReason[RemovalReason.Interval]);
        Assert.Equal(5, summary.RemovedByReason[RemovalReason.ManualRemoval]);
        Assert.Equal(45, summary.RepeatedFrames);
        Assert.Equal(90, summary.OutputFrames);
        Assert.Equal(summary.InputFrames - summary.RemovedByReason.Values.Sum() + summary.RepeatedFrames, summary.OutputFrames);
    }

    [Fact]
    public void MontaggioVuoto_AvvisoEDurataZero()
    {
        MontagePlan plan = PlanOk(
            Frames(0),
            NoStatsExpected(),
            Op("speed", """{ "factor": 2 }"""));

        Assert.Empty(plan.Frames);
        Assert.Equal(0.0, plan.Summary.DurationSeconds);
        Assert.Contains("montaggio è vuoto", WarningsOf(plan));
    }

    [Fact]
    public void DissolvenzaPiuLungaDelMontaggio_Avviso()
    {
        MontagePlan plan = PlanOk(
            Frames(10),
            NoStatsExpected(),
            Op("fade", """{ "kind": "in", "durationSeconds": 1 }"""));

        Assert.Contains("più lunga del montaggio", WarningsOf(plan));
    }

    [Fact]
    public void StessoProgetto_StessaImpronta_CartellaDiversaNo()
    {
        StudioOperation[] operations =
        [
            Op("speed", """{ "factor": 2, "fps": 25 }"""),
            Op("titleCard", """{ "text": "Notte", "durationSeconds": 2, "position": "start" }"""),
        ];
        List<StudioFrame> frames = Frames(20);
        List<StudioFrame> moved = frames.Select(f => f with { FilePath = "/altra/cartella/" + f.FileName }).ToList();

        string first = PlanOk(frames, NoStatsExpected(), operations).Fingerprint();
        string second = PlanOk(frames, NoStatsExpected(), operations).Fingerprint();
        string otherFolder = PlanOk(moved, NoStatsExpected(), operations).Fingerprint();
        string otherFps = PlanOk(frames, NoStatsExpected(), Op("speed", """{ "factor": 2, "fps": 24 }""")).Fingerprint();

        Assert.Equal(first, second);
        Assert.Equal(first, otherFolder);
        Assert.NotEqual(first, otherFps);
    }

    [Fact]
    public void Avvisi_NonContengonoPercorsi()
    {
        MontagePlan plan = PlanOk(
            Frames(10),
            UniformStats(),
            Op("removeFrames", """{ "fileNames": ["mancante.jpg"] }"""),
            Op("autoRemove", """{ "clouds": true }"""));

        Assert.NotEmpty(plan.Warnings);
        Assert.DoesNotContain("/dati", WarningsOf(plan));
    }
}
