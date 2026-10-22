using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AllSky.Publish;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AllSky.Host;

/// <summary>
/// Endpoint <c>/api/publish/*</c>: impostazioni, stato e prova di connessione. Il token e il valore dell'header extra
/// non escono mai da qui: si vede solo se sono impostati (<c>hasToken</c>, <c>hasExtraHeaderValue</c>).
/// Scrittura e prova sono ammesse solo dal computer locale (indirizzo di loopback); la lettura e' sempre consentita.
/// Errori di campo: 200 con <c>{ok:false, errors:[{path,message}]}</c>, come <c>PUT /api/config</c>.
/// Errori di formato della richiesta: 400 con <c>{error}</c>.
/// </summary>
internal static class PublishRoutes
{
    private const int MaxBodyBytes = 64 * 1024;
    private const string LocalOnlyMessage =
        "Le impostazioni di pubblicazione si cambiano solo da questo computer. Aprire la UI da questa macchina.";
    private const string IsoFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>Registra gli endpoint della pubblicazione.</summary>
    /// <param name="app">Applicazione web.</param>
    /// <param name="host">Dipendenze condivise.</param>
    public static void Map(WebApplication app, HostContext host)
    {
        app.MapGet("/api/publish/settings", () => Results.Json(SettingsView(host.Publishing?.Current ?? new PublishSettings()), HostJson.Options));

        app.MapPut("/api/publish/settings", async (HttpContext context) =>
        {
            PublishSettingsBody body = await ReadBodyAsync(context.Request, context.RequestAborted);
            PublishReply reply = SaveSettings(host, context.Connection.RemoteIpAddress, body);
            return Results.Json(reply.Body, HostJson.Options, statusCode: reply.Status);
        });

        app.MapGet("/api/publish/status", () => Results.Json(StatusView(host), HostJson.Options));

        app.MapPost("/api/publish/test", async (HttpContext context) =>
        {
            PublishSettingsBody body = await ReadBodyAsync(context.Request, context.RequestAborted);
            PublishReply reply = await TestSettingsAsync(host, context.Connection.RemoteIpAddress, body, context.RequestAborted);
            return Results.Json(reply.Body, HostJson.Options, statusCode: reply.Status);
        });
    }

    /// <summary>Risposta di un endpoint: codice HTTP e corpo JSON.</summary>
    /// <param name="Status">Codice HTTP.</param>
    /// <param name="Body">Corpo serializzato con <see cref="HostJson"/>.</param>
    internal sealed record PublishReply(int Status, object Body);

    /// <summary>
    /// Corpo di PUT e POST /api/publish/*. Tutti i campi sono facoltativi: un campo assente o null mantiene il valore corrente.
    /// I segreti (token, valore dell'header) sono inviati solo se digitati: una stringa vuota non cambia nulla.
    /// </summary>
    internal sealed record PublishSettingsBody
    {
        public bool? Enabled { get; init; }
        public string? ServerUrl { get; init; }
        public string? DeviceId { get; init; }
        public string? ProfileName { get; init; }
        public int? IntervalSeconds { get; init; }
        public int? LongEdgePx { get; init; }
        public int? JpegQuality { get; init; }
        public string? ExtraHeaderName { get; init; }
        public string? Token { get; init; }
        public bool? ClearToken { get; init; }
        public string? ExtraHeaderValue { get; init; }
        public bool? ClearExtraHeaderValue { get; init; }
    }

    /// <summary>Vista delle impostazioni per la UI: nessun segreto, solo i booleani di presenza.</summary>
    internal sealed record PublishSettingsView(
        bool Enabled,
        string ServerUrl,
        string DeviceId,
        bool HasToken,
        string? ProfileName,
        int IntervalSeconds,
        int LongEdgePx,
        int JpegQuality,
        string? ExtraHeaderName,
        bool HasExtraHeaderValue);

    /// <summary>Vista dello stato per la UI e per l'evento SSE <c>publish</c>. Le date sono UTC con millisecondi.</summary>
    internal sealed record PublishStatusView(
        string State,
        string? LastSuccessUtc,
        string? LastErrorCode,
        string? LastError,
        string? LastErrorUtc,
        int QueueCount,
        int SentTotal,
        int DiscardedTotal,
        string? NextAttemptUtc);

    /// <summary>Legge il corpo JSON delle impostazioni. Il messaggio d'errore non riporta il contenuto della richiesta.</summary>
    internal static async Task<PublishSettingsBody> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is > MaxBodyBytes)
        {
            throw new ArgumentException("Le impostazioni di pubblicazione sono troppo grandi. Controllare i dati inviati.");
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string text = await reader.ReadToEndAsync(ct);
        if (text.Length > MaxBodyBytes)
        {
            throw new ArgumentException("Le impostazioni di pubblicazione sono troppo grandi. Controllare i dati inviati.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new PublishSettingsBody();
        }

        try
        {
            return JsonSerializer.Deserialize<PublishSettingsBody>(text, HostJson.Options) ?? new PublishSettingsBody();
        }
        catch (JsonException)
        {
            throw new ArgumentException("Le impostazioni non sono un JSON valido. Riprovare dalla pagina Pubblicazione.");
        }
    }

    /// <summary>Vero se la richiesta arriva dal computer locale. Un indirizzo assente conta come non locale.</summary>
    internal static bool IsLocalRequest(IPAddress? remote) => remote is not null && IPAddress.IsLoopback(remote);

    /// <summary>Valida, salva e applica le impostazioni. Gli errori di campo non salvano nulla.</summary>
    internal static PublishReply SaveSettings(HostContext host, IPAddress? remote, PublishSettingsBody body)
    {
        if (!IsLocalRequest(remote))
        {
            return Refused();
        }

        PublishSettingsOperations.SaveOutcome outcome = PublishSettingsOperations.Save(host.Publishing, body);
        return outcome.Ok
            ? new PublishReply(200, new { ok = true })
            : new PublishReply(200, new { ok = false, errors = outcome.Errors });
    }

    /// <summary>
    /// Prova di connessione con i valori del form (anche non salvati). I segreti non digitati prendono quelli salvati.
    /// Non modifica le impostazioni ne' lo stato del publisher.
    /// </summary>
    internal static async Task<PublishReply> TestSettingsAsync(HostContext host, IPAddress? remote, PublishSettingsBody body, CancellationToken ct)
    {
        if (!IsLocalRequest(remote))
        {
            return Refused();
        }

        PublishSettingsOperations.TestOutcome outcome = await PublishSettingsOperations.TestAsync(host.Publishing, body, ct);
        return new PublishReply(200, new { ok = outcome.Ok, code = outcome.Code, message = outcome.Message, statusCode = outcome.StatusCode });
    }

    /// <summary>Impostazioni visibili alla UI.</summary>
    internal static PublishSettingsView SettingsView(PublishSettings s) => new(
        Enabled: s.Enabled,
        ServerUrl: s.ServerUrl,
        DeviceId: s.DeviceId,
        HasToken: !s.Token.IsEmpty,
        ProfileName: s.ProfileName,
        IntervalSeconds: s.IntervalSeconds,
        LongEdgePx: s.LongEdgePx,
        JpegQuality: s.JpegQuality,
        ExtraHeaderName: s.ExtraHeaderName,
        HasExtraHeaderValue: !s.ExtraHeaderValue.IsEmpty);

    /// <summary>Stato corrente; senza servizi di pubblicazione risponde "disabled".</summary>
    internal static PublishStatusView StatusView(HostContext host)
    {
        PublishServices? services = host.Publishing;
        return services is null
            ? new PublishStatusView("disabled", null, null, null, null, 0, 0, 0, null)
            : ViewOf(services.Publisher.GetStatus(), services.Redactor);
    }

    /// <summary>Converte lo stato del publisher nella vista per la UI e per l'evento SSE.</summary>
    internal static PublishStatusView ViewOf(PublishStatus status, Redactor redactor) => new(
        State: status.State.ToString().ToLowerInvariant(),
        LastSuccessUtc: Iso(status.LastSuccessUtc),
        LastErrorCode: status.LastErrorCode,
        LastError: PublishSettingsOperations.SanitizeError(status.LastError, redactor),
        LastErrorUtc: Iso(status.LastErrorUtc),
        QueueCount: status.QueueCount,
        SentTotal: status.SentTotal,
        DiscardedTotal: status.DiscardedTotal,
        NextAttemptUtc: Iso(status.NextAttemptUtc));

    private static PublishReply Refused() => new(403, new ErrorBody(LocalOnlyMessage));

    private static string? Iso(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString(IsoFormat, CultureInfo.InvariantCulture);
}
