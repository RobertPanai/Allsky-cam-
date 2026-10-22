using Xunit;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AllSky.Studio.Tests;

public sealed class ProjectStoreTests : IDisposable
{
    private readonly TempFolder _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static StudioProject SampleProject() => new()
    {
        Name = "Notte di prova",
        LocalTimeZoneId = "Europe/Rome",
        Sources =
        [
            new StudioSource { Kind = StudioSourceKinds.ArchiveNight, NightId = "2026-10-22" },
            new StudioSource
            {
                Kind = StudioSourceKinds.Folder,
                SourcePath = "/dati/altri",
                TimestampRule = new TimestampRule
                {
                    Mode = TimestampRuleModes.FileNamePattern,
                    FileNamePattern = "allsky_{yyyy}{MM}{dd}_{HH}{mm}{ss}.jpg",
                },
                Extensions = [".jpg", ".png"],
            },
            new StudioSource { Kind = StudioSourceKinds.Video, SourcePath = "/dati/cielo.mp4", IntervalSeconds = 2.5 },
        ],
        Operations =
        [
            new StudioOperation
            {
                Type = "interval",
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["from"] = Json("\"22:00\""),
                    ["fps"] = Json("25"),
                },
            },
        ],
    };

    [Fact]
    public void RoundTrip_SalvaERicaricaSenzaPerdereNulla()
    {
        string first = _tmp.FilePath("prova.asbproject");
        StudioProjectStore.Save(first, SampleProject());

        StudioProject loaded = StudioProjectStore.Load(first);

        Assert.Equal(1, loaded.Version);
        Assert.Equal("Notte di prova", loaded.Name);
        Assert.Equal("Europe/Rome", loaded.LocalTimeZoneId);
        Assert.Equal(3, loaded.Sources.Count);
        Assert.Equal("2026-10-22", loaded.Sources[0].NightId);
        Assert.Equal(TimestampRuleModes.FileNamePattern, loaded.Sources[1].TimestampRule!.Mode);
        Assert.Equal(2.5, loaded.Sources[2].IntervalSeconds!.Value);
        Assert.Equal("interval", loaded.Operations[0].Type);
        Assert.Equal(25, loaded.Operations[0].Parameters["fps"].GetInt32());

        string second = _tmp.FilePath("copia.asbproject");
        StudioProjectStore.Save(second, loaded);
        Assert.Equal(File.ReadAllText(first), File.ReadAllText(second));
    }

    [Fact]
    public void RoundTrip_CampiSconosciutiERestanoRiscritti()
    {
        const string original = """
        {
          "version": 1,
          "name": "Con campi nuovi",
          "futuroProgetto": { "x": [1, 2] },
          "sources": [
            { "kind": "archiveNight", "nightId": "2026-10-22", "colore": "rosso" }
          ],
          "operations": [
            { "type": "crop", "parameters": { "x": 10, "y": 20, "w": 100, "h": 50, "nuovo": true }, "nota": "ciao" }
          ]
        }
        """;
        string path = _tmp.FilePath("nuovi.asbproject");
        File.WriteAllText(path, original);

        StudioProject loaded = StudioProjectStore.Load(path);
        Assert.True(loaded.Operations[0].Parameters["nuovo"].GetBoolean());
        Assert.Equal("ciao", loaded.Operations[0].UnknownFields["nota"].GetString());

        string saved = _tmp.FilePath("salvato.asbproject");
        StudioProjectStore.Save(saved, loaded);
        JsonNode? before = JsonNode.Parse(original);
        JsonNode? after = JsonNode.Parse(File.ReadAllText(saved));
        Assert.True(JsonNode.DeepEquals(before, after), "il file salvato deve contenere gli stessi campi dell'originale");
    }

    [Fact]
    public void VersioneFutura_ErroreChiaroSenzaPercorsi()
    {
        string path = _tmp.FilePath("futuro.asbproject");
        File.WriteAllText(path, """{ "version": 2, "name": "Nuovo" }""");

        StudioException error = Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Load(path);
        });

        Assert.Contains("creato da una versione più recente", error.Message);
        Assert.DoesNotContain(_tmp.Root, error.Message);
    }

    [Fact]
    public void JsonCorrotto_ErroreInItalianoNonEccezioneGrezza()
    {
        string path = _tmp.FilePath("rotto.asbproject");
        File.WriteAllText(path, """{ "version": 1, "name": """);

        StudioException error = Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Load(path);
        });

        Assert.Contains("non è un progetto valido", error.Message);
        Assert.DoesNotContain(_tmp.Root, error.Message);
    }

    [Fact]
    public void CampoDiTipoSbagliato_ErroreControllato()
    {
        string path = _tmp.FilePath("tipo.asbproject");
        File.WriteAllText(path, """{ "version": 1, "name": 5 }""");

        StudioException error = Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Load(path);
        });

        Assert.Contains("campi non validi", error.Message);
    }

    [Fact]
    public void FileMancante_ErroreConNomeSoloFile()
    {
        string path = _tmp.FilePath("assente.asbproject");

        StudioException error = Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Load(path);
        });

        Assert.Contains("assente.asbproject", error.Message);
        Assert.DoesNotContain(_tmp.Root, error.Message);
    }

    [Fact]
    public void VersioneMancante_Errore()
    {
        string path = _tmp.FilePath("senza-versione.asbproject");
        File.WriteAllText(path, """{ "name": "Senza versione" }""");

        StudioException error = Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Load(path);
        });

        Assert.Contains("numero di versione", error.Message);
    }

    [Fact]
    public void TipoSorgenteSconosciuto_Errore()
    {
        string path = _tmp.FilePath("sorgente.asbproject");
        File.WriteAllText(path, """{ "version": 1, "name": "X", "sources": [ { "kind": "fits" } ] }""");

        StudioException error = Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Load(path);
        });

        Assert.Contains("tipo di sorgente non riconosciuto", error.Message);
    }

    [Fact]
    public void Salvataggio_NonLasciaFileTemporanei()
    {
        StudioProjectStore.Save(_tmp.FilePath("pulito.asbproject"), SampleProject());
        StudioProjectStore.Save(_tmp.FilePath("pulito.asbproject"), SampleProject());

        Assert.Empty(Directory.GetFiles(_tmp.Root, "*.tmp"));
        Assert.Single(Directory.GetFiles(_tmp.Root, "*.asbproject"));
    }

    [Fact]
    public void ProgettoNonValido_NonToccaIlFileEsistente()
    {
        string path = _tmp.FilePath("intatto.asbproject");
        StudioProjectStore.Save(path, SampleProject());
        string before = File.ReadAllText(path);

        StudioException error = Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Save(path, SampleProject() with { Name = " " });
        });

        Assert.Contains("nome", error.Message);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_tmp.Root, "*.tmp"));
    }

    [Fact]
    public void VersioneDueInScrittura_Rifiutata()
    {
        string path = _tmp.FilePath("v2.asbproject");

        Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Save(path, SampleProject() with { Version = 2 });
        });

        Assert.False(File.Exists(path), "un progetto non valido non deve creare il file");
    }

    [Fact]
    public void NightIdNonValido_Rifiutato()
    {
        var project = SampleProject() with
        {
            Sources = [new StudioSource { Kind = StudioSourceKinds.ArchiveNight, NightId = "2026-13-40" }],
        };

        StudioException error = Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Save(_tmp.FilePath("notte.asbproject"), project);
        });

        Assert.Contains("identificatore di notte non valido", error.Message);
    }

    [Fact]
    public void FusoOrarioSconosciuto_Rifiutato()
    {
        Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Save(_tmp.FilePath("fuso.asbproject"), SampleProject() with { LocalTimeZoneId = "Luogo/Inventato" });
        });
    }

    [Fact]
    public void ModelloDiDataNonValidoNellaCartella_Rifiutato()
    {
        var project = SampleProject() with
        {
            Sources =
            [
                new StudioSource
                {
                    Kind = StudioSourceKinds.Folder,
                    SourcePath = "/dati",
                    TimestampRule = new TimestampRule { Mode = TimestampRuleModes.FileNamePattern, FileNamePattern = "x_{anno}.jpg" },
                },
            ],
        };

        StudioException error = Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Save(_tmp.FilePath("modello.asbproject"), project);
        });

        Assert.Contains("segnaposto non riconosciuto", error.Message);
    }

    [Fact]
    public void SalvataggioInCartellaInesistente_Errore()
    {
        string path = System.IO.Path.Combine(_tmp.Root, "non-esiste", "p.asbproject");

        Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Save(path, SampleProject());
        });
    }

    [Fact]
    public void OperazioniSenzaTipo_Rifiutate()
    {
        var project = SampleProject() with { Operations = [new StudioOperation { Type = "" }] };

        Assert.Throws<StudioException>(() =>
        {
            StudioProjectStore.Save(_tmp.FilePath("op.asbproject"), project);
        });
    }
}
