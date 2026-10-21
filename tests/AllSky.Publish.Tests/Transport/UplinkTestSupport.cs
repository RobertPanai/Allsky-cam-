using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AllSky.Publish.Tests;

/// <summary>Una parte del multipart: header grezzi e contenuto binario.</summary>
public sealed record MultipartPart(string Headers, byte[] Content)
{
    /// <summary>Valore di un header della parte (confronto senza distinzione di maiuscole), o null.</summary>
    public string? Header(string name)
    {
        foreach (string line in Headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && string.Equals(line[..colon].Trim(), name, StringComparison.OrdinalIgnoreCase))
                return line[(colon + 1)..].Trim();
        }
        return null;
    }

    /// <summary>Nome della parte (attributo name di Content-Disposition), o null.</summary>
    /// <remarks>Le virgolette sono facoltative: .NET le omette per i valori che sono token (es. name=meta).</remarks>
    public string? Name => FirstGroup(Header("Content-Disposition"), "(?:^|;)\\s*name=\"?([^\";]*)\"?");

    /// <summary>Nome del file (attributo filename di Content-Disposition), o null.</summary>
    public string? FileName => FirstGroup(Header("Content-Disposition"), "(?:^|;)\\s*filename=\"?([^\";]*)\"?");

    /// <summary>Contenuto come testo UTF-8.</summary>
    public string Text => Encoding.UTF8.GetString(Content);

    private static string? FirstGroup(string? input, string pattern)
    {
        if (input is null)
            return null;
        System.Text.RegularExpressions.Match match = Regex.Match(input, pattern, RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }
}

/// <summary>Strumenti condivisi dei test Uplink: fixture del contratto, impostazioni, richiesta di esempio, parser multipart.</summary>
public static class UplinkTestSupport
{
    /// <summary>Token finto: deve sparire da ogni log, messaggio, ToString e URL.</summary>
    public const string TokenValue = "tok-SECRET-0001";

    /// <summary>Valore finto dell'header extra: trattato come segreto.</summary>
    public const string ExtraValue = "extra-SECRET-0002";

    /// <summary>URL base finto del server.</summary>
    public const string ServerUrl = "https://uplink.test";

    /// <summary>
    /// Percorso di un file in contract/. Cerca salendo prima dalla cartella di questo sorgente, poi da quella di esecuzione,
    /// cosi' il test trova la fixture sia dal repository sia da una cartella di esecuzione diversa.
    /// </summary>
    public static string FixturePath(string fileName, [System.Runtime.CompilerServices.CallerFilePath] string? sourcePath = null)
    {
        var starts = new List<string?>();
        if (sourcePath is not null)
            starts.Add(Path.GetDirectoryName(sourcePath));
        starts.Add(AppContext.BaseDirectory);

        foreach (string? start in starts)
        {
            string? dir = start;
            while (dir is not null)
            {
                string candidate = Path.Combine(dir, "contract", fileName);
                if (File.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir);
            }
        }
        throw new FileNotFoundException("Fixture non trovata: contract/" + fileName);
    }

    /// <summary>Metadati della fixture allsky-v1.meta.json.</summary>
    public static JsonObject LoadFixtureMeta()
        => (JsonObject)JsonNode.Parse(File.ReadAllText(FixturePath("allsky-v1.meta.json")))!;

    /// <summary>Valore stringa di un campo della fixture meta.</summary>
    public static string FixtureString(string name) => LoadFixtureMeta()[name]!.GetValue<string>();

    /// <summary>Istante della fixture (capturedAt), in UTC.</summary>
    public static DateTimeOffset FixtureCapturedAt()
        => DateTimeOffset.Parse(FixtureString("capturedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    /// <summary>Impostazioni valide per i test. Il profilo e l'header extra sono facoltativi.</summary>
    public static PublishSettings Settings(string? profileName = null, string? extraHeaderName = null)
        => new()
        {
            Enabled = true,
            ServerUrl = ServerUrl,
            DeviceId = FixtureString("deviceId"),
            Token = new Secret(TokenValue),
            ProfileName = profileName,
            ExtraHeaderName = extraHeaderName,
            ExtraHeaderValue = new Secret(ExtraValue),
        };

    /// <summary>Redactor con i segreti del test (token e valore dell'header extra).</summary>
    public static Redactor Redactor() => new(() => new[] { TokenValue, ExtraValue });

    /// <summary>Elemento in coda coerente con la fixture (captureId e capturedAt della fixture).</summary>
    public static QueuedItem Item()
        => new(FixtureString("captureId"), FixtureCapturedAt(), 175, 0, PublishMode.Night, null, "frame-test.jpg");

    /// <summary>Byte JPEG finti: contengono anche sequenze CRLF e trattini per verificare che il corpo binario resti intatto.</summary>
    public static byte[] Jpeg() => new byte[] { 0xFF, 0xD8, 0x0D, 0x0A, 0x2D, 0x2D, 0x00, 0xFF, 0xD9 };

    /// <summary>Estrae il boundary dal Content-Type multipart/form-data.</summary>
    public static string? BoundaryOf(string? contentType)
    {
        if (contentType is null)
            return null;
        Match match = Regex.Match(contentType, "boundary=\"?([^\";]+)\"?", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Spezza un corpo multipart sui boundary e restituisce le parti in ordine.</summary>
    public static List<MultipartPart> ParseMultipart(byte[] body, string boundary)
    {
        byte[] marker = Encoding.ASCII.GetBytes("--" + boundary);
        byte[] headerEnd = "\r\n\r\n"u8.ToArray();

        var positions = new List<int>();
        int pos = IndexOf(body, marker, 0);
        while (pos >= 0)
        {
            positions.Add(pos);
            pos = IndexOf(body, marker, pos + marker.Length);
        }

        var parts = new List<MultipartPart>();
        for (int i = 0; i < positions.Count; i++)
        {
            int start = positions[i] + marker.Length;
            int end = i + 1 < positions.Count ? positions[i + 1] : body.Length;

            // "--boundary--" e' la chiusura: non e' una parte.
            if (start + 1 < body.Length && body[start] == '-' && body[start + 1] == '-')
                break;

            // Ogni parte e' preceduta da CRLF (dopo il marker) e seguita da CRLF (prima del marker successivo).
            int segStart = start + 2;
            int segEnd = end - 2;
            if (segEnd < segStart)
                throw new FormatException("Parte multipart malformata.");

            byte[] segment = body[segStart..segEnd];
            int split = IndexOf(segment, headerEnd, 0);
            if (split < 0)
                throw new FormatException("Parte multipart senza separatore header/contenuto.");

            string headers = Encoding.UTF8.GetString(segment, 0, split);
            byte[] content = segment[(split + headerEnd.Length)..];
            parts.Add(new MultipartPart(headers, content));
        }
        return parts;
    }

    /// <summary>Copia di un oggetto JSON senza i campi indicati (usata per il confronto con la fixture).</summary>
    public static JsonObject Without(JsonNode node, params string[] names)
    {
        var copy = (JsonObject)JsonNode.Parse(node.ToJsonString())!;
        foreach (string name in names)
            copy.Remove(name);
        return copy;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (int i = start; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }
}
