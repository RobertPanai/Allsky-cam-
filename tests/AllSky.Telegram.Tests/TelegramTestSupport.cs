using System.Net;
using System.Text;
using AllSky.Core;
using AllSky.Publish;
using Xunit;

namespace AllSky.Telegram.Tests;

/// <summary>Token finto: contiene una stringa riconoscibile per la ricerca di fughe nei test. Non e' un token reale.</summary>
internal static class FakeToken
{
    public const string Value = "123456789:TOKENFINTO-7f3a9c_test";

    /// <summary>Frammento da cercare: deve comparire in nessun messaggio, log o ToString.</summary>
    public const string Marker = "TOKENFINTO";
}

/// <summary>Richiesta registrata dal gestore finto. Body: byte grezzi letti come Latin-1 (il multipart e' testo ASCII piu' binario).</summary>
internal sealed record RecordedRequest(string Method, string Path, string FullUrl, string Body);

/// <summary>
/// Gestore HTTP a script: ogni richiesta consuma la risposta successiva. Se lo script e' vuoto la richiesta fallisce,
/// cosi' un test vede anche una chiamata di rete imprevista (il conteggio di <see cref="Requests"/> lo mostra).
/// </summary>
internal sealed class ScriptedTelegramHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _script = new();

    public List<RecordedRequest> Requests { get; } = new();

    public int Count => Requests.Count;

    public void Reply(int status, string body = "{\"ok\":false}")
        => _script.Enqueue(_ => Json(status, body));

    public void Ok(string result = "{\"message_id\":1}")
        => Reply(200, "{\"ok\":true,\"result\":" + result + "}");

    public void Throw(Exception ex) => _script.Enqueue(_ => throw ex);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = string.Empty;
        if (request.Content is not null)
        {
            byte[] bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            body = Encoding.Latin1.GetString(bytes);
        }

        Requests.Add(new RecordedRequest(
            request.Method.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri.ToString(),
            body));

        if (_script.Count == 0)
            throw new InvalidOperationException("Richiesta di rete inattesa nel test.");
        return _script.Dequeue()(request);
    }

    private static HttpResponseMessage Json(int status, string body) => new((HttpStatusCode)status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}

/// <summary>Orologio modificabile.</summary>
internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan delta) => UtcNow += delta;
}

/// <summary>Attesa finta: registra la durata e fa avanzare l'orologio, senza dormire davvero.</summary>
internal sealed class FakeDelay
{
    private readonly FakeClock _clock;

    public FakeDelay(FakeClock clock) => _clock = clock;

    public List<TimeSpan> Delays { get; } = new();

    public Task Run(TimeSpan delay, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Delays.Add(delay);
        _clock.Advance(delay);
        return Task.CompletedTask;
    }
}

/// <summary>Protettore finto reversibile (XOR con prefisso). Puo' simulare la protezione non disponibile.</summary>
internal sealed class ReversibleProtector : ISecretProtector
{
    private static readonly byte[] Prefix = Encoding.ASCII.GetBytes("TGF1:");

    public bool NotSupported { get; set; }

    public byte[] Protect(byte[] plain)
    {
        if (NotSupported)
            throw new PlatformNotSupportedException("Protezione non disponibile nel test.");

        var output = new byte[Prefix.Length + plain.Length];
        Array.Copy(Prefix, output, Prefix.Length);
        for (int i = 0; i < plain.Length; i++)
            output[Prefix.Length + i] = (byte)(plain[i] ^ 0x5A);
        return output;
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        if (protectedData.Length < Prefix.Length || !protectedData.AsSpan(0, Prefix.Length).SequenceEqual(Prefix))
            throw new InvalidDataException("Dati cifrati non riconosciuti.");

        var output = new byte[protectedData.Length - Prefix.Length];
        for (int i = 0; i < output.Length; i++)
            output[i] = (byte)(protectedData[Prefix.Length + i] ^ 0x5A);
        return output;
    }
}

/// <summary>Cartella temporanea per i test dello store, rimossa alla fine.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "allsky-telegram-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name) => Path.Combine(Root, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}

/// <summary>Insieme di componenti collegati, con orologio, attesa, gestore finto e registro in memoria.</summary>
internal sealed class TelegramRig
{
    public TelegramRig(int dailyLimit = SendQueue.DefaultDailyLimit, Secret? token = null)
    {
        Clock = new FakeClock();
        Delay = new FakeDelay(Clock);
        Handler = new ScriptedTelegramHandler();
        Log = new TelegramLog(LogLines.Add, new Redactor(() => new[] { FakeToken.Value }));
        Client = new TelegramClient(new HttpClient(Handler), token ?? new Secret(FakeToken.Value));
        Queue = new SendQueue(Clock, Delay.Run, Log, dailyLimit);
    }

    public FakeClock Clock { get; }

    public FakeDelay Delay { get; }

    public ScriptedTelegramHandler Handler { get; }

    public List<string> LogLines { get; } = new();

    public TelegramLog Log { get; }

    public TelegramClient Client { get; }

    public SendQueue Queue { get; }

    public TelegramSender Sender(TelegramSettings settings) => new(settings, Client, Queue, Log);

    public static TelegramSettings Settings(params long[] chatIds) => new()
    {
        Enabled = true,
        BotToken = new Secret(FakeToken.Value),
        AuthorizedChatIds = chatIds,
    };

    /// <summary>Foto finta: il contenuto non conta, conta la dimensione dichiarata.</summary>
    public static byte[] Bytes(int count) => new byte[count];
}
