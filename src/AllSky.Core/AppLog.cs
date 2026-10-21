using System.Text;

namespace AllSky.Core;

/// <summary>Scrittura dei log delle app Windows. Un errore di scrittura non deve mai far fallire l'app.</summary>
public static class AppLog
{
    /// <summary>Aggiunge una voce con data e ora al file indicato, creando le cartelle se mancano.</summary>
    /// <param name="filePath">File di log completo.</param>
    /// <param name="text">Testo della voce.</param>
    public static void Append(string filePath, string text)
    {
        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(filePath, entry, Encoding.UTF8);
        }
        catch (Exception)
        {
            // Il log è best-effort: se non si può scrivere, l'app continua o si chiude comunque.
        }
    }
}
