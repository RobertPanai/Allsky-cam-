namespace AllSky.Studio;

/// <summary>Esito della conversione di un orario locale in UTC.</summary>
internal enum LocalTimeNote
{
    /// <summary>Orario unico nel fuso: conversione normale.</summary>
    None,

    /// <summary>Orario che compare due volte (fine dell'ora legale): scelta la prima occorrenza.</summary>
    Ambiguous,

    /// <summary>Orario che non esiste (inizio dell'ora legale): usato l'offset in vigore prima del salto.</summary>
    Nonexistent,
}

/// <summary>
/// Conversione di un orario locale senza fuso (come quello di EXIF o del nome file) in UTC.
/// <para>
/// Scelte documentate per gli orari ambigui e inesistenti (passaggio ora legale/solare):
/// <list type="bullet">
/// <item>Ambiguo: si sceglie l'occorrenza più antica in UTC, cioè quella con l'offset più grande (ora legale).
/// Il risultato è segnalato con <see cref="LocalTimeNote.Ambiguous"/> e la scansione lo conta in un avviso.</item>
/// <item>Inesistente: si usa l'offset in vigore prima del salto (ora solare), cioè la stessa ora "di prima"
/// interpretata con l'orologio della macchina che non ha ancora fatto il salto. Segnalato con
/// <see cref="LocalTimeNote.Nonexistent"/>. Il frame non viene scartato.</item>
/// </list>
/// Senza fuso (<c>zone</c> null) l'orario è trattato come UTC.
/// </para>
/// </summary>
internal static class LocalTime
{
    /// <summary>Risolve un identificatore di fuso (IANA o Windows) con <see cref="TimeZoneInfo"/>.</summary>
    internal static bool TryResolveZone(string? id, out TimeZoneInfo? zone)
    {
        zone = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Converte un orario locale (Kind ignorato) in UTC.</summary>
    /// <param name="local">Orario locale senza offset.</param>
    /// <param name="zone">Fuso orario, oppure null per trattare l'orario come UTC.</param>
    /// <param name="note">Esito della conversione.</param>
    internal static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo? zone, out LocalTimeNote note)
    {
        note = LocalTimeNote.None;
        DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone is null)
        {
            return Utc(unspecified);
        }

        if (zone.IsInvalidTime(unspecified))
        {
            note = LocalTimeNote.Nonexistent;
            // Offset in vigore qualche ora prima: al momento del salto l'orologio non è ancora cambiato.
            TimeSpan before = zone.GetUtcOffset(unspecified.AddHours(-3));
            return Utc(unspecified - before);
        }

        if (zone.IsAmbiguousTime(unspecified))
        {
            note = LocalTimeNote.Ambiguous;
            TimeSpan chosen = zone.GetAmbiguousTimeOffsets(unspecified).Max();
            return Utc(unspecified - chosen);
        }

        return Utc(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone));
    }

    private static DateTimeOffset Utc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.Zero);
    }
}
