namespace AllSky.YouTube;

/// <summary>
/// Modello del titolo di un video, con segnaposto. Si crea solo con <see cref="Parse"/>, che non lancia:
/// gli errori stanno nel risultato.
/// </summary>
/// <remarks>
/// Segnaposto: {data}, {data:formato} (default yyyy-MM-dd), {notte}, {notte:formato} (default yyyy-MM-dd),
/// {ora}, {ora:formato} (default HH:mm), {luogo}, {fotogrammi}, {durata} (mm:ss), {meteore}, {transiti}.
/// Le graffe letterali si scrivono con {{ e }}.
/// </remarks>
public sealed class TitleTemplate
{
    private readonly IReadOnlyList<TemplateEngine.Token> _tokens;

    private TitleTemplate(IReadOnlyList<TemplateEngine.Token> tokens) => _tokens = tokens;

    /// <summary>Analizza il modello e restituisce tutti gli errori trovati, con la posizione di ciascuno.</summary>
    public static TemplateParseResult<TitleTemplate> Parse(string template)
    {
        if (template is null)
            return Failed(new TemplateError(0, "posizione 0: il modello del titolo non può essere nullo."));
        if (template.Trim().Length == 0)
            return Failed(new TemplateError(0, "posizione 0: il titolo non può essere vuoto."));

        var errors = new List<TemplateError>();
        List<TemplateEngine.Token> tokens = TemplateEngine.Tokenize(template, errors);
        if (errors.Count > 0)
            return new TemplateParseResult<TitleTemplate>(null, errors);
        return new TemplateParseResult<TitleTemplate>(new TitleTemplate(tokens), Array.Empty<TemplateError>());
    }

    /// <summary>
    /// Titolo finale: testo normalizzato (spazi ridotti, controlli e '&lt;' '&gt;' rimossi) e ritagliato
    /// a <see cref="YouTubeLimits.TitleMaxLength"/> unità UTF-16.
    /// </summary>
    public string Render(TitleContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        string text = TemplateEngine.NormalizeLine(TemplateEngine.Render(_tokens, ctx));
        return TemplateEngine.TruncateUtf16(text, YouTubeLimits.TitleMaxLength);
    }

    /// <summary>Anteprima del titolo con un contesto di esempio: parse e render in un solo passo.</summary>
    public static TemplatePreview Preview(string template, TitleContext sample)
    {
        TemplateParseResult<TitleTemplate> parsed = Parse(template);
        return parsed.Template is { } title
            ? new TemplatePreview(title.Render(sample), parsed.Errors)
            : new TemplatePreview(null, parsed.Errors);
    }

    private static TemplateParseResult<TitleTemplate> Failed(TemplateError error) =>
        new(null, new[] { error });
}
