namespace AllSky.YouTube;

/// <summary>
/// Modello della descrizione di un video, con gli stessi segnaposto del titolo (vedi <see cref="TitleTemplate"/>).
/// Le righe restano separate; la descrizione può essere vuota.
/// </summary>
public sealed class DescriptionTemplate
{
    private readonly IReadOnlyList<TemplateEngine.Token> _tokens;

    private DescriptionTemplate(IReadOnlyList<TemplateEngine.Token> tokens) => _tokens = tokens;

    /// <summary>Analizza il modello e restituisce tutti gli errori trovati, con la posizione di ciascuno.</summary>
    public static TemplateParseResult<DescriptionTemplate> Parse(string template)
    {
        if (template is null)
            return new TemplateParseResult<DescriptionTemplate>(null, new[] { new TemplateError(0, "posizione 0: il modello della descrizione non può essere nullo.") });

        var errors = new List<TemplateError>();
        List<TemplateEngine.Token> tokens = TemplateEngine.Tokenize(template, errors);
        if (errors.Count > 0)
            return new TemplateParseResult<DescriptionTemplate>(null, errors);
        return new TemplateParseResult<DescriptionTemplate>(new DescriptionTemplate(tokens), Array.Empty<TemplateError>());
    }

    /// <summary>
    /// Descrizione finale: righe normalizzate e ritagliata a <see cref="YouTubeLimits.DescriptionMaxBytes"/> byte UTF-8.
    /// </summary>
    public string Render(TitleContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string text = TemplateEngine.NormalizeMultiline(TemplateEngine.Render(_tokens, ctx));
        return TemplateEngine.TruncateUtf8(text, YouTubeLimits.DescriptionMaxBytes);
    }

    /// <summary>Anteprima della descrizione con un contesto di esempio: parse e render in un solo passo.</summary>
    public static TemplatePreview Preview(string template, TitleContext sample)
    {
        TemplateParseResult<DescriptionTemplate> parsed = Parse(template);
        return parsed.Template is { } description
            ? new TemplatePreview(description.Render(sample), parsed.Errors)
            : new TemplatePreview(null, parsed.Errors);
    }
}
