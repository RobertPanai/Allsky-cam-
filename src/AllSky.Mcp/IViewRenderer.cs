namespace AllSky.Mcp;

/// <summary>
/// Render di una vista come PNG, per il tool <c>view_png</c>. L'implementazione reale (WPF, P17.3) non sta in questo
/// progetto: l'Host passa null finche' non esiste, e il tool risponde «rendering non disponibile».
/// </summary>
public interface IViewRenderer
{
    /// <summary>
    /// Restituisce il PNG della vista. <paramref name="view"/> e' uno di <see cref="McpViews.Names"/>, <paramref name="theme"/>
    /// e' uno di <see cref="McpViews.Themes"/>. Null se il render non e' disponibile per quella vista.
    /// </summary>
    Task<byte[]?> RenderAsync(string view, string theme, CancellationToken cancellationToken);
}

/// <summary>Elenchi chiusi degli argomenti di <c>view_png</c>. Le viste sono quelle di P17.3 del piano.</summary>
public static class McpViews
{
    /// <summary>Nomi ammessi per la vista. Elenco chiuso: nessun nome libero.</summary>
    public static readonly IReadOnlyList<string> Names =
        ["mirino", "grafico-altezza", "distanza-luna", "stato-park", "diagnostica"];

    /// <summary>Temi ammessi: normale e rosso notte.</summary>
    public static readonly IReadOnlyList<string> Themes = ["normal", "nightRed"];
}
