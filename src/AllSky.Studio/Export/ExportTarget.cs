namespace AllSky.Studio;

/// <summary>
/// Una esportazione richiesta dal progetto: un preset e una destinazione. Il progetto ne contiene un elenco
/// (campo JSON <c>exports</c>); ogni voce diventa un lavoro della coda con <see cref="ExportJobFactory"/>.
/// </summary>
public sealed record ExportTarget
{
    /// <summary>Preset di risoluzione, frame rate e qualità.</summary>
    public ExportPreset Preset { get; init; } = ExportPresets.Originale;

    /// <summary>Dove finisce il file. Null è possibile solo in un JSON guasto: la validazione lo segnala.</summary>
    public ExportDestination? Destination { get; init; }
}
