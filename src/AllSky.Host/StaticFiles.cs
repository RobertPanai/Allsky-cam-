using Microsoft.AspNetCore.Http;

namespace AllSky.Host;

/// <summary>
/// Serve i file statici della UI dalla cartella <c>web</c>. Blocca la fuga dalla cartella (path traversal),
/// non serve le cartelle di sviluppo (<c>_dev</c>, <c>_shots</c>) ne' il server di prova <c>mock</c>.
/// <para>
/// Nessuna risorsa esterna: la UI e' autosufficiente. Cache-Control "no-cache" su tutti i file, come da
/// configurazione di sviluppo: il browser riconvalida ogni volta e la UI non resta su una versione vecchia.
/// </para>
/// </summary>
internal static class StaticFiles
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".txt"] = "text/plain; charset=utf-8",
    };

    /// <summary>Risponde con il file richiesto, oppure 404 se non e' servibile.</summary>
    /// <param name="context">Richiesta HTTP.</param>
    /// <param name="webRoot">Cartella radice completa (<see cref="Path.GetFullPath(string)"/>), senza separatore finale.</param>
    public static async Task ServeAsync(HttpContext context, string webRoot)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers["Allow"] = "GET, HEAD";
            return;
        }

        string? file = ResolveFile(context.Request.Path.Value, webRoot);
        if (file is null)
        {
            await WriteNotFoundAsync(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = ContentTypes.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream");
        context.Response.Headers["Cache-Control"] = "no-cache";
        await context.Response.SendFileAsync(file);
    }

    /// <summary>Percorso completo del file, oppure null se la richiesta non e' servibile.</summary>
    private static string? ResolveFile(string? requestPath, string webRoot)
    {
        string relative = string.IsNullOrEmpty(requestPath) || requestPath == "/" ? "index.html" : requestPath.TrimStart('/');

        string firstSegment = relative.Split('/', 2)[0];
        if (firstSegment.StartsWith('_') || string.Equals(firstSegment, "mock", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            // Path.GetFullPath risolve ".." e il controllo del prefisso blocca ogni percorso fuori dalla radice.
            string full = Path.GetFullPath(Path.Combine(webRoot, relative));
            string prefix = webRoot + Path.DirectorySeparatorChar;
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(prefix, comparison))
            {
                return null;
            }

            return File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            // Caratteri non validi nel percorso: stessa risposta di un file mancante.
            return null;
        }
    }

    private static Task WriteNotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain; charset=utf-8";
        return context.Response.WriteAsync("Non trovato");
    }
}
