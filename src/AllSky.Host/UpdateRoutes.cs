using System.Globalization;
using System.Text;
using System.Text.Json;
using AllSky.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace AllSky.Host;

/// <summary>
/// Endpoint <c>/api/update/*</c>: stato dell'aggiornamento, controllo, download, installazione e impostazione del controllo automatico.
/// La lettura dello stato e' sempre consentita. Comandi e impostazione sono ammessi solo dal computer locale, come le impostazioni
/// di pubblicazione. I messaggi d'errore sono testi fissi: non vengono mai copiati da un'eccezione, quindi non portano percorsi ne' segreti.
/// Errori di formato della richiesta: 400 con <c>{error}</c>.
/// </summary>
internal static class UpdateRoutes
{
    private const int MaxBodyBytes = 4 * 1024;
    private const string LocalOnlyMessage =
        "Gli aggiornamenti si gestiscono solo da questo computer. Aprire la UI da questa macchina.";
    private const string InvalidSettingsMessage =
        "L'impostazione degli aggiornamenti non è valida. Controllare i dati inviati.";
    private const string SaveFailedMessage =
        "Impossibile salvare l'impostazione degli aggiornamenti. Controllare i permessi della cartella dati.";
    private const string IsoFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>Registra gli endpoint degli aggiornamenti.</summary>
    /// <param name="app">Applicazione web.</param>
    /// <param name="host">Dipendenze condivise.</param>
    public static void Map(WebApplication app, HostContext host)
    {
        app.MapGet("/api/update/status", () => Results.Json(View(host), HostJson.Options));

        app.MapPost("/api/update/check", async (HttpContext context) =>
        {
            if (!IsLocal(context))
            {
                return Refused();
            }

            UpdateServices? services = host.Updates;
            if (services is null)
            {
                return Results.Json(View(host), HostJson.Options);
            }

            UpdateStatus status = await services.Service.CheckNowAsync(context.RequestAborted);
            return Results.Json(ViewOf(status, services), HostJson.Options);
        });

        app.MapPost("/api/update/download", async (HttpContext context) =>
        {
            if (!IsLocal(context))
            {
                return Refused();
            }

            UpdateServices? services = host.Updates;
            if (services is null)
            {
                return Results.Json(View(host), HostJson.Options);
            }

            // Senza il token della richiesta: il download non si interrompe se il browser viene chiuso a meta'.
            UpdateStatus status = await services.Service.DownloadAsync();
            return Results.Json(ViewOf(status, services), HostJson.Options);
        });

        // Programma l'installazione: l'aggiornamento parte quando AllSky esce, poi l'app si riavvia. Non chiude l'app da solo.
        app.MapPost("/api/update/apply", (HttpContext context) =>
        {
            if (!IsLocal(context))
            {
                return Refused();
            }

            UpdateServices? services = host.Updates;
            if (services is null)
            {
                return Results.Json(View(host), HostJson.Options);
            }

            UpdateStatus status = services.Service.ApplyLater();
            return Results.Json(ViewOf(status, services), HostJson.Options);
        });

        app.MapPut("/api/update/settings", async (HttpContext context) =>
        {
            if (!IsLocal(context))
            {
                return Refused();
            }

            UpdateSettingsBody? body = await ReadBodyAsync(context.Request, context.RequestAborted);
            if (body is null)
            {
                return Results.Json(new ErrorBody(InvalidSettingsMessage), HostJson.Options, statusCode: 400);
            }

            UpdateServices? services = host.Updates;
            if (services is null)
            {
                return Results.Json(View(host), HostJson.Options);
            }

            bool autoCheck = body.AutoCheck ?? services.Service.AutoCheckEnabled;
            try
            {
                services.Store.Save(autoCheck);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                host.Log.Write("Salvataggio dell'impostazione degli aggiornamenti non riuscito", ex);
                return Results.Json(new ErrorBody(SaveFailedMessage), HostJson.Options, statusCode: 500);
            }

            services.Service.SetAutoCheck(autoCheck);
            return Results.Json(ViewOf(services.Service.GetStatus(), services), HostJson.Options);
        });
    }

    /// <summary>Vista dello stato per la UI. Nessun dato sensibile: solo versioni, stato, errore e data dell'ultimo controllo (UTC).</summary>
    internal sealed record UpdateStatusView(
        UpdateState State,
        string CurrentVersion,
        string? AvailableVersion,
        int? ProgressPercent,
        string? ErrorCode,
        string? ErrorMessage,
        string? LastCheckUtc,
        bool AutoCheck);

    private static bool IsLocal(HttpContext context) => PublishRoutes.IsLocalRequest(context.Connection.RemoteIpAddress);

    private static IResult Refused() => Results.Json(new ErrorBody(LocalOnlyMessage), HostJson.Options, statusCode: 403);

    /// <summary>Stato corrente; senza servizio degli aggiornamenti risponde "disabled".</summary>
    private static UpdateStatusView View(HostContext host)
    {
        UpdateServices? services = host.Updates;
        return services is null
            ? new UpdateStatusView(UpdateState.Disabled, string.Empty, null, null, null, null, null, false)
            : ViewOf(services.Service.GetStatus(), services);
    }

    private static UpdateStatusView ViewOf(UpdateStatus status, UpdateServices services) => new(
        State: status.State,
        CurrentVersion: status.CurrentVersion,
        AvailableVersion: status.AvailableVersion,
        ProgressPercent: status.ProgressPercent,
        ErrorCode: status.ErrorCode,
        ErrorMessage: status.ErrorMessage,
        LastCheckUtc: Iso(status.LastCheckUtc),
        AutoCheck: services.Service.AutoCheckEnabled);

    /// <summary>Legge il corpo JSON. Restituisce null se il corpo è troppo grande o non è un JSON valido.</summary>
    private static async Task<UpdateSettingsBody?> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is > MaxBodyBytes)
        {
            return null;
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string text = await reader.ReadToEndAsync(ct);
        if (text.Length > MaxBodyBytes)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new UpdateSettingsBody();
        }

        try
        {
            return JsonSerializer.Deserialize<UpdateSettingsBody>(text, HostJson.Options) ?? new UpdateSettingsBody();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Iso(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString(IsoFormat, CultureInfo.InvariantCulture);
}

/// <summary>Corpo di PUT /api/update/settings e forma di <c>updates.json</c>. Un campo assente mantiene il valore corrente.</summary>
internal sealed record UpdateSettingsBody
{
    public bool? AutoCheck { get; init; }
}

/// <summary>Servizio degli aggiornamenti e il suo archivio, creati dall'Host all'avvio.</summary>
/// <param name="Service">Controllo, download e installazione.</param>
/// <param name="Store">Impostazione del controllo automatico, salvata in <c>updates.json</c>.</param>
internal sealed record UpdateServices(UpdateService Service, UpdateSettingsStore Store)
{
    /// <summary>Nome del file delle impostazioni, nella cartella dati.</summary>
    public const string SettingsFileName = "updates.json";

    /// <summary>Crea il servizio con l'impostazione salvata. Senza backend gli aggiornamenti risultano disabilitati.</summary>
    /// <param name="backend">Sorgente degli aggiornamenti; null per le installazioni non aggiornabili.</param>
    /// <param name="dataFolder">Cartella dati dell'utente.</param>
    public static UpdateServices Create(IUpdateBackend? backend, string dataFolder)
    {
        var store = new UpdateSettingsStore(Path.Combine(dataFolder, SettingsFileName));
        var service = new UpdateService(backend ?? new UnavailableUpdateBackend(), new SystemUpdateClock(), store.Load());
        return new UpdateServices(service, store);
    }
}

/// <summary>
/// Impostazione del controllo automatico in <c>updates.json</c> (<c>{"autoCheck": true}</c>). Predefinito: attivo.
/// File assente o illeggibile: predefinito. File corrotto: spostato in <c>.bad</c>. Scrittura atomica (file .tmp poi spostamento).
/// </summary>
internal sealed class UpdateSettingsStore
{
    private const bool DefaultAutoCheck = true;

    private readonly string _filePath;

    /// <param name="filePath">Percorso di <c>updates.json</c>.</param>
    public UpdateSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        _filePath = filePath;
    }

    /// <summary>Legge il controllo automatico. Non lancia: in caso di problema restituisce il predefinito.</summary>
    public bool Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return DefaultAutoCheck;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_filePath, Encoding.UTF8));
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("autoCheck", out JsonElement value)
                && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False))
            {
                return value.GetBoolean();
            }

            return DefaultAutoCheck;
        }
        catch (JsonException)
        {
            Quarantine();
            return DefaultAutoCheck;
        }
        catch (Exception)
        {
            // Lettura impossibile (file bloccato, permessi): nessuno spostamento, predefinito in memoria.
            return DefaultAutoCheck;
        }
    }

    /// <summary>Salva il controllo automatico in modo atomico.</summary>
    /// <exception cref="IOException">Scrittura o spostamento del file non riusciti.</exception>
    /// <exception cref="UnauthorizedAccessException">Permessi insufficienti sulla cartella.</exception>
    public void Save(bool autoCheck)
    {
        string full = Path.GetFullPath(_filePath);
        string? directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(new UpdateSettingsBody { AutoCheck = autoCheck }, HostJson.Options);
        string temporary = full + ".tmp";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, full, overwrite: true);
        }
        catch (Exception)
        {
            TryDelete(temporary);
            throw;
        }
    }

    private void Quarantine()
    {
        try
        {
            File.Move(_filePath, _filePath + ".bad", overwrite: true);
        }
        catch (Exception)
        {
            // Best effort: se lo spostamento fallisce il file resta com'e'.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
