using System.Globalization;

namespace AllSky.Core.Events;

/// <summary>Una riga della tabella dei dati di un evento. <paramref name="Tooltip"/> spiega i valori mancanti.</summary>
/// <param name="Label">Nome del dato, nella lingua scelta.</param>
/// <param name="Value">Valore formattato, con unità. "—" se non disponibile.</param>
/// <param name="Tooltip">Testo di aiuto, o null.</param>
public sealed record EventDataRow(string Label, string Value, string? Tooltip = null);

/// <summary>
/// Testi e formati della vista Eventi: nomi di tipo, classe ed etichetta, ore, angoli e tabella dei dati.
/// Logica pura, testabile senza WPF. I valori non disponibili diventano "—" con un tooltip, mai zero.
/// </summary>
public static class EventFormatter
{
    /// <summary>Carattere per un valore non disponibile.</summary>
    public const string Missing = "—";

    /// <summary>Nome del tipo di evento.</summary>
    public static string CategoryName(EventCategory category, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return category switch
        {
            EventCategory.Meteor => texts.EventsCategoryMeteor,
            EventCategory.Bolide => texts.EventsCategoryBolide,
            EventCategory.Transit => texts.EventsCategoryTransit,
            _ => Missing,
        };
    }

    /// <summary>Nome della classe assegnata dal classificatore.</summary>
    public static string ClassName(EventClassKind kind, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return kind switch
        {
            EventClassKind.Meteor => texts.EventsClassMeteor,
            EventClassKind.Bolide => texts.EventsClassBolide,
            EventClassKind.Satellite => texts.EventsClassSatellite,
            EventClassKind.AircraftOrSatellite => texts.EventsClassAircraft,
            _ => texts.EventsClassUnknown,
        };
    }

    /// <summary>Nome dell'etichetta dell'utente.</summary>
    public static string LabelName(EventLabelChoice label, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return label switch
        {
            EventLabelChoice.Confirmed => texts.EventsLabelConfirmed,
            EventLabelChoice.FalsePositive => texts.EventsLabelFalsePositive,
            _ => texts.EventsLabelNone,
        };
    }

    /// <summary>Nome dell'esito della conferma di un transito.</summary>
    public static string OutcomeName(TransitOutcome outcome, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return outcome switch
        {
            TransitOutcome.Identified => texts.EventsVerdictIdentified,
            TransitOutcome.NotIdentified => texts.EventsVerdictNotIdentified,
            _ => texts.EventsVerdictPredicted,
        };
    }

    /// <summary>Titolo di una riga della galleria: tipo, ora locale e punteggio, per esempio "Meteora · 21:04:33 · 3.25".</summary>
    public static string ListCaption(EventListItem item, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(texts);

        string time = item.TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return $"{CategoryName(item.Category, texts)} · {time} · {FormatScore(item.Score, texts)}";
    }

    /// <summary>Data e ora locali, "yyyy-MM-dd HH:mm:ss".</summary>
    public static string FormatLocalDateTime(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Sola ora locale, "HH:mm:ss".</summary>
    public static string FormatLocalTime(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Istante UTC per il tooltip, "yyyy-MM-dd HH:mm:ss UTC".</summary>
    public static string FormatUtc(DateTimeOffset utc) =>
        utc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>Punteggio con due decimali, per esempio "3.25".</summary>
    public static string FormatScore(double score, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return double.IsFinite(score) ? score.ToString("0.00", texts.Culture) : Missing;
    }

    /// <summary>Lunghezza in pixel del frame ridotto, con un decimale e l'unità, per esempio "61.5 px".</summary>
    public static string FormatLength(double lengthPx, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return double.IsFinite(lengthPx) ? lengthPx.ToString("0.0", texts.Culture) + " px" : Missing;
    }

    /// <summary>Luminosità con un decimale, senza unità perché è in livelli a 8 bit.</summary>
    public static string FormatLuma(double value, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return double.IsFinite(value) ? value.ToString("0.0", texts.Culture) : Missing;
    }

    /// <summary>Altezza e azimut con un decimale e il simbolo dei gradi, per esempio "41.3° / 183.5°".</summary>
    public static string FormatSkyPoint(SkyPoint? point, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (point is null || !double.IsFinite(point.AltDeg) || !double.IsFinite(point.AzDeg))
        {
            return Missing;
        }

        return string.Format(texts.Culture, "{0:0.0}° / {1:0.0}°", point.AltDeg, point.AzDeg);
    }

    /// <summary>
    /// Immagine principale del dettaglio: la composita per un transito, il ritaglio ingrandito per una scia. Se manca,
    /// ripiega sul frame intero. Null se l'evento non ha nessuna immagine.
    /// </summary>
    public static string? PreferredImagePath(EventDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return detail.Category == EventCategory.Transit
            ? detail.CompositePath ?? detail.FramePath
            : detail.CropPath ?? detail.FramePath;
    }

    /// <summary>
    /// Tabella dei dati dell'evento. Le righe dei transiti compaiono solo se <see cref="EventDetail.Transit"/> è presente.
    /// Alt/az mancanti hanno un tooltip che spiega perché.
    /// </summary>
    public static IReadOnlyList<EventDataRow> DataRows(EventDetail detail, DesktopTexts texts)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(texts);

        string? skyTooltip = detail.Start is null && detail.End is null ? texts.EventsNoSkyPosition : null;
        var rows = new List<EventDataRow>
        {
            new(texts.EventsDataTime, FormatLocalDateTime(detail.TimestampUtc), FormatUtc(detail.TimestampUtc)),
            new(texts.EventsDataClass, ClassName(detail.Class, texts)),
            new(texts.EventsDataScore, FormatScore(detail.Score, texts)),
            new(texts.EventsDataLength, FormatLength(detail.LengthPx, texts)),
            new(texts.EventsDataPeak, FormatLuma(detail.PeakLuma, texts)),
            new(texts.EventsDataIntegrated, FormatLuma(detail.IntegratedLuma, texts)),
            new(texts.EventsDataStart, FormatSkyPoint(detail.Start, texts), detail.Start is null ? skyTooltip : null),
            new(texts.EventsDataEnd, FormatSkyPoint(detail.End, texts), detail.End is null ? skyTooltip : null),
        };

        TransitInfo? transit = detail.Transit;
        if (transit is not null)
        {
            rows.Add(new EventDataRow(texts.EventsDataSatellite, transit.SatelliteName));
            rows.Add(new EventDataRow(texts.EventsDataNorad, transit.NoradId.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new EventDataRow(
                texts.EventsDataWindow,
                FormatLocalTime(transit.StartUtc) + " – " + FormatLocalTime(transit.EndUtc),
                FormatUtc(transit.StartUtc) + " – " + FormatUtc(transit.EndUtc)));
            rows.Add(new EventDataRow(texts.EventsDataCulmination, FormatLocalTime(transit.CulminationUtc), FormatUtc(transit.CulminationUtc)));
            rows.Add(new EventDataRow(texts.EventsDataMaxElevation, string.Format(texts.Culture, "{0:0.0}°", transit.MaxElevationDeg)));
            rows.Add(new EventDataRow(texts.EventsDataConfirmation, FormatScore(transit.ConfirmationScore, texts)));
            rows.Add(new EventDataRow(texts.EventsDataVerdict, OutcomeName(transit.Outcome, texts)));
        }

        return rows;
    }
}
