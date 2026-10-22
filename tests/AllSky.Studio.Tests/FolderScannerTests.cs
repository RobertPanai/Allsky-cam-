using Xunit;
namespace AllSky.Studio.Tests;

public sealed class FolderScannerTests : IDisposable
{
    private readonly TempFolder _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private static TimestampRule Exif() => new() { Mode = TimestampRuleModes.Exif };

    private static TimestampRule Pattern(string template) => new()
    {
        Mode = TimestampRuleModes.FileNamePattern,
        FileNamePattern = template,
    };

    private static TimestampRule Modified() => new() { Mode = TimestampRuleModes.FileModifiedTime };

    private static string Names(IEnumerable<StudioFrame> frames) => string.Join("|", frames.Select(f => f.FileName));

    private static string Skipped(ScanResult result) =>
        string.Join("|", result.Skipped.Select(s => s.FileName + "=" + s.Reason));

    private string Write(string name, byte[] bytes)
    {
        string path = _tmp.FilePath(name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private ScanResult Scan(TimestampRule rule, string? zone = "Europe/Rome", IReadOnlyList<string>? extensions = null) =>
        FolderScanner.Scan(_tmp.Root, rule, new ScanOptions { LocalTimeZoneId = zone, Extensions = extensions });

    [Fact]
    public void Exif_OrdinaPerIstanteEPoiPerNome_ESegnalaIFrameCondivisi()
    {
        Write("b.jpg", JpegBuilder.WithExif("2026:10:23 22:00:00", null));
        Write("a.jpg", JpegBuilder.WithExif("2026:10:23 21:00:00", null));
        Write("c.jpeg", JpegBuilder.WithExif("2026:10:23 22:00:00", null, bigEndian: true));
        Write("d.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]);
        Write("note.txt", "testo"u8.ToArray());
        Write("rotto.jpg", new byte[] { 1, 2, 3, 4, 5 });
        Write("tronco.jpg", JpegBuilder.WithExif("2026:10:23 20:00:00", null, cutAt: 50));

        ScanResult result = Scan(Exif());

        Assert.Equal("a.jpg|b.jpg|c.jpeg", Names(result.Frames));
        // 21:00 locali in ottobre (ora legale, +2) = 19:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 19, 0, 0, TimeSpan.Zero), result.Frames[0].TimestampUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 20, 0, 0, TimeSpan.Zero), result.Frames[1].TimestampUtc);

        string skipped = Skipped(result);
        Assert.Contains("d.png=EXIF non disponibile nei PNG", skipped);
        Assert.Contains("note.txt=estensione non ammessa", skipped);
        Assert.Contains("rotto.jpg=non è un'immagine JPEG o PNG", skipped);
        Assert.Contains("tronco.jpg=data EXIF non trovata", skipped);

        Assert.Contains(result.Warnings, w => w.Contains("istanti condivisi"));
        AssertNoPaths(result);
    }

    [Fact]
    public void Exif_SenzaFusoOrario_TrattaComeUtcConAvviso()
    {
        Write("a.jpg", JpegBuilder.WithExif("2026:10:23 21:00:00", null));

        ScanResult result = Scan(Exif(), zone: null);

        Assert.Equal(new DateTimeOffset(2026, 10, 23, 21, 0, 0, TimeSpan.Zero), result.Frames[0].TimestampUtc);
        Assert.Contains(result.Warnings, w => w.Contains("UTC"));
    }

    [Fact]
    public void FileNamePattern_LeggeIlNomeESegnalaIScartati()
    {
        Write("allsky_20261023_220000.jpg", JpegBuilder.Plain());
        Write("allsky_20261023_210000.jpg", JpegBuilder.Plain());
        Write("altro.jpg", JpegBuilder.Plain());
        Write("allsky_20261099_000000.jpg", JpegBuilder.Plain());

        ScanResult result = Scan(Pattern("allsky_{yyyy}{MM}{dd}_{HH}{mm}{ss}.jpg"));

        Assert.Equal("allsky_20261023_210000.jpg|allsky_20261023_220000.jpg", Names(result.Frames));
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 19, 0, 0, TimeSpan.Zero), result.Frames[0].TimestampUtc);
        string skipped = Skipped(result);
        Assert.Contains("altro.jpg=il nome non corrisponde al modello.", skipped);
        Assert.Contains("allsky_20261099_000000.jpg=data non valida nel nome file.", skipped);
        AssertNoPaths(result);
    }

    [Fact]
    public void FileModifiedTime_InUtcConOrdinamentoStabile()
    {
        string z = Write("z.jpg", JpegBuilder.Plain());
        string m = Write("m.jpg", JpegBuilder.Plain());
        string late = Write("tardi.jpg", JpegBuilder.Plain());
        var instant = new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(z, instant);
        File.SetLastWriteTimeUtc(m, instant);
        File.SetLastWriteTimeUtc(late, instant.AddHours(3));

        ScanResult result = Scan(Modified());

        Assert.Equal("m.jpg|z.jpg|tardi.jpg", Names(result.Frames));
        Assert.Equal(new DateTimeOffset(instant, TimeSpan.Zero), result.Frames[0].TimestampUtc);
        Assert.Equal(new DateTimeOffset(instant.AddHours(3), TimeSpan.Zero), result.Frames[2].TimestampUtc);
    }

    [Fact]
    public void Estensioni_LimitanoLaScansione()
    {
        Write("a.jpg", JpegBuilder.Plain());
        Write("b.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        ScanResult result = Scan(Modified(), extensions: [".png"]);

        Assert.Equal("b.png", Names(result.Frames));
        Assert.Contains("a.jpg=estensione non ammessa", Skipped(result));
    }

    [Fact]
    public void OrologioAmbiguo_EOrarioInesistente_GeneranoAvvisi()
    {
        Write("t_20261025_023000.jpg", JpegBuilder.Plain());
        Write("t_20260329_023000.jpg", JpegBuilder.Plain());

        ScanResult result = Scan(Pattern("t_{yyyy}{MM}{dd}_{HH}{mm}{ss}.jpg"));

        Assert.Equal(2, result.Frames.Count);
        Assert.Contains(result.Warnings, w => w.Contains("ambiguo"));
        Assert.Contains(result.Warnings, w => w.Contains("inesistente"));
        // 2026-03-29 02:30 inesistente: offset +1 => 01:30 UTC. Il frame non viene scartato.
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), result.Frames[0].TimestampUtc);
    }

    [Fact]
    public void IntervalloMedioOltreUnOra_GeneraAvviso()
    {
        var start = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++)
        {
            string path = Write($"f{i}.jpg", JpegBuilder.Plain());
            File.SetLastWriteTimeUtc(path, start.AddHours(2 * i));
        }

        ScanResult result = Scan(Modified());

        Assert.Contains(result.Warnings, w => w.Contains("intervallo mediano"));
        Assert.DoesNotContain("salti di data", string.Join(" ", result.Warnings));
    }

    [Fact]
    public void SaltoDiData_GeneraAvviso()
    {
        var start = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        string first = Write("uno.jpg", JpegBuilder.Plain());
        File.SetLastWriteTimeUtc(first, start);
        string second = Write("due.jpg", JpegBuilder.Plain());
        File.SetLastWriteTimeUtc(second, start.AddHours(40));

        ScanResult result = Scan(Modified());

        Assert.Contains(result.Warnings, w => w.Contains("salti di data"));
    }

    [Fact]
    public void Anteprima_PrimiECinqueUltimi()
    {
        var start = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 12; i++)
        {
            string path = Write($"p{i:00}.jpg", JpegBuilder.Plain());
            File.SetLastWriteTimeUtc(path, start.AddMinutes(i));
        }

        ScanResult result = Scan(Modified());

        Assert.Equal(10, result.Preview.Count);
        Assert.Equal("p00", result.Preview[0].FileName.Substring(0, 3));
        Assert.Equal("p04", result.Preview[4].FileName.Substring(0, 3));
        Assert.Equal("p07", result.Preview[5].FileName.Substring(0, 3));
        Assert.Equal("p11", result.Preview[9].FileName.Substring(0, 3));
    }

    [Fact]
    public void Anteprima_SenzaRipetizioniSePiccola()
    {
        Write("a.jpg", JpegBuilder.Plain());
        Write("b.jpg", JpegBuilder.Plain());
        Write("c.jpg", JpegBuilder.Plain());

        ScanResult result = Scan(Modified());

        Assert.Equal(3, result.Preview.Count);
    }

    [Fact]
    public void CartellaInesistente_Errore()
    {
        Assert.Throws<StudioException>(() =>
        {
            FolderScanner.Scan(_tmp.FilePath("non-esiste"), Modified());
        });
    }

    [Fact]
    public void ModelloNonValido_Errore()
    {
        Assert.Throws<StudioException>(() =>
        {
            FolderScanner.Scan(_tmp.Root, Pattern("x_{anno}.jpg"));
        });
    }

    [Fact]
    public void FusoSconosciuto_Errore()
    {
        Assert.Throws<StudioException>(() =>
        {
            FolderScanner.Scan(_tmp.Root, Exif(), new ScanOptions { LocalTimeZoneId = "Luogo/Inventato" });
        });
    }

    [Fact]
    public void CollegamentiSimbolici_FuoriNonSeguiti_DentroAccettati()
    {
        using var outside = new TempFolder();
        string externalTarget = outside.FilePath("esterno.jpg");
        File.WriteAllBytes(externalTarget, JpegBuilder.Plain());
        string inside = Write("interno.jpg", JpegBuilder.Plain());

        File.CreateSymbolicLink(_tmp.FilePath("fuori.jpg"), externalTarget);
        File.CreateSymbolicLink(_tmp.FilePath("dentro.jpg"), inside);

        ScanResult result = Scan(Modified());

        Assert.Contains(result.Skipped, s => s.FileName == "fuori.jpg" && s.Reason == "collegamento fuori dalla cartella");
        Assert.Contains(result.Frames, f => f.FileName == "dentro.jpg");
        Assert.DoesNotContain(result.Frames, f => f.FileName == "fuori.jpg");
        AssertNoPaths(result);
    }

    [Fact]
    public void ScansioneNonRicorsiva_SottocartelleIgnorate()
    {
        Directory.CreateDirectory(_tmp.FilePath("sotto"));
        File.WriteAllBytes(_tmp.FilePath("sotto/nascosto.jpg"), JpegBuilder.Plain());
        Write("visibile.jpg", JpegBuilder.Plain());

        ScanResult result = Scan(Modified());

        Assert.Equal("visibile.jpg", Names(result.Frames));
    }

    private void AssertNoPaths(ScanResult result)
    {
        foreach (string text in result.Warnings.Concat(result.Skipped.Select(s => s.FileName + " " + s.Reason)))
        {
            Assert.DoesNotContain(_tmp.Root, text);
        }
    }
}
