using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AllSky.Nina;

/// <summary>
/// Cronologia degli eventi NINA di una notte: <c>nights/&lt;notte&gt;/nina-events.jsonl</c>, una riga JSON per evento,
/// in UTC. La scrittura non lancia mai: un guasto su disco non deve fermare la cattura.
/// Ogni riga viene scritta con una sola chiamata di scrittura (append). Le funzioni sono sincrone e brevi.
/// </summary>
public sealed class NinaEventLog
{
    /// <summary>Nome del file della notte.</summary>
    public const string FileName = "nina-events.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Action<string, Exception?>? _log;

    /// <summary>Apre la cronologia nel file indicato. Il file e le cartelle si creano alla prima scrittura.</summary>
    /// <param name="filePath">Percorso del file JSONL.</param>
    /// <param name="log">Registro opzionale per i guasti di scrittura o lettura.</param>
    public NinaEventLog(string filePath, Action<string, Exception?>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _path = Path.GetFullPath(filePath);
        _log = log;
    }

    /// <summary>Percorso del file.</summary>
    public string FilePath => _path;

    /// <summary>
    /// Percorso del file della notte sotto la cartella dell'archivio: <c>&lt;archivio&gt;/nights/&lt;notte&gt;/nina-events.jsonl</c>.
    /// </summary>
    /// <exception cref="ArgumentException">Identificatore di notte non valido ("YYYY-MM-DD" con data reale).</exception>
    public static string PathForNight(string archiveRoot, string nightId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archiveRoot);
        if (!IsValidNightId(nightId))
        {
            throw new ArgumentException("L'identificatore di notte deve essere \"YYYY-MM-DD\" con una data reale.", nameof(nightId));
        }

        return Path.Combine(Path.GetFullPath(archiveRoot), "nights", nightId, FileName);
    }

    /// <summary>
    /// Aggiunge un evento in fondo al file. Restituisce false (e registra) se la scrittura fallisce; non lancia mai.
    /// Due eventi con lo stesso istante restano entrambi: l'ordine e' quello di scrittura.
    /// </summary>
    public bool TryAppend(NinaEvent? ev)
    {
        if (ev is null)
        {
            return false;
        }

        try
        {
            var riga = new NinaEventoRiga
            {
                AtUtc = ev.AtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Source = ev.Source,
                Name = ev.Name,
                Level = ev.Level,
                Message = ev.Message,
                Category = ev.Category,
            };
            string json = JsonSerializer.Serialize(riga, JsonOptions);
            byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");

            lock (_gate)
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
                fs.Write(bytes, 0, bytes.Length);
            }

            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke("Scrittura della cronologia NINA non riuscita", ex);
            return false;
        }
    }

    /// <summary>
    /// Legge tutti gli eventi, ordinati per <see cref="NinaEvent.AtUtc"/> (stabile: a parità di istante resta l'ordine del file).
    /// File mancante: lista vuota. Righe illegibili o di forma errata: saltate e contate in <see cref="NinaEventsRead.MalformedLines"/>.
    /// Non lancia mai.
    /// </summary>
    public NinaEventsRead ReadAll()
    {
        try
        {
            lock (_gate)
            {
                if (!File.Exists(_path))
                {
                    return new NinaEventsRead([], 0);
                }

                var eventi = new List<NinaEvent>();
                int malformate = 0;
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    NinaEvent? parsed = ParseLine(line);
                    if (parsed is null)
                    {
                        malformate++;
                    }
                    else
                    {
                        eventi.Add(parsed);
                    }
                }

                return new NinaEventsRead(eventi.OrderBy(e => e.AtUtc).ToList(), malformate);
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke("Lettura della cronologia NINA non riuscita", ex);
            return new NinaEventsRead([], 0);
        }
    }

    private static NinaEvent? ParseLine(string line)
    {
        try
        {
            NinaEventoRiga? riga = JsonSerializer.Deserialize<NinaEventoRiga>(line, JsonOptions);
            if (riga is null
                || string.IsNullOrEmpty(riga.Name)
                || riga.Message is null
                || riga.Source is not (NinaEvent.SourceWebSocket or NinaEvent.SourceLog)
                || !DateTimeOffset.TryParse(riga.AtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at))
            {
                return null;
            }

            return new NinaEvent(at.ToUniversalTime(), riga.Source, riga.Name, riga.Level, riga.Message, riga.Category);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsValidNightId(string? s)
    {
        return s is not null
            && System.Text.RegularExpressions.Regex.IsMatch(s, @"^[0-9]{4}-[0-9]{2}-[0-9]{2}\z")
            && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    /// <summary>Forma di una riga del file. Solo per la (de)serializzazione.</summary>
    internal sealed class NinaEventoRiga
    {
        public string? AtUtc { get; set; }

        public string? Source { get; set; }

        public string? Name { get; set; }

        public string? Level { get; set; }

        public string? Message { get; set; }

        public string? Category { get; set; }
    }
}

/// <summary>Esito della lettura della cronologia.</summary>
/// <param name="Events">Eventi ordinati per istante UTC.</param>
/// <param name="MalformedLines">Righe saltate perche' illegibili o di forma errata.</param>
public sealed record NinaEventsRead(IReadOnlyList<NinaEvent> Events, int MalformedLines);
