using AllSky.YouTube;

namespace AllSky.YouTube.Tests;

/// <summary>Dati di esempio condivisi dai test dei modelli.</summary>
internal static class Fixtures
{
    /// <summary>Notte del 21 ottobre 2026, ripresa alle 22:15 (ora legale, +02:00).</summary>
    public static TitleContext Sample() => new(
        new DateTimeOffset(2026, 10, 21, 22, 15, 0, TimeSpan.FromHours(2)),
        new DateTime(2026, 10, 21),
        "Osservatorio",
        120,
        new TimeSpan(0, 1, 5),
        3,
        1);

    /// <summary>Istante locale di Europe/Rome per un istante UTC; senza fuso orario di sistema usa l'offset indicato.</summary>
    public static DateTimeOffset RomeFromUtc(DateTime utc, TimeSpan fallbackOffset)
    {
        TimeZoneInfo? rome = FindRome();
        return rome is null
            ? new DateTimeOffset(utc.Add(fallbackOffset), fallbackOffset)
            : TimeZoneInfo.ConvertTime(new DateTimeOffset(utc, TimeSpan.Zero), rome);
    }

    private static TimeZoneInfo? FindRome()
    {
        foreach (string id in new[] { "Europe/Rome", "W. Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }
        return null;
    }
}
