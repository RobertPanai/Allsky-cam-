using System.Net;
using System.Text;
using AllSky.YouTube;

namespace AllSky.YouTube.Tests;

/// <summary>Protettore finto: inverte i byte e aggiunge un prefisso. Non e' cifratura: serve a verificare il formato su disco.</summary>
internal sealed class FakeProtector : ISecretProtector
{
    private static readonly byte[] Prefix = Encoding.UTF8.GetBytes("FAKE:");

    public bool NotSupported { get; set; }
    public bool FailUnprotect { get; set; }

    public byte[] Protect(byte[] plain)
    {
        if (NotSupported)
            throw new PlatformNotSupportedException("Protezione non disponibile nel test.");
        return Prefix.Concat(Reverse(plain)).ToArray();
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        if (FailUnprotect || protectedData.Length < Prefix.Length)
            throw new InvalidOperationException("Dato non decifrabile nel test.");
        return Reverse(protectedData.Skip(Prefix.Length).ToArray());
    }

    private static byte[] Reverse(byte[] data)
    {
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = data[data.Length - 1 - i];
        return result;
    }
}

/// <summary>Richiesta registrata dall'handler finto.</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Url, string Body);

/// <summary>Handler HTTP finto: risponde sempre con lo stato e il corpo indicati, senza rete.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public FakeHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    public List<RecordedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string requestBody = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, requestBody));
        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>Orologio fisso per i test di scadenza.</summary>
internal sealed class FixedClock : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedClock(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>Cartella temporanea del test, rimossa a fine test.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Root = Path.Combine(Path.GetTempPath(), "allsky-yt-" + Guid.NewGuid().ToString("N"));
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pulizia best effort.
        }
    }
}

/// <summary>Parsing di query string e corpi application/x-www-form-urlencoded, per le asserzioni.</summary>
internal static class QueryParser
{
    public static Dictionary<string, string> ParseQuery(string query) => Parse(query.TrimStart('?'));

    public static Dictionary<string, string> ParseForm(string body) => Parse(body);

    private static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string name = eq < 0 ? pair : pair[..eq];
            string value = eq < 0 ? string.Empty : pair[(eq + 1)..];
            result[Decode(name)] = Decode(value);
        }

        return result;
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}

/// <summary>Client HTTP verso il loopback del test: nessun proxy di sistema.</summary>
internal static class LocalHttp
{
    public static HttpClient Create() => new(new HttpClientHandler { UseProxy = false });
}
