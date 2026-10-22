using System.Globalization;
using System.Text.RegularExpressions;

namespace AllSky.Nina;

/// <summary>
/// Lettura di una riga del log di NINA. Ogni implementazione descrive UN formato. Non lancia eccezioni:
/// una riga che non corrisponde restituisce un esito senza evento, con motivo "formato non riconosciuto".
/// </summary>
public interface INinaLogParser
{
    /// <summary>Interpreta una riga. <paramref name="clockOffsetSeconds"/> viene applicato al timestamp.</summary>
    NinaEventResult Parse(string? line, double clockOffsetSeconds);
}

/// <summary>
/// [NV] Formato IPOTIZZATO, non verificato su un log reale di NINA (docs/NINA-API.md §5: formato delle righe [NV]).
/// Ipotesi: <c>yyyy-MM-ddTHH:mm:ss[.fffffff]</c> con offset (<c>Z</c> o <c>±hh:mm</c>), uno spazio, il livello tra
/// parentesi quadre (INF/INFO, WRN/WARN, ERR/ERROR, DBG/DEBUG, TRC/TRACE, senza distinzione di maiuscole), uno spazio,
/// il messaggio. Esempio ipotetico: <c>2026-10-22T22:01:05.123+02:00 [INF] Messaggio</c>.
/// Da confermare o sostituire con un log reale di Robert (P11.13). Le righe di altra forma non sono interpretate.
/// </summary>
public sealed class UnverifiedNinaLogParser : INinaLogParser
{
    /// <summary>Motivo restituito per ogni riga che non corrisponde al formato.</summary>
    public const string UnrecognizedReason = "formato non riconosciuto";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    // [NV] Struttura ipotizzata della riga (vedi commento della classe).
    private static readonly Regex LineRegex = new(
        @"^(?<ts>[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2}))\s+\[(?<lvl>[A-Za-z]{3,7})\]\s?(?<msg>.*)\z",
        RegexOptions.CultureInvariant, RegexTimeout);

    // [NV] Forme del timestamp ipotizzate, con e senza frazione di secondo.
    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mm:ssK",
    ];

    // [NV] Livelli ipotizzati e loro nome normalizzato.
    private static readonly Dictionary<string, string> Levels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INF"] = NinaLogLevels.Info,
        ["INFO"] = NinaLogLevels.Info,
        ["WRN"] = NinaLogLevels.Warning,
        ["WARN"] = NinaLogLevels.Warning,
        ["WARNING"] = NinaLogLevels.Warning,
        ["ERR"] = NinaLogLevels.Error,
        ["ERROR"] = NinaLogLevels.Error,
        ["DBG"] = NinaLogLevels.Debug,
        ["DEBUG"] = NinaLogLevels.Debug,
        ["TRC"] = NinaLogLevels.Trace,
        ["TRACE"] = NinaLogLevels.Trace,
    };

    /// <inheritdoc />
    public NinaEventResult Parse(string? line, double clockOffsetSeconds)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return NinaEventResult.Rejected(UnrecognizedReason);
        }

        try
        {
            Match match = LineRegex.Match(line);
            if (!match.Success)
            {
                return NinaEventResult.Rejected(UnrecognizedReason);
            }

            if (!DateTimeOffset.TryParseExact(
                    match.Groups["ts"].Value, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset local))
            {
                return NinaEventResult.Rejected(UnrecognizedReason);
            }

            if (!Levels.TryGetValue(match.Groups["lvl"].Value, out string? level))
            {
                return NinaEventResult.Rejected(UnrecognizedReason);
            }

            DateTimeOffset at = NinaClock.ToUtc(local, clockOffsetSeconds);
            string text = NinaMessageSanitizer.Clean(match.Groups["msg"].Value);
            return NinaEventResult.Ok(new NinaEvent(at, NinaEvent.SourceLog, NinaEvent.LogLineName, level, text, null));
        }
        catch (RegexMatchTimeoutException)
        {
            return NinaEventResult.Rejected(UnrecognizedReason);
        }
        catch (ArgumentOutOfRangeException)
        {
            return NinaEventResult.Rejected("correzione dell'orologio fuori intervallo");
        }
    }
}
