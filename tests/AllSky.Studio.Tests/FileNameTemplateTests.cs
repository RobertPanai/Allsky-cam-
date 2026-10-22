using Xunit;
using System.Diagnostics;

namespace AllSky.Studio.Tests;

public sealed class FileNameTemplateTests
{
    private static FileNameTemplate Compile(string template)
    {
        Assert.True(FileNameTemplate.TryCompile(template, out FileNameTemplate? compiled, out string? error), error);
        return compiled!;
    }

    private static void RejectsCompile(string? template, string expectedPart)
    {
        bool ok = FileNameTemplate.TryCompile(template, out _, out string? error);
        Assert.False(ok, "il modello doveva essere rifiutato");
        Assert.Contains(expectedPart, error!);
    }

    [Fact]
    public void ModelloDiEsempio_LeggeDataEOra()
    {
        FileNameTemplate template = Compile("allsky_{yyyy}{MM}{dd}_{HH}{mm}{ss}.jpg");

        Assert.True(template.TryMatch("allsky_20261023_221530.jpg", out DateTime local, out _));
        Assert.Equal(new DateTime(2026, 10, 23, 22, 15, 30), local);
    }

    [Fact]
    public void Jolly_AccettaQualsiasiTestoNelMezzo()
    {
        FileNameTemplate template = Compile("cam*_{yyyy}{MM}{dd}{HH}{mm}{ss}.jpg");

        Assert.True(template.TryMatch("camera-A_20260101120000.jpg", out DateTime local, out _));
        Assert.Equal(new DateTime(2026, 1, 1, 12, 0, 0), local);
    }

    [Fact]
    public void CaratteriSpeciali_SonoLetteralmente()
    {
        FileNameTemplate template = Compile("img (1)_{yyyy}.{MM}.{dd}+x[1]_{HH}{mm}{ss}.png");

        Assert.True(template.TryMatch("img (1)_2026.10.23+x[1]_221530.png", out _, out _));
        // Il punto del modello è un punto letterale, non un jolly.
        Assert.False(template.TryMatch("img (1)_2026x10x23+x[1]_221530.png", out _, out _));
        // Le parentesi quadre del modello non sono classi di caratteri.
        Assert.False(template.TryMatch("img (1)_2026.10.23+x1_221530.png", out _, out _));
    }

    [Fact]
    public void SenzaOra_OraMezzanotte()
    {
        FileNameTemplate template = Compile("d_{yyyy}{MM}{dd}.jpg");

        Assert.True(template.TryMatch("d_20261023.jpg", out DateTime local, out _));
        Assert.Equal(new DateTime(2026, 10, 23), local);
    }

    [Fact]
    public void Estensione_IgnoraMaiuscole()
    {
        FileNameTemplate template = Compile("IMG_{yyyy}{MM}{dd}.JPG");

        Assert.True(template.TryMatch("img_20261023.jpg", out _, out _));
    }

    [Fact]
    public void DataNonValida_Rifiutata()
    {
        FileNameTemplate template = Compile("a_{yyyy}{MM}{dd}.jpg");

        Assert.False(template.TryMatch("a_20260230.jpg", out _, out string? error));
        Assert.Contains("data non valida", error!);
    }

    [Fact]
    public void NomeNonConforme_Rifiutato()
    {
        FileNameTemplate template = Compile("a_{yyyy}{MM}{dd}.jpg");

        Assert.False(template.TryMatch("b_20261023.jpg", out _, out string? error));
        Assert.Contains("non corrisponde", error!);
    }

    [Fact]
    public void ModelliNonValidi_SonoRifiutatiConMessaggio()
    {
        RejectsCompile(null, "vuoto");
        RejectsCompile("   ", "vuoto");
        RejectsCompile("x_{anno}.jpg", "segnaposto non riconosciuto");
        RejectsCompile("{yyyy}{yyyy}{MM}{dd}", "ripetuto");
        RejectsCompile("{yyyy}{MM}", "{dd}");
        RejectsCompile("{yyyy}{MM}{dd}{", "non chiuso");
        RejectsCompile("a}{yyyy}{MM}{dd}", "senza apertura");
        RejectsCompile("a/{yyyy}{MM}{dd}", "separatori");
        RejectsCompile("a\\{yyyy}{MM}{dd}", "separatori");
        RejectsCompile("*{yyyy}{MM}{dd}*a*b*c*d*e*f*g*h*i*", "troppi jolly");
        RejectsCompile(new string('a', 201) + "{yyyy}{MM}{dd}", "supera");
    }

    [Fact]
    public void JollyCatastrofici_NonEsplodonoIlTempo()
    {
        FileNameTemplate template = Compile("*a*a*a*a*a*a*b_{yyyy}{MM}{dd}.jpg");
        string hostile = new string('a', 20000);

        var watch = Stopwatch.StartNew();
        bool matched = template.TryMatch(hostile, out _, out string? error);
        watch.Stop();

        Assert.False(matched);
        Assert.NotNull(error);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"troppo lento: {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void FormatiNumericiFuoriRange_Rifiutati()
    {
        FileNameTemplate template = Compile("t_{yyyy}{MM}{dd}_{HH}{mm}{ss}.jpg");

        Assert.False(template.TryMatch("t_20261023_250000.jpg", out _, out _));
        Assert.False(template.TryMatch("t_20261023_006000.jpg", out _, out _));
        Assert.False(template.TryMatch("t_20261023_000060.jpg", out _, out _));
        Assert.True(template.TryMatch("t_20261023_235959.jpg", out _, out _));
    }
}
