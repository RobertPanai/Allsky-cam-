using System.Text;
using AllSky.Core.LogView;
using Xunit;

namespace AllSky.Core.Tests.LogView;

public sealed class LogFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "allsky-logview-" + Guid.NewGuid().ToString("N"));

    public LogFilesTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Cartella temporanea: un file ancora aperto non fa fallire il test.
        }
    }

    [Fact]
    public void LatestDailyLog_SceglieLaDataPiuRecente()
    {
        File.WriteAllText(Path.Combine(_folder, "allsky-20261020.log"), "a");
        File.WriteAllText(Path.Combine(_folder, "allsky-20261022.log"), "b");
        File.WriteAllText(Path.Combine(_folder, "allsky-20261021.log"), "c");
        File.WriteAllText(Path.Combine(_folder, "startup.log"), "d");
        File.WriteAllText(Path.Combine(_folder, "crash.log"), "e");

        Assert.Equal(Path.Combine(_folder, "allsky-20261022.log"), LogFiles.LatestDailyLog(_folder));
    }

    [Fact]
    public void LatestDailyLog_CartellaVuotaOInesistente_Null()
    {
        Assert.Null(LogFiles.LatestDailyLog(_folder));
        Assert.Null(LogFiles.LatestDailyLog(Path.Combine(_folder, "non-esiste")));
    }

    [Fact]
    public void ReadTailLines_FileCorto_LeggeTutteLeRighe()
    {
        string path = Path.Combine(_folder, "allsky-20261022.log");
        File.WriteAllText(path, "uno\r\ndue\r\n", new UTF8Encoding(false));

        IReadOnlyList<string> lines = LogFiles.ReadTailLines(path, 1024);

        Assert.Equal(["uno", "due", ""], lines);
    }

    [Fact]
    public void ReadTailLines_FileLungo_TagliaLaPrimaRigaParziale()
    {
        string path = Path.Combine(_folder, "allsky-20261022.log");
        var text = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            text.Append("riga numero ").Append(i.ToString("D3")).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));

        IReadOnlyList<string> lines = LogFiles.ReadTailLines(path, 200);

        Assert.Equal("riga numero 199", lines.Where(l => l.Length > 0).Last());
        Assert.True(lines[0].StartsWith("riga numero ", StringComparison.Ordinal));
        Assert.True(lines.Count < 200);
    }

    [Fact]
    public void ReadTailLines_FileInesistente_ListaVuota()
    {
        Assert.Empty(LogFiles.ReadTailLines(Path.Combine(_folder, "nessuno.log"), 1024));
    }

    [Fact]
    public void ReadTailLines_LeggeAncheMentreIlFileEAperto()
    {
        string path = Path.Combine(_folder, "allsky-20261022.log");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        byte[] bytes = Encoding.UTF8.GetBytes("2026-10-22 10:00:00  Avvio\n");
        writer.Write(bytes);
        writer.Flush();

        IReadOnlyList<string> lines = LogFiles.ReadTailLines(path, 1024);

        Assert.Equal("2026-10-22 10:00:00  Avvio", lines[0]);
    }
}
