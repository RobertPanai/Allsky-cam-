using System.Globalization;
using System.Text.RegularExpressions;

namespace AllSky.Core.LogView;

/// <summary>Gravità di una voce, stimata dal testo (il log dell'Host non scrive il livello).</summary>
public enum LogLevel
{
    /// <summary>Informazione: avvio, arresto, cattura, salvataggi.</summary>
    Info = 0,

    /// <summary>Avviso: qualcosa è stato saltato o ripiegato, ma l'app continua.</summary>
    Warning = 1,

    /// <summary>Errore: un'operazione è fallita o è stata registrata un'eccezione.</summary>
    Error = 2,
}

/// <summary>Filtro per gravità della vista Log.</summary>
public enum LogLevelFilter
{
    /// <summary>Tutte le voci.</summary>
    All,

    /// <summary>Avvisi ed errori.</summary>
    WarningsAndErrors,

    /// <summary>Solo errori.</summary>
    ErrorsOnly,
}

/// <summary>Voce di log: istante (se leggibile), gravità stimata e testo già mascherato, con le righe di continuazione.</summary>
/// <param name="Timestamp">Istante locale della voce, o null se la riga non ne ha uno.</param>
/// <param name="Level">Gravità stimata.</param>
/// <param name="Text">Testo della voce, senza segreti.</param>
public sealed record LogRecord(DateTime? Timestamp, LogLevel Level, string Text);

/// <summary>
/// Lettura delle righe di log: raggruppa le righe di continuazione (tracce delle eccezioni) con la voce che le precede,
/// stima la gravità, maschera i segreti. Riconosce il formato di <c>DailyFileLog</c> (<c>data  testo</c>) e quello di
/// <c>AppLog</c> (<c>[data] testo</c>). Logica pura.
/// </summary>
public static class LogParser
{
    private static readonly Regex StartRegex = new(
        @"^\[?(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]?(?:[ \t]+(?<rest>.*))?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly string[] ErrorWords =
    [
        "exception", "eccezione", "errore", "error", "non riuscit", "fallit", "failed", "crash", "impossibile",
    ];

    private static readonly string[] WarningWords =
    [
        "attenzione", "avviso", "warning", "occupata", "non ammess", "non trovat", "non disponibil",
        "scaduto", "timeout", "riprov", "non completat", "saltat", "ignorat",
    ];

    /// <summary>Raggruppa le righe in voci. Le righe vuote in coda a una voce non fanno parte del testo.</summary>
    /// <param name="lines">Righe del file, in ordine.</param>
    public static IReadOnlyList<LogRecord> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var records = new List<LogRecord>();
        DateTime? timestamp = null;
        var body = new List<string>();
        bool open = false;

        void Flush()
        {
            if (!open)
            {
                return;
            }

            string raw = string.Join("\n", body).TrimEnd();
            string text = LogSecretMask.Apply(raw);
            records.Add(new LogRecord(timestamp, Classify(text), text));
            body.Clear();
            open = false;
        }

        foreach (string line in lines)
        {
            Match match = StartRegex.Match(line.TrimEnd('\r'));
            if (match.Success)
            {
                Flush();
                timestamp = DateTime.ParseExact(match.Groups["ts"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                body.Add(match.Groups["rest"].Value);
                open = true;
                continue;
            }

            if (!open)
            {
                // Righe prima della prima voce (per esempio il resto di un file letto dalla coda): voce senza istante.
                timestamp = null;
                open = true;
            }

            body.Add(line.TrimEnd('\r'));
        }

        Flush();
        return records;
    }

    /// <summary>Gravità stimata dal testo. Un errore prevale su un avviso.</summary>
    /// <param name="text">Testo della voce (già mascherato o no: il risultato non cambia).</param>
    public static LogLevel Classify(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return LogLevel.Info;
        }

        if (ContainsAny(text, ErrorWords))
        {
            return LogLevel.Error;
        }

        return ContainsAny(text, WarningWords) ? LogLevel.Warning : LogLevel.Info;
    }

    private static bool ContainsAny(string text, string[] words)
    {
        foreach (string word in words)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Filtro e taglio delle voci per la vista Log. Logica pura.</summary>
public static class LogFilter
{
    /// <summary>Voci che passano il filtro, le ultime <paramref name="maxRecords"/> in ordine cronologico.</summary>
    /// <param name="records">Voci lette, in ordine cronologico.</param>
    /// <param name="level">Filtro per gravità.</param>
    /// <param name="query">Testo da cercare (senza distinzione di maiuscole); vuoto o null per non filtrare.</param>
    /// <param name="maxRecords">Quante voci al massimo restituire, a partire dalle più recenti.</param>
    public static IReadOnlyList<LogRecord> Apply(IEnumerable<LogRecord> records, LogLevelFilter level, string? query, int maxRecords)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (maxRecords < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRecords), maxRecords, "Il numero di voci deve essere almeno 1.");
        }

        string search = query?.Trim() ?? string.Empty;
        var matching = records
            .Where(r => Passes(r.Level, level))
            .Where(r => search.Length == 0 || r.Text.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        int skip = Math.Max(0, matching.Count - maxRecords);
        return matching.Skip(skip).ToList();
    }

    /// <summary>Vero se la gravità passa il filtro.</summary>
    /// <param name="level">Gravità della voce.</param>
    /// <param name="filter">Filtro scelto.</param>
    public static bool Passes(LogLevel level, LogLevelFilter filter) => filter switch
    {
        LogLevelFilter.All => true,
        LogLevelFilter.WarningsAndErrors => level >= LogLevel.Warning,
        LogLevelFilter.ErrorsOnly => level == LogLevel.Error,
        _ => true,
    };
}
