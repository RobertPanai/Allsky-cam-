using System.Globalization;
using System.Text;
using System.Text.Json;
using AllSky.Archive;
using AllSky.Core;
using AllSky.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AllSky.Host;

/// <summary>
/// Endpoint di <c>/api</c> secondo docs/API.md. Errori: <c>{ "error": "..." }</c> con codice 4xx/5xx.
/// Eccezione deliberata: <c>PUT /api/config</c> con valori non validi risponde 200 con <c>{ ok:false, errors }</c>,
/// perche' la UI legge gli errori di campo da quella risposta (vedi web/js/views/site.js) e un 4xx li nasconderebbe.
/// </summary>
internal static class ApiRoutes
{
    private const int DefaultFrameLimit = 200;
    private const int MaxFrameLimit = 1000;
    private const int MaxBodyBytes = 1024 * 1024;

    /// <summary>Registra tutti gli endpoint dell'API.</summary>
    /// <param name="app">Applicazione web.</param>
    /// <param name="host">Dipendenze condivise.</param>
    public static void Map(WebApplication app, HostContext host)
    {
        IAllSkyService engine = host.Engine;

        app.MapGet("/api/status", () => Json(engine.GetStatus()));

        app.MapGet("/api/events", (HttpContext context) => EventStream.ServeAsync(context, engine, host.Stopping));

        app.MapGet("/api/frame/latest.jpg", (HttpContext context) =>
        {
            bool raw = string.Equals(context.Request.Query["stage"], "raw", StringComparison.Ordinal);
            byte[]? jpeg = engine.GetLatestJpeg(raw);
            if (jpeg is null)
            {
                return Fail(404, "Nessun frame ancora disponibile: attendere il primo scatto della camera, poi riprovare.");
            }

            context.Response.Headers["Cache-Control"] = "no-store";
            return Results.Bytes(jpeg, "image/jpeg");
        });

        app.MapGet("/api/cameras/discover", async (HttpContext context) =>
        {
            IReadOnlyList<CameraCandidate> cameras = await engine.DiscoverCamerasAsync(context.RequestAborted);
            return Json(cameras);
        });

        app.MapPost("/api/camera/connect", async (HttpContext context) =>
        {
            ConnectRequest? body = await ReadJsonAsync<ConnectRequest>(context.Request, context.RequestAborted);
            if (string.IsNullOrWhiteSpace(body?.Id))
            {
                throw new ArgumentException("Indicare la camera da collegare nel campo 'id'.");
            }

            ConnectResult result = await engine.ConnectCameraAsync(body.Id, context.RequestAborted);
            return result.Ok
                ? Json(new { ok = true })
                : Json(new { ok = false, error = result.Error });
        });

        app.MapGet("/api/config", () => Json(engine.GetConfig()));

        app.MapPut("/api/config", async (HttpContext context) =>
        {
            string text = await ReadBodyAsync(context.Request, context.RequestAborted);
            try
            {
                JsonDocument.Parse(text).Dispose();
            }
            catch (JsonException)
            {
                return Fail(400, "La configurazione non è un JSON valido. Controlla le modifiche e riprova.");
            }

            AppConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<AppConfig>(text, ConfigStore.SerializerOptions);
            }
            catch (JsonException ex)
            {
                // JSON corretto ma con un valore di tipo sbagliato o non riconosciuto (per esempio un tema inesistente):
                // e' un errore di campo, con il percorso per la UI.
                string path = ex.Path is null ? string.Empty : TrimJsonPath(ex.Path);
                return Json(new ConfigResult(false, [new ValidationError(path, "Valore non riconosciuto: correggere il campo e salvare di nuovo.")]));
            }

            if (config is null)
            {
                return Fail(400, "La configurazione è vuota. Ricarica la pagina Impostazioni e riprova.");
            }

            ConfigUpdateResult result = engine.UpdateConfig(config);
            return Json(new ConfigResult(result.Ok, result.Errors));
        });

        app.MapGet("/api/nights", () => Json(engine.ListNights()));

        app.MapGet("/api/nights/{nightId}/frames", (string nightId, HttpContext context) =>
        {
            int limit = ParseQueryInt(context.Request.Query, "limit", DefaultFrameLimit, 1, MaxFrameLimit);
            int offset = ParseQueryInt(context.Request.Query, "offset", 0, 0, int.MaxValue);
            IReadOnlyList<FrameDto> frames = engine.ListFrames(nightId, offset, limit);
            if (frames.Count == 0 && !NightExists(engine, nightId))
            {
                return NightNotFound(nightId);
            }

            return Json(frames);
        });

        app.MapGet("/api/nights/{nightId}/frames/{frameFile}", (string nightId, string frameFile, HttpContext context) =>
        {
            string? frameId = StripJpgExtension(frameFile);
            if (frameId is null)
            {
                return Fail(404, "Frame non trovato: il nome del file deve terminare con .jpg.");
            }

            return ServeFile(context, engine.GetFramePath(nightId, frameId), "image/jpeg",
                "Frame non trovato: la notte o il frame non esistono più. Scegliere un'altro frame dall'elenco.",
                "public, max-age=3600", enableRange: false);
        });

        app.MapGet("/api/nights/{nightId}/thumb/{frameFile}", (string nightId, string frameFile, HttpContext context) =>
        {
            string? frameId = StripJpgExtension(frameFile);
            if (frameId is null)
            {
                return Fail(404, "Miniatura non trovata: il nome del file deve terminare con .jpg.");
            }

            return ServeFile(context, engine.GetThumbPath(nightId, frameId), "image/jpeg",
                "Miniatura non trovata: la notte o il frame non esistono più.",
                "public, max-age=3600", enableRange: false);
        });

        app.MapGet("/api/nights/{nightId}/keogram.jpg", (string nightId, HttpContext context) =>
            ServeFile(context, engine.GetProductPath(nightId, NightProduct.Keogram), "image/jpeg",
                "Il keogram di questa notte non è ancora pronto: viene aggiornato man mano che arrivano i frame.",
                "no-cache", enableRange: false));

        app.MapGet("/api/nights/{nightId}/startrails.jpg", (string nightId, HttpContext context) =>
            ServeFile(context, engine.GetProductPath(nightId, NightProduct.Startrails), "image/jpeg",
                "Le startrails di questa notte non sono ancora pronte: vengono aggiornate man mano che arrivano i frame.",
                "no-cache", enableRange: false));

        app.MapGet("/api/nights/{nightId}/timelapse.mp4", (string nightId, HttpContext context) =>
            ServeFile(context, engine.GetProductPath(nightId, NightProduct.Timelapse), "video/mp4",
                "Il timelapse di questa notte non è ancora pronto. Avvialo dalla scheda Timelapse.",
                "no-cache", enableRange: true));

        app.MapPost("/api/nights/{nightId}/timelapse", async (string nightId, HttpContext context) =>
        {
            CheckNightId(nightId);
            if (!NightExists(engine, nightId))
            {
                return NightNotFound(nightId);
            }

            bool queued = await engine.RequestTimelapseAsync(nightId);
            return queued
                ? Json(new { ok = true })
                : Fail(409, "Il timelapse non può essere avviato: la notte è già in coda o in lavorazione, oppure non ha frame.");
        });

        app.MapDelete("/api/nights/{nightId}", (string nightId) =>
        {
            CheckNightId(nightId);
            try
            {
                return engine.DeleteNight(nightId)
                    ? Json(new { ok = true })
                    : NightNotFound(nightId);
            }
            catch (EngineException ex)
            {
                return Fail(409, ex.Message);
            }
        });
    }

    /// <summary>Corpo di <c>POST /api/camera/connect</c>.</summary>
    /// <param name="Id">Identificativo della camera scelta dall'elenco di discover.</param>
    private sealed record ConnectRequest(string? Id);

    /// <summary>Esito di <c>PUT /api/config</c>.</summary>
    /// <param name="Ok">Vero se la configurazione e' stata salvata e applicata.</param>
    /// <param name="Errors">Errori di validazione con percorso e messaggio; vuoto se Ok.</param>
    private sealed record ConfigResult(bool Ok, IReadOnlyList<ValidationError> Errors);

    private static IResult Json(object? value)
    {
        return Results.Json(value, HostJson.Options);
    }

    /// <summary>Risposta d'errore uniforme, con il codice HTTP indicato.</summary>
    internal static IResult Fail(int status, string message)
    {
        return Results.Json(new ErrorBody(message), HostJson.Options, statusCode: status);
    }

    private static IResult NightNotFound(string nightId)
    {
        return Fail(404, $"Notte {nightId} non trovata. Scegliere un'altra notte dall'elenco.");
    }

    /// <summary>Serve un file se esiste, altrimenti 404 con il messaggio dato.</summary>
    private static IResult ServeFile(HttpContext context, string path, string contentType, string notFound, string cacheControl, bool enableRange)
    {
        if (!File.Exists(path))
        {
            return Fail(404, notFound);
        }

        context.Response.Headers["Cache-Control"] = cacheControl;
        return Results.File(path, contentType, enableRangeProcessing: enableRange);
    }

    /// <summary>Vero se la notte compare nell'elenco dell'archivio.</summary>
    private static bool NightExists(IAllSkyService engine, string nightId)
    {
        return engine.ListNights().Any(n => string.Equals(n.Id, nightId, StringComparison.Ordinal));
    }

    /// <summary>Verifica il formato YYYY-MM-DD prima di usare l'id in un'operazione di scrittura.</summary>
    private static void CheckNightId(string nightId)
    {
        if (!NightArchive.IsValidNightId(nightId))
        {
            throw new ArgumentException("Identificatore di notte non valido: atteso YYYY-MM-DD.");
        }
    }

    /// <summary>"frame.jpg" diventa "frame"; null se l'estensione non e' .jpg.</summary>
    private static string? StripJpgExtension(string file)
    {
        const string extension = ".jpg";
        if (!file.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || file.Length == extension.Length)
        {
            return null;
        }

        return file[..^extension.Length];
    }

    private static int ParseQueryInt(IQueryCollection query, string name, int defaultValue, int min, int max)
    {
        string? raw = query[name];
        if (string.IsNullOrEmpty(raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
        {
            throw new ArgumentException($"Il parametro '{name}' deve essere un numero intero da {min} a {max}.");
        }

        return value;
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is > MaxBodyBytes)
        {
            throw new ArgumentException("Il corpo della richiesta è troppo grande. Controllare i dati inviati.");
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpRequest request, CancellationToken ct)
    {
        string text = await ReadBodyAsync(request, ct);
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, HostJson.Options);
        }
        catch (JsonException)
        {
            throw new ArgumentException("La richiesta non è un JSON valido. Riprovare dalla pagina Camera.");
        }
    }

    /// <summary>"$.ui.theme" diventa "ui.theme".</summary>
    private static string TrimJsonPath(string path)
    {
        return path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path.TrimStart('$');
    }
}
