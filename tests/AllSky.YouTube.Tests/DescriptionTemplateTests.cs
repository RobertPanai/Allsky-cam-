using System.Text;
using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class DescriptionTemplateTests
{
    [Fact]
    public void Render_RigheMultilineSonoMantenute()
    {
        DescriptionTemplate template = DescriptionTemplate.Parse("Riga 1\r\nRiga 2\n\n\n\nRiga 3 {luogo}").Template!;

        Assert.Equal("Riga 1\nRiga 2\n\nRiga 3 Osservatorio", template.Render(Fixtures.Sample()));
    }

    [Fact]
    public void Render_ControlliNellaDescrizione_DiventanoSpazi()
    {
        DescriptionTemplate template = DescriptionTemplate.Parse("A\u0001\u0002B").Template!;

        Assert.Equal("A B", template.Render(Fixtures.Sample()));
    }

    [Fact]
    public void Render_TroncaPerByteUtf8SenzaSpezzareCodePoint()
    {
        // Ogni emoji vale 4 byte e 2 unità UTF-16. Budget: 5000 - 3 (per '…') = 4997 byte, quindi 1249 emoji.
        string text = string.Concat(Enumerable.Repeat("\U0001F30C", 1500));
        DescriptionTemplate template = DescriptionTemplate.Parse(text).Template!;

        string rendered = template.Render(Fixtures.Sample());
        Assert.True(Encoding.UTF8.GetByteCount(rendered) <= YouTubeLimits.DescriptionMaxBytes);
        Assert.EndsWith("…", rendered);
        Assert.Equal(1249 * 2 + 1, rendered.Length);
        Assert.DoesNotContain("\uFFFD", rendered);
    }

    [Fact]
    public void Parse_DescrizioneVuota_EValida()
    {
        TemplateParseResult<DescriptionTemplate> parsed = DescriptionTemplate.Parse("");

        Assert.True(parsed.IsValid);
        Assert.Equal("", parsed.Template!.Render(Fixtures.Sample()));
    }

    [Fact]
    public void Parse_SegnaposoSconosciuto_ConPosizione()
    {
        TemplateParseResult<DescriptionTemplate> parsed = DescriptionTemplate.Parse("Riga\n{bad}");

        Assert.False(parsed.IsValid);
        Assert.Equal(5, parsed.Errors[0].Position);
    }

    [Fact]
    public void Preview_DescrizioneMultilinea()
    {
        TemplatePreview preview = DescriptionTemplate.Preview("Meteore: {meteore}\nTransiti: {transiti}", Fixtures.Sample());

        Assert.True(preview.IsValid);
        Assert.Equal("Meteore: 3\nTransiti: 1", preview.Text);
    }
}
