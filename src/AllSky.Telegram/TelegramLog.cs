using AllSky.Publish;

namespace AllSky.Telegram;

/// <summary>
/// Registro del modulo Telegram. Ogni riga passa dal <see cref="Redactor"/> prima di essere scritta, quindi il token
/// non compare nel log nemmeno se qualcuno lo passa per errore in un messaggio. Un errore del destinatario non
/// interrompe mai l'invio.
/// </summary>
public sealed class TelegramLog
{
    private readonly Action<string> _sink;
    private readonly Redactor _redactor;

    public TelegramLog(Action<string> sink, Redactor redactor)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("AVVISO", message);

    private void Write(string level, string message)
    {
        string line = "[" + level + "] " + _redactor.Sanitize(message);
        try
        {
            _sink(line);
        }
        catch (Exception)
        {
            // Il registro non deve interrompere l'invio.
        }
    }
}
