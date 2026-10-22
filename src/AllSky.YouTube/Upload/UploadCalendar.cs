using System.Globalization;

namespace AllSky.YouTube;

/// <summary>Conversioni di fuso orario per giorno locale, ora di caricamento e mezzanotte. Usa il fuso passato dall'Host.</summary>
internal sealed class UploadCalendar
{
    private readonly TimeZoneInfo _zone;

    public UploadCalendar(TimeZoneInfo zone)
    {
        _zone = zone ?? throw new ArgumentNullException(nameof(zone));
    }

    public DateTimeOffset ToLocal(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, _zone);

    public DateOnly LocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(ToLocal(utc).DateTime);

    /// <summary>Chiave del giorno locale, "yyyy-MM-dd".</summary>
    public static string Key(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Istante UTC dell'ora di caricamento in un dato giorno locale.</summary>
    public DateTimeOffset SlotUtc(DateOnly date, TimeOnly time)
    {
        DateTime local = date.ToDateTime(time);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }

    /// <summary>Inizio del giorno locale successivo, come istante UTC.</summary>
    public DateTimeOffset NextMidnightUtc(DateTimeOffset utc)
    {
        DateTime local = LocalDate(utc).AddDays(1).ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }

    /// <summary>Data e ora locali per i messaggi all'utente.</summary>
    public string Format(DateTimeOffset utc) => ToLocal(utc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
