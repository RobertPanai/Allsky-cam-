namespace AllSky.YouTube;

/// <summary>
/// Prossimo istante utile del caricamento automatico, per la vista. Usa le stesse regole di
/// <see cref="YouTubeUploadScheduler"/>, ma non chiama la rete e non modifica lo stato.
/// </summary>
public static class UploadSchedule
{
    /// <summary>
    /// Istante UTC del prossimo tentativo del pianificatore. Se l'ora di oggi e' gia' passata ma ancora nella finestra di
    /// recupero, il tentativo e' "adesso" (il tick successivo lo esegue). Oltre la finestra si passa a domani.
    /// </summary>
    /// <param name="nowUtc">Istante corrente.</param>
    /// <param name="zone">Fuso orario dell'osservatorio.</param>
    /// <param name="dailyTime">Ora locale del caricamento.</param>
    /// <param name="catchUpWindow">Finestra di recupero dopo l'ora prevista.</param>
    /// <param name="lastSlotDate">Ultimo giorno locale gia' elaborato ("yyyy-MM-dd"), o null.</param>
    public static DateTimeOffset NextAttemptUtc(
        DateTimeOffset nowUtc,
        TimeZoneInfo zone,
        TimeOnly dailyTime,
        TimeSpan catchUpWindow,
        string? lastSlotDate)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var calendar = new UploadCalendar(zone);
        DateOnly today = calendar.LocalDate(nowUtc);
        if (lastSlotDate != UploadCalendar.Key(today))
        {
            DateTimeOffset slot = calendar.SlotUtc(today, dailyTime);
            if (nowUtc < slot)
                return slot;
            if (nowUtc - slot <= catchUpWindow)
                return nowUtc;
        }

        return calendar.SlotUtc(today.AddDays(1), dailyTime);
    }
}
