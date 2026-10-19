namespace AllSky.Core;

/// <summary>Errore di validazione: <paramref name="Path"/> è il percorso JSON (es. "site.latitude").</summary>
public sealed record ValidationError(string Path, string Message);
