using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

/// <summary>Richiesta registrata dallo handler a script. Il corpo e' riportato solo per la risorsa di inizializzazione.</summary>
internal sealed record SentRequest(
    string Method,
    string Url,
    string? Authorization,
    string? ContentRange,
    int ContentLength,
    string? UploadLength,
    string? UploadType,
    string? Body);

/// <summary>
/// Handler HTTP a script: ogni richiesta consuma la risposta successiva. Se lo script e' vuoto la richiesta fallisce,
/// così un test verifica anche che non parta nessuna chiamata di rete imprevista.
/// </summary>
internal sealed class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _script = new();

    public List<SentRequest> Sent { get; } = new();

    public int Count(string method) => Sent.Count(r => r.Method == method);

    public void Reply(int status, string body = "", params (string Name, string Value)[] headers)
    {
        _script.Enqueue(() =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            foreach ((string name, string value) in headers)
                response.Headers.TryAddWithoutValidation(name, value);
            return response;
        });
    }

    /// <summary>Risposta di inizializzazione con URI di sessione nell'header Location.</summary>
    public void ReplyLocation(string sessionUrl = "https://upload.example/sessione?upload_id=SECRET-ABC")
        => Reply(200, "", ("Location", sessionUrl));

    /// <summary>Simula un'interruzione del processo durante la richiesta.</summary>
    public void Crash() => _script.Enqueue(() => throw new SimulatedCrashException());

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] bytes = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        string method = request.Method.Method;
        string? body = method == "POST" ? Encoding.UTF8.GetString(bytes) : null;
        Sent.Add(new SentRequest(
            method,
            request.RequestUri!.ToString(),
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentRange?.ToString(),
            bytes.Length,
            FirstHeader(request, "X-Upload-Content-Length"),
            FirstHeader(request, "X-Upload-Content-Type"),
            body));

        if (_script.Count == 0)
            throw new InvalidOperationException($"Richiesta di rete inattesa nel test: {method} {request.RequestUri!.AbsolutePath}");
        return _script.Dequeue()();
    }

    private static string? FirstHeader(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
}

/// <summary>Interruzione simulata del processo: non e' transitoria, quindi risale senza essere ritentata.</summary>
internal sealed class SimulatedCrashException : Exception
{
    public SimulatedCrashException() : base("Arresto simulato.") { }
}

/// <summary>Orologio modificabile per i test.</summary>
internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }
}

/// <summary>Fornitore di token finto. Il token cambia a ogni rinnovo (tok-1, tok-2, ...).</summary>
internal sealed class FakeTokens : IAccessTokenSource
{
    public int Refreshes { get; private set; }

    public bool RefreshFails { get; set; }

    public Task<OAuthSecret> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (forceRefresh)
        {
            Refreshes++;
            if (RefreshFails)
                throw new OAuthException("invalid_grant", "Google ha rifiutato il refresh token.", 400);
        }

        return Task.FromResult(new OAuthSecret("tok-" + (Refreshes + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}

/// <summary>Fonte del timelapse finta: il test imposta la notte da restituire.</summary>
internal sealed class FakeTimelapseProvider : ILatestTimelapseProvider
{
    public LatestTimelapse? Latest { get; set; }

    public LatestTimelapse? GetLatestCompleted() => Latest;
}

/// <summary>
/// Ambiente di test del caricamento: cartella temporanea, file MP4 di 600.000 byte (3 chunk da 256 KB),
/// registro su file, handler a script e orologio. <see cref="Restart"/> ricarica registro e pianificatore da disco,
/// come un riavvio del programma.
/// </summary>
internal sealed class UploadHarness : IDisposable
{
    /// <summary>Fuso fisso UTC+2 (ora legale): evita dipendenze dal tzdata del sistema.</summary>
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone(
        "UTC+02", TimeSpan.FromHours(2), "UTC+02", "UTC+02");

    public const int FileBytes = 600_000;
    public const int ChunkBytes = 256 * 1024;

    public UploadHarness()
    {
        Dir = new TempDir();
        File.WriteAllBytes(Dir.File("2026-10-21.mp4"), new byte[FileBytes]);
        Clock = new TestClock { UtcNow = Local(2026, 10, 22, 7, 0) };
        Provider.Latest = NightAt();
        Restart();
    }

    public TempDir Dir { get; }

    public ScriptedHandler Handler { get; } = new();

    public FakeTokens Tokens { get; } = new();

    public FakeProtector Protector { get; } = new();

    public FakeTimelapseProvider Provider { get; } = new();

    public TestClock Clock { get; }

    public List<TimeSpan> Delays { get; } = new();

    public YouTubeUploadSettings Settings { get; set; } = YouTubeUploadSettings.Defaults;

    public UploadJournal Journal { get; private set; } = null!;

    public NightUploader Uploader { get; private set; } = null!;

    public YouTubeUploadScheduler Scheduler { get; private set; } = null!;

    public string JournalPath => Dir.File("youtube-uploads.json");

    public static DateTimeOffset Local(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(2));

    /// <summary>Notte di test: il file MP4 e' quello creato nella cartella di prova.</summary>
    public LatestTimelapse NightAt(string id = "2026-10-21") => new(
        id,
        new DateTime(2026, 10, 21, 20, 0, 0),
        Dir.File("2026-10-21.mp4"),
        300,
        TimeSpan.FromSeconds(12),
        1,
        0);

    /// <summary>Script di un caricamento completo: sessione, due chunk parziali, completamento con videoId.</summary>
    public void ScriptFullUpload(string videoId = "vid-1")
    {
        Handler.ReplyLocation();
        Handler.Reply(308, "", ("Range", "bytes=0-262143"));
        Handler.Reply(308, "", ("Range", "bytes=0-524287"));
        Handler.Reply(201, $"{{\"id\":\"{videoId}\"}}");
    }

    /// <summary>Ricarica registro, client, caricatore e pianificatore dal disco. Simula il riavvio.</summary>
    public void Restart()
    {
        Journal = new UploadJournal(JournalPath, Protector);
        var client = new ResumableUploadClient(new HttpClient(Handler), Tokens, ChunkBytes);
        Uploader = new NightUploader(
            client,
            Journal,
            Clock,
            Zone,
            RetryPolicy.Default,
            (wait, _) =>
            {
                Delays.Add(wait);
                return Task.CompletedTask;
            });
        Scheduler = new YouTubeUploadScheduler(Clock, Zone, Provider, Uploader, Journal, () => Settings);
    }

    public void Dispose() => Dir.Dispose();
}

/// <summary>Costruttori di comodo per i record di stato usati dai test.</summary>
internal static class UploadTestRecords
{
    public static readonly DateTimeOffset Created = new(2026, 10, 22, 5, 0, 0, TimeSpan.Zero);

    public static NightUploadRecord Record(string nightId, NightUploadState state, OAuthSecret session = default) =>
        new(nightId, state, "2026-10-21.mp4", UploadHarness.FileBytes, session, null, 0, null, null, Created);

    /// <summary>Sostituisce i record del registro, mantenendo i contatori del giorno.</summary>
    public static void SetRecords(UploadJournal journal, params NightUploadRecord[] records) =>
        journal.Update(s => new UploadJournalState(
            records.ToDictionary(r => r.NightId, StringComparer.Ordinal),
            s.LastSlotDate,
            s.StartsDate,
            s.StartsCount,
            s.QuotaBlockedUntilUtc));

    /// <summary>Fa partire un caricamento che si interrompe dopo il primo chunk (arresto simulato).</summary>
    public static async Task CrashAfterFirstChunkAsync(UploadHarness h)
    {
        h.Handler.ReplyLocation();
        h.Handler.Reply(308, "", ("Range", "bytes=0-262143"));
        h.Handler.Crash();
        await Assert.ThrowsAsync<SimulatedCrashException>(() => h.Uploader.StartAsync(h.NightAt(), h.Settings, CancellationToken.None));
    }
}
