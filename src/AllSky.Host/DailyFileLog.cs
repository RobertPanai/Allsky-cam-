using System.Globalization;
using System.Text;

namespace AllSky.Host;

/// <summary>
/// Log su file con rotazione giornaliera: ogni riga va in <c>allsky-YYYYMMDD.log</c> nella cartella indicata,
/// in base alla data locale. Gli errori di scrittura non vengono mai rilanciati: il log non deve fermare l'applicazione.
/// </summary>
internal sealed class DailyFileLog
{
    private readonly string _folder;
    private readonly Func<string, string>? _sanitize;
    private readonly object _gate = new();

    /// <summary>Crea il log. La cartella viene creata alla prima scrittura.</summary>
    /// <param name="folder">Cartella dei file di log.</param>
    /// <param name="sanitize">Filtro applicato a tutto il testo prima della scrittura (segreti). Null: nessun filtro.</param>
    public DailyFileLog(string folder, Func<string, string>? sanitize = null)
    {
        _folder = folder;
        _sanitize = sanitize;
    }

    /// <summary>Scrive un messaggio, con l'eventuale eccezione completa, nel file del giorno.</summary>
    /// <param name="message">Messaggio in italiano.</param>
    /// <param name="exception">Eccezione da includere, se presente.</param>
    public void Write(string message, Exception? exception = null)
    {
        DateTime now = DateTime.Now;
        var text = new StringBuilder();
        text.Append(now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append("  ")
            .AppendLine(message);
        if (exception is not null)
        {
            text.AppendLine(exception.ToString());
        }

        string line = Sanitize(text.ToString());
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                string file = Path.Combine(_folder, "allsky-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(file, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Log non scrivibile (disco pieno, permessi): nessuna eccezione verso l'applicazione.
            }
        }
    }

    /// <summary>Applica il filtro; se fallisce il testo originale non viene scritto.</summary>
    private string Sanitize(string text)
    {
        if (_sanitize is null)
        {
            return text;
        }

        try
        {
            return _sanitize(text);
        }
        catch (Exception)
        {
            return "(testo non registrabile: filtro dei segreti non disponibile)" + Environment.NewLine;
        }
    }
}
