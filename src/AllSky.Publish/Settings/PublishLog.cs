using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AllSky.Core;

namespace AllSky.Publish;

/// <summary>
/// Logger a file del modulo di pubblicazione, thread-safe.
/// Righe: <c>2026-10-21T21:30:00.123Z [INFO] testo</c> in <c>publish-YYYYMMDD.log</c> (data UTC).
/// Ogni testo e ogni messaggio d'eccezione passa per la funzione di sanificazione (per togliere i segreti)
/// e diventa una riga sola di al massimo 500 caratteri di testo (a capo convertiti in spazi).
/// Eccezioni: solo tipo e messaggio, mai lo stack trace.
/// Non lancia mai: ogni operazione di I/O e' protetta. Alla prima scrittura di ogni giorno elimina i file
/// publish-*.log piu' vecchi di 14 giorni.
/// </summary>
public sealed class PublishLog
{
    /// <summary>Lunghezza massima del testo di una riga (senza il prefisso data e livello).</summary>
    public const int MaxMessageLength = 500;

    /// <summary>Giorni di conservazione dei file di log.</summary>
    public const int RetentionDays = 14;

    private static readonly Regex FileNamePattern = new(@"^publish-(\d{8})\.log\z", RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly object _gate = new();
    private readonly string _folder;
    private readonly IClock _clock;
    private readonly Func<string, string>? _sanitize;
    private string? _rotatedForDay;

    /// <param name="folder">Cartella dei file di log (creata se manca).</param>
    /// <param name="clock">Orologio, per data e ora delle righe e per la rotazione.</param>
    /// <param name="sanitize">Funzione che rimuove i segreti dal testo; se null il testo e' scritto cosi' com'e'.</param>
    public PublishLog(string folder, IClock clock, Func<string, string>? sanitize = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        _folder = folder;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _sanitize = sanitize;
    }

    /// <summary>Scrive una riga di livello INFO.</summary>
    public void Info(string text) => Write("INFO", text, null);

    /// <summary>Scrive una riga di livello WARN.</summary>
    public void Warn(string text) => Write("WARN", text, null);

    /// <summary>Scrive una riga di livello ERROR; con <paramref name="ex"/> aggiunge tipo e messaggio dell'eccezione.</summary>
    public void Error(string text, Exception? ex = null) => Write("ERROR", text, ex);

    /// <summary>Restituisce il log come callback Action&lt;string&gt; (livello INFO).</summary>
    public Action<string> AsInfo() => Info;

    private void Write(string level, string text, Exception? ex)
    {
        try
        {
            lock (_gate)
            {
                DateTime now = _clock.UtcNow.UtcDateTime;
                string day = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

                string message = Clean(text);
                if (ex is not null)
                    message += " | " + Clean(ex.GetType().FullName ?? ex.GetType().Name) + ": " + Clean(ex.Message);
                if (message.Length > MaxMessageLength)
                    message = message[..(MaxMessageLength - 3)] + "...";

                string stamp = now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                string line = stamp + " [" + level + "] " + message + Environment.NewLine;

                Directory.CreateDirectory(_folder);
                if (_rotatedForDay != day)
                {
                    RotateOldFiles(now.Date);
                    _rotatedForDay = day;
                }

                File.AppendAllText(Path.Combine(_folder, "publish-" + day + ".log"), line, Utf8NoBom);
            }
        }
        catch (Exception)
        {
            // Il log non deve mai far cadere il modulo.
        }
    }

    /// <summary>Applica la sanificazione e rende il testo una riga sola.</summary>
    private string Clean(string? value)
    {
        string safe;
        try
        {
            safe = _sanitize is null ? value ?? string.Empty : _sanitize(value ?? string.Empty);
        }
        catch (Exception)
        {
            // Se la sanificazione fallisce non si scrive il testo originale.
            return "(testo non registrabile)";
        }

        var sb = new StringBuilder(safe.Length);
        foreach (char c in safe.Replace("\r\n", " "))
            sb.Append(char.IsControl(c) ? ' ' : c);
        return sb.ToString();
    }

    /// <summary>Elimina i publish-YYYYMMDD.log piu' vecchi di RetentionDays rispetto a <paramref name="today"/>.</summary>
    private void RotateOldFiles(DateTime today)
    {
        try
        {
            if (!Directory.Exists(_folder))
                return;

            DateTime cutoff = today.AddDays(-RetentionDays);
            foreach (string path in Directory.EnumerateFiles(_folder, "publish-*.log"))
            {
                Match match = FileNamePattern.Match(Path.GetFileName(path));
                if (!match.Success)
                    continue;
                if (!DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime fileDay))
                    continue;

                if (fileDay < cutoff)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception)
                    {
                        // Best effort: riprovera' alla prossima rotazione.
                    }
                }
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
