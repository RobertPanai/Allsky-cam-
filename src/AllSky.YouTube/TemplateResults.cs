namespace AllSky.YouTube;

/// <summary>Errore di un modello. <see cref="Position"/> e l'indice del carattere nel testo originale (0 = primo carattere).</summary>
public sealed record TemplateError(int Position, string Message);

/// <summary>Esito di Parse. Il modello è utilizzabile solo se <see cref="Errors"/> è vuoto.</summary>
public sealed record TemplateParseResult<T>(T? Template, IReadOnlyList<TemplateError> Errors) where T : class
{
    /// <summary>True se il modello è stato creato senza errori.</summary>
    public bool IsValid => Template is not null;
}

/// <summary>Esito di Preview. <see cref="Text"/> è null se il modello non è valido.</summary>
public sealed record TemplatePreview(string? Text, IReadOnlyList<TemplateError> Errors)
{
    /// <summary>True se il modello è valido e <see cref="Text"/> contiene l'anteprima.</summary>
    public bool IsValid => Text is not null;
}
