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
    private readonly object _gate = new();

    /// <summary>Crea il log. La cartella viene creata alla prima scrittura.</summary>
    /// <param name="folder">Cartella dei file di log.</param>
    public DailyFileLog(string folder)
    {
        _folder = folder;
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

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                string file = Path.Combine(_folder, "allsky-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
                File.AppendAllText(file, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Log non scrivibile (disco pieno, permessi): nessuna eccezione verso l'applicazione.
            }
        }
    }
}
