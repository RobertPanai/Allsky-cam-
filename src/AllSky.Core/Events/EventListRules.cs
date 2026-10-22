namespace AllSky.Core.Events;

/// <summary>
/// Filtro, ordinamento ed elenco delle notti per la galleria Eventi. Logica pura: nessun accesso al disco.
/// </summary>
public static class EventListRules
{
    /// <summary>Vero se la riga rispetta il filtro. Un campo null del filtro non filtra.</summary>
    /// <param name="item">Riga da controllare.</param>
    /// <param name="filter">Filtro da applicare.</param>
    public static bool Matches(EventListItem item, EventFilter filter)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(filter);

        return (filter.NightId is null || string.Equals(item.NightId, filter.NightId, StringComparison.Ordinal))
            && (filter.Category is null || item.Category == filter.Category.Value);
    }

    /// <summary>Righe che rispettano il filtro, nell'ordine di <see cref="Sort"/>.</summary>
    /// <param name="items">Righe di partenza.</param>
    /// <param name="filter">Filtro da applicare.</param>
    public static IReadOnlyList<EventListItem> Filter(IEnumerable<EventListItem> items, EventFilter filter)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(filter);

        return Sort(items.Where(item => Matches(item, filter)));
    }

    /// <summary>Ordina dal più recente al più vecchio. A parità di istante l'ordine è quello dell'identificatore, al contrario.</summary>
    /// <param name="items">Righe da ordinare.</param>
    public static IReadOnlyList<EventListItem> Sort(IEnumerable<EventListItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items
            .OrderByDescending(item => item.TimestampUtc)
            .ThenByDescending(item => item.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Notti presenti nelle righe, dalla più recente, senza duplicati.</summary>
    /// <param name="items">Righe di tutte le notti.</param>
    public static IReadOnlyList<string> Nights(IEnumerable<EventListItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items
            .Select(item => item.NightId)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(night => night, StringComparer.Ordinal)
            .ToList();
    }
}
