using System.Globalization;

namespace AllSky.Core.Nights;

/// <summary>Stato del timelapse di una notte, come lo mostra la vista "Notti".</summary>
public enum NightTimelapseState
{
    /// <summary>Nessun timelapse e nessuna lavorazione.</summary>
    None,

    /// <summary>Il file è pronto alla riproduzione.</summary>
    Ready,

    /// <summary>In coda o in lavorazione.</summary>
    Generating,

    /// <summary>L'ultima generazione non è riuscita.</summary>
    Failed,

    /// <summary>Valore non riconosciuto: non si mostra come "nessuno".</summary>
    Unknown,
}

/// <summary>Conversione del campo <c>timelapse</c> di <c>GET /api/nights</c> nello stato della vista.</summary>
public static class NightTimelapseStates
{
    /// <summary>Converte "none", "ready", "generating" o "failed"; qualsiasi altro valore diventa <see cref="NightTimelapseState.Unknown"/>.</summary>
    /// <param name="wire">Valore del campo, o null.</param>
    public static NightTimelapseState FromWire(string? wire) => wire switch
    {
        "none" => NightTimelapseState.None,
        "ready" => NightTimelapseState.Ready,
        "generating" => NightTimelapseState.Generating,
        "failed" => NightTimelapseState.Failed,
        _ => NightTimelapseState.Unknown,
    };
}

/// <summary>
/// Dati di una notte copiati dall'Engine, senza dipendere dai suoi DTO. Le date sono UTC.
/// </summary>
/// <param name="Id">Data di inizio della notte, YYYY-MM-DD.</param>
/// <param name="Frames">Numero di frame archiviati; zero se la notte è vuota.</param>
/// <param name="FirstFrame">Primo frame in UTC, null se la notte è vuota.</param>
/// <param name="LastFrame">Ultimo frame in UTC, null se la notte è vuota.</param>
/// <param name="HasKeogram">Vero se esiste keogram.jpg.</param>
/// <param name="HasStartrails">Vero se esiste startrails.jpg.</param>
/// <param name="Timelapse">Stato del timelapse.</param>
public sealed record NightSummary(
    string Id,
    int Frames,
    DateTimeOffset? FirstFrame,
    DateTimeOffset? LastFrame,
    bool HasKeogram,
    bool HasStartrails,
    NightTimelapseState Timelapse);

/// <summary>Una riga dell'elenco notti, già nella lingua scelta.</summary>
/// <param name="Id">Identificativo della notte, usato per la selezione.</param>
/// <param name="Title">Data della notte, es. "21 ottobre 2026"; l'identificativo grezzo se la data non è valida.</param>
/// <param name="Subtitle">Numero di frame e, se presente, l'intervallo di ora locale del primo e ultimo frame.</param>
/// <param name="Timelapse">Stato del timelapse, anche come codice per l'icona.</param>
/// <param name="TimelapseLabel">Stato del timelapse nella lingua scelta.</param>
public sealed record NightRow(string Id, string Title, string Subtitle, NightTimelapseState Timelapse, string TimelapseLabel);

/// <summary>Formatta le righe dell'elenco notti. Logica pura: nessuna dipendenza da WPF.</summary>
public static class NightRowFormatter
{
    /// <summary>Formatta una notte per l'elenco.</summary>
    /// <param name="night">Dati della notte.</param>
    /// <param name="texts">Testi e cultura della lingua scelta.</param>
    public static NightRow Format(NightSummary night, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(night);
        ArgumentNullException.ThrowIfNull(texts);

        string frames = string.Format(texts.Culture, texts.NightsFramesCount, night.Frames);
        string span = FormatSpan(night.FirstFrame, night.LastFrame);
        string subtitle = span.Length == 0 ? frames : frames + " · " + span;

        return new NightRow(
            night.Id,
            FormatDate(night.Id, texts.Culture),
            subtitle,
            night.Timelapse,
            TimelapseLabel(night.Timelapse, texts));
    }

    /// <summary>Data di una notte in formato esteso, es. "21 ottobre 2026". Se l'identificativo non è una data, lo restituisce invariato.</summary>
    /// <param name="id">Identificativo YYYY-MM-DD.</param>
    /// <param name="culture">Cultura della lingua scelta.</param>
    public static string FormatDate(string id, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(culture);

        return DateOnly.TryParseExact(id, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? date.ToString("d MMMM yyyy", culture)
            : id;
    }

    /// <summary>Etichetta dello stato del timelapse.</summary>
    /// <param name="state">Stato.</param>
    /// <param name="texts">Testi della lingua scelta.</param>
    public static string TimelapseLabel(NightTimelapseState state, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        return state switch
        {
            NightTimelapseState.None => texts.NightsTimelapseNone,
            NightTimelapseState.Ready => texts.NightsTimelapseReady,
            NightTimelapseState.Generating => texts.NightsTimelapseGenerating,
            NightTimelapseState.Failed => texts.NightsTimelapseFailed,
            _ => texts.NightsTimelapseUnknown,
        };
    }

    private static string FormatSpan(DateTimeOffset? first, DateTimeOffset? last)
    {
        if (first is null || last is null)
        {
            return string.Empty;
        }

        string from = first.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        string to = last.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        return from + "–" + to;
    }
}
