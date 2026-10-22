using System.Globalization;
using System.Text.RegularExpressions;
using AllSky.Archive;

namespace AllSky.Events;

/// <summary>
/// Identificatori degli eventi. Un id contiene solo lettere, cifre, '_' e '-': non puo' contenere separatori
/// di percorso ne' "..", quindi non puo' uscire dalla cartella degli eventi.
/// </summary>
public static class EventIds
{
    private static readonly Regex IdRegex = new(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,79}\z", RegexOptions.CultureInvariant);

    /// <summary>Vero se <paramref name="id"/> e' un identificatore di evento valido.</summary>
    public static bool IsValid(string? id) => id is not null && IdRegex.IsMatch(id);

    /// <summary>
    /// Id di una scia del frame: "&lt;frameId&gt;-&lt;indice a 3 cifre&gt;". Stabile: lo stesso frame e la stessa scia danno sempre lo stesso id.
    /// </summary>
    /// <param name="frameId">Identificatore del frame ("yyyyMMddTHHmmssZ").</param>
    /// <param name="streakIndex">Posizione della scia nella classificazione del frame, 0..999.</param>
    public static string ForStreak(string frameId, int streakIndex)
    {
        if (!NightArchive.IsValidFrameId(frameId))
        {
            throw new ArgumentException("Identificatore di frame non valido.", nameof(frameId));
        }
        if (streakIndex < 0 || streakIndex > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(streakIndex), streakIndex, "L'indice della scia deve stare in 0..999.");
        }

        return frameId + "-" + streakIndex.ToString("000", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Id di un transito: "transit-&lt;NORAD&gt;-&lt;inizio UTC "yyyyMMddTHHmmssZ"&gt;". Stabile: lo stesso passaggio
    /// (stesso satellite, stesso inizio) dà sempre lo stesso id, quindi non produce due eventi.
    /// </summary>
    /// <param name="noradId">Numero di catalogo NORAD, 1..999999999.</param>
    /// <param name="startUtc">Inizio del passaggio.</param>
    public static string ForTransit(int noradId, DateTimeOffset startUtc)
    {
        if (noradId < 1 || noradId > 999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(noradId), noradId, "Il numero NORAD deve stare in 1..999999999.");
        }

        return "transit-" + noradId.ToString(CultureInfo.InvariantCulture) + "-"
            + startUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }
}
