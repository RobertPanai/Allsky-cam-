using System.Text.Json.Nodes;
using Xunit;

namespace AllSky.Studio.Tests;

public sealed class ExportProjectTests : IDisposable
{
    private readonly TempFolder _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static StudioProject ProjectWithExports(string folder) => new()
    {
        Name = "Notte di prova",
        LocalTimeZoneId = "Europe/Rome",
        Operations =
        [
            MontageFixtures.Op("interval", """{ "from": "2026-10-22T20:00:00Z", "to": "2026-10-22T20:00:59Z" }"""),
        ],
        Exports =
        [
            new ExportTarget
            {
                Preset = ExportPresets.FullHd,
                Destination = new LocalFileDestination
                {
                    Folder = folder,
                    FileNameTemplate = "{project} {date}",
                    Overwrite = OverwritePolicy.Rename,
                },
            },
            new ExportTarget
            {
                Preset = ExportPresets.Social,
                Destination = new YouTubeDestination
                {
                    Title = "Timelapse {notte:yyyy-MM-dd}",
                    Description = "Da {luogo}",
                    Privacy = "unlisted",
                },
            },
            new ExportTarget
            {
                Preset = ExportPresets.Hd,
                Destination = new TelegramDestination { ChatId = "-1001234567890", Caption = "Notte" },
            },
        ],
    };

    [Fact]
    public void ProgettoConEsportazioni_SalvatoERicaricato_DaLoStessoLavoro()
    {
        string folder = _tmp.Root;
        StudioProject original = ProjectWithExports(folder);
        string path = _tmp.FilePath("con-export.asbproject");
        StudioProjectStore.Save(path, original);

        StudioProject loaded = StudioProjectStore.Load(path);

        Assert.Equal(original.Exports, loaded.Exports);
        Assert.IsType<LocalFileDestination>(loaded.Exports[0].Destination);
        Assert.IsType<YouTubeDestination>(loaded.Exports[1].Destination);
        Assert.IsType<TelegramDestination>(loaded.Exports[2].Destination);

        MontagePlan plan = MontagePlanner.Plan(MontageFixtures.Frames(60), original.Operations, MontageFixtures.UniformStats()).Plan!;
        DateTimeOffset when = new(2026, 10, 22, 21, 0, 0, TimeSpan.Zero);
        IReadOnlyList<ExportJob> fromOriginal = ExportJobFactory.FromProject(original, path, plan, when);
        IReadOnlyList<ExportJob> fromLoaded = ExportJobFactory.FromProject(loaded, path, plan, when);

        Assert.Equal(3, fromLoaded.Count);
        Assert.Equal(fromOriginal, fromLoaded);
        Assert.Equal(fromOriginal.Select(j => j.Id), fromLoaded.Select(j => j.Id));
        Assert.Equal(new[] { 1, 2, 3 }, fromLoaded.Select(j => j.Sequence));
        Assert.Equal(3, fromLoaded.Select(j => j.Id).Distinct().Count());
    }

    [Fact]
    public void ProgettoSenzaEsportazioni_NonScriveIlCampo()
    {
        string path = _tmp.FilePath("senza-export.asbproject");
        StudioProjectStore.Save(path, new StudioProject { Name = "Solo montaggio" });

        JsonNode? node = JsonNode.Parse(File.ReadAllText(path));

        Assert.NotNull(node);
        Assert.Null(node!["exports"]);
        Assert.Empty(StudioProjectStore.Load(path).Exports);
    }

    [Fact]
    public void ProgettoConDestinazioneNonValida_RifiutatoAlSalvataggio()
    {
        StudioProject project = new()
        {
            Name = "Errore",
            Exports =
            [
                new ExportTarget { Destination = new TelegramDestination { ChatId = "abc" } },
            ],
        };

        StudioException ex = Assert.Throws<StudioException>(() =>
            StudioProjectStore.Save(_tmp.FilePath("errore.asbproject"), project));

        Assert.Contains("esportazione 1", ex.Message);
        Assert.DoesNotContain(_tmp.Root, ex.Message);
        Assert.False(File.Exists(_tmp.FilePath("errore.asbproject")));
    }

    [Fact]
    public void ProgettoConPresetFuoriScala_RifiutatoAlCaricamento()
    {
        const string json = """
        {
          "version": 1,
          "name": "Guasto",
          "exports": [
            {
              "preset": { "name": "Male", "width": 1921, "height": 1080, "quality": 20, "container": "mp4" },
              "destination": { "type": "localFile", "folder": "/tmp/x", "fileNameTemplate": "{project}", "overwrite": "Fail" }
            }
          ]
        }
        """;
        string path = _tmp.FilePath("guasto.asbproject");
        File.WriteAllText(path, json);

        StudioException ex = Assert.Throws<StudioException>(() => StudioProjectStore.Load(path));

        Assert.Contains("esportazione 1", ex.Message);
    }
}
